using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// LLM-backed player agent that sends full game state (including shadow thresholds
    /// and strategic rules) to Anthropic's Claude API via tool_use for structured output.
    /// Falls back to ScoringPlayerAgent on any failure.
    /// </summary>
    public sealed class LlmPlayerAgent : IPlayerAgent, IDisposable
    {

        /// <summary>
        /// Tool definition for structured output via Anthropic tool_use.
        /// Loads the JSON schema from a static resource file rather than inlining a parsed JSON string.
        /// </summary>
        private static readonly ToolDefinition SubmitChoiceTool = LoadSubmitChoiceTool();

        private readonly AnthropicClient _client;
        private readonly ScoringPlayerAgent _fallback;
        private readonly string _model;
        private readonly string _playerName;
        private readonly string _dateeName;
        private readonly IRuleResolver? _ruleResolver;
        private readonly SimAgentPromptAssets _promptAssets;
        private bool _disposed;
        private readonly List<CallSummaryStat> _tokenStats = new List<CallSummaryStat>();

        /// <summary>
        /// The last explanation produced by the LLM agent. Empty string if no explanation available.
        /// </summary>
        public string LastExplanation { get; private set; } = "";

        internal LlmPlayerAgentFallbackDiagnostic? LastFallbackDiagnostic { get; private set; }

        internal Func<MessagesRequest, Task<MessagesResponse>>? SendMessagesAsyncOverride { get; set; }

        public ScoringMode ScoringMode => ScoringMode.Llm;
        public string MechanicsSource => "llm+heuristic:LlmPlayerAgent wraps ScoringPlayerAgent heuristic and makes strategic LLM choices";

        /// <summary>Returns per-call token stats for each llm-player-pick call made.</summary>
        public IReadOnlyList<CallSummaryStat> GetTokenStats() => _tokenStats.AsReadOnly();

        /// <summary>
        /// Creates an LLM-backed player agent.
        /// </summary>
        /// <param name="options">Anthropic API configuration (API key, model, etc.).</param>
        /// <param name="fallback">Deterministic scoring agent used on LLM failure.</param>
        /// <param name="playerName">Player character display name (optional, for prompt immersion).</param>
        /// <param name="dateeName">Datee character display name (optional, for prompt immersion).</param>
        /// <param name="ruleResolver">Dynamic rule resolver. When null, uses the host-registered DefaultRuleResolver.Instance.</param>
        /// <exception cref="ArgumentNullException">If options or fallback is null.</exception>
        public LlmPlayerAgent(
            AnthropicOptions options,
            ScoringPlayerAgent fallback,
            string playerName = "the player",
            string dateeName = "the datee",
            IRuleResolver? ruleResolver = null,
            PromptCatalog? promptCatalog = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _playerName = playerName ?? "the player";
            _dateeName = dateeName ?? "the datee";
            _model = options.Model;
            _client = new AnthropicClient(options.ApiKey);
            _ruleResolver = ruleResolver ?? DefaultRuleResolver.Instance;
            _promptAssets = SimAgentPromptAssets.Load(promptCatalog ?? LoadPromptCatalog());
        }

        private static ToolDefinition LoadSubmitChoiceTool()
        {
            string relativePath = Path.Combine("data", "schemas", "submit_choice_tool.json");
            string? path = DataFileLocator.FindDataFile(AppContext.BaseDirectory, relativePath);
            if (path == null || !File.Exists(path))
            {
                throw new FileNotFoundException($"Required player-agent tool schema asset was not found: {relativePath}");
            }

            JObject root;
            try
            {
                root = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException || ex is Newtonsoft.Json.JsonException)
            {
                throw new InvalidDataException($"Could not load player-agent tool schema asset '{path}'.", ex);
            }

            var inputSchema = root["input_schema"] as JObject
                ?? throw new InvalidDataException($"{path}: required object property 'input_schema' is missing.");

            return new ToolDefinition
            {
                Name = RequiredToolString(root, "name", path),
                Description = RequiredToolString(root, "description", path),
                InputSchema = inputSchema
            };
        }

        private static string RequiredToolString(JObject root, string propertyName, string path)
        {
            string? value = root.Value<string>(propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{path}: required string property '{propertyName}' is missing.");
            }

            return value!;
        }

        private static PromptCatalog LoadPromptCatalog()
        {
            string? repoRoot = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);
            string promptsDir = repoRoot != null 
                ? Path.Combine(repoRoot, "data", "prompts")
                : Path.Combine(AppContext.BaseDirectory, "data", "prompts");

            if (!Directory.Exists(promptsDir))
            {
                throw new DirectoryNotFoundException($"Required prompt directory was not found: {promptsDir}");
            }

            return PromptCatalog.LoadFromDirectory(promptsDir);
        }

        private static (int T1, int T2, int T3) ResolveShadowThresholds(IRuleResolver? resolver)
        {
            int t1 = 6;
            int t2 = 12;
            int t3 = 18;

            if (resolver == null) return (t1, t2, t3);

            bool foundT1 = false;
            bool foundT2 = false;
            bool foundT3 = false;

            for (int v = 1; v <= 25; v++)
            {
                int? lvl = resolver.GetShadowThresholdLevel(v);
                if (lvl == 1 && !foundT1)
                {
                    t1 = v;
                    foundT1 = true;
                }
                else if (lvl == 2 && !foundT2)
                {
                    t2 = v;
                    foundT2 = true;
                }
                else if (lvl == 3 && !foundT3)
                {
                    t3 = v;
                    foundT3 = true;
                }
            }

            return (t1, t2, t3);
        }

        private string ResolveSuccessRules(IRuleResolver? resolver)
        {
            int? tier1 = resolver?.GetSuccessInterestDelta(1, 10);
            int? tier2 = resolver?.GetSuccessInterestDelta(5, 10);
            int? tier3 = resolver?.GetSuccessInterestDelta(10, 10);
            int? nat20 = resolver?.GetSuccessInterestDelta(1, 20);

            string t1Str = tier1.HasValue ? $"+{tier1}" : "+1";
            string t2Str = tier2.HasValue ? $"+{tier2}" : "+2";
            string t3Str = tier3.HasValue ? $"+{tier3}" : "+3";
            string natStr = nat20.HasValue ? $"+{nat20}" : "+4";

            return _promptAssets.Render("sim_agent_success_rules", new Dictionary<string, string>
            {
                { "tier1", t1Str },
                { "tier2", t2Str },
                { "tier3", t3Str },
                { "nat20", natStr }
            });
        }

        private string ResolveFailureRules(IRuleResolver? resolver)
        {
            int? fumble = resolver?.GetFailureInterestDelta(1, 10);
            int? misfire = resolver?.GetFailureInterestDelta(3, 10);
            int? trap = resolver?.GetFailureInterestDelta(6, 10);
            int? catastrophe = resolver?.GetFailureInterestDelta(10, 10);
            int? legendary = resolver?.GetFailureInterestDelta(1, 1);

            string fStr = fumble.HasValue ? $"{fumble}" : "−1";
            string mStr = misfire.HasValue ? $"{misfire}" : "−1";
            string tStr = trap.HasValue ? $"{trap}" : "−2";
            string cStr = catastrophe.HasValue ? $"{catastrophe}" : "−3";
            string lStr = legendary.HasValue ? $"{legendary}" : "−4";

            return _promptAssets.Render("sim_agent_failure_rules", new Dictionary<string, string>
            {
                { "fumble", fStr },
                { "misfire", mStr },
                { "trap", tStr },
                { "catastrophe", cStr },
                { "legendary", lStr }
            });
        }

        private string ResolveMomentumRules(IRuleResolver? resolver)
        {
            var activeRules = new List<string>();
            for (int streak = 1; streak <= 10; streak++)
            {
                int? bonus = resolver?.GetMomentumBonus(streak);
                if (bonus.HasValue && bonus.Value > 0)
                {
                    int? prevBonus = streak > 1 ? resolver?.GetMomentumBonus(streak - 1) : null;
                    if (prevBonus != bonus.Value)
                    {
                        activeRules.Add(_promptAssets.Render("sim_agent_momentum_rules_threshold", new Dictionary<string, string>
                        {
                            { "streak", streak.ToString() },
                            { "bonus", bonus.Value.ToString() }
                        }));
                    }
                }
            }

            if (activeRules.Count > 0)
            {
                return string.Join(". ", activeRules) + ".";
            }
            return string.Join(". ", new[]
            {
                _promptAssets.Render("sim_agent_momentum_rules_threshold", new Dictionary<string, string>
                {
                    { "streak", "3" },
                    { "bonus", "2" }
                }),
                _promptAssets.Render("sim_agent_momentum_rules_threshold", new Dictionary<string, string>
                {
                    { "streak", "5" },
                    { "bonus", "3" }
                })
            }) + ".";
        }

        /// <summary>
        /// Sends the full game state to Claude with tool_use for structured output,
        /// parses the strategic pick and explanation.
        /// Falls back to ScoringPlayerAgent on any failure.
        /// </summary>
        public async Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (turn.Options.Length == 0)
                throw new InvalidOperationException("No options available");

            // Always compute scoring agent decision first — we need its Scores array
            var scoringDecision = await _fallback.DecideAsync(turn, context).ConfigureAwait(false);
            LastFallbackDiagnostic = null;

            try
            {
                string userMessage = BuildPrompt(turn, context, scoringDecision.Scores);
                string systemMsg = BuildSystemMessage(context);

                var request = new MessagesRequest
                {
                    Model = _model,
                    MaxTokens = 512,
                    Temperature = 0.3,
                    System = new[] { new ContentBlock { Type = "text", Text = systemMsg } },
                    Messages = new[] { new Message { Role = "user", Content = userMessage } },
                    Tools = new[] { SubmitChoiceTool },
                    ToolChoice = new ToolChoiceOption { Type = "tool", Name = SubmitChoiceTool.Name }
                };

                var sendMessagesAsync = SendMessagesAsyncOverride;
                var response = sendMessagesAsync != null
                    ? await sendMessagesAsync(request).ConfigureAwait(false)
                    : await _client.SendMessagesAsync(request).ConfigureAwait(false);

                // Record token usage for audit table
                if (response.Usage != null)
                {
                    _tokenStats.Add(new CallSummaryStat
                    {
                        Turn = turn.State.TurnNumber,
                        Type = "llm-player-pick",
                        InputTokens = response.Usage.InputTokens,
                        OutputTokens = response.Usage.OutputTokens,
                        CacheReadInputTokens = response.Usage.CacheReadInputTokens,
                        CacheCreationInputTokens = response.Usage.CacheCreationInputTokens
                    });
                }

                // Extract tool_use input
                JObject toolInput = response.GetToolInput();
                if (toolInput == null)
                {
                    // Fallback: try parsing text response for PICK: pattern
                    string responseText = response.GetText();
                    return HandleTextFallback(responseText, turn, context, scoringDecision);
                }

                int? choice = toolInput.Value<int?>("choice");
                string explanation = toolInput.Value<string>("explanation") ?? "";

                if (choice == null || choice.Value < 0 || choice.Value >= turn.Options.Length)
                {
                    return MakeFallbackDecision(
                        scoringDecision,
                        "LLM response invalid, defaulting to option 0",
                        null,
                        turn,
                        context,
                        0);
                }

                LastExplanation = !string.IsNullOrWhiteSpace(explanation)
                    ? explanation
                    : "No explanation provided";

                return new PlayerDecision(choice.Value, LastExplanation, scoringDecision.Scores);
            }
            catch (AnthropicApiException ex)
            {
                return MakeFallbackDecision(scoringDecision, $"Anthropic API error ({ex.StatusCode})", ex, turn, context);
            }
            catch (HttpRequestException ex)
            {
                return MakeFallbackDecision(scoringDecision, "Network error", ex, turn, context);
            }
            catch (TaskCanceledException ex)
            {
                return MakeFallbackDecision(scoringDecision, "Request timed out", ex, turn, context);
            }
            catch (Exception ex)
            {
                return MakeFallbackDecision(scoringDecision, "Unexpected player-agent error", ex, turn, context);
            }
        }

        private string BuildSystemMessage(PlayerAgentContext context)
        {
            var dict = BuildSubstitutionDict(null, context, null);
            return PromptCatalog.Substitute(_promptAssets.SystemPrompt, dict);
        }

        /// <summary>
        /// Builds the full LLM prompt with game state, shadow analysis, and option details.
        /// </summary>
        internal string BuildPrompt(TurnStart turn, PlayerAgentContext context, OptionScore[] scores = null)
        {
            var dict = BuildSubstitutionDict(turn, context, scores);
            return PromptCatalog.Substitute(_promptAssets.UserTemplate, dict);
        }

        private Dictionary<string, string> BuildSubstitutionDict(TurnStart? turn, PlayerAgentContext context, OptionScore[]? scores)
        {
            var (t1, t2, t3) = ResolveShadowThresholds(_ruleResolver);
            string successRules = ResolveSuccessRules(_ruleResolver);
            string failureRules = ResolveFailureRules(_ruleResolver);
            string momentumRules = ResolveMomentumRules(_ruleResolver);

            string playerNameValue = !string.IsNullOrEmpty(context.PlayerName) ? context.PlayerName : _playerName;
            string dateeNameValue = !string.IsNullOrEmpty(context.DateeName) ? context.DateeName : _dateeName;

            string modifierNoteValue = GetModifierNote(context.InterestState);
            string momentumNoteValue = GetMomentumNote(context.MomentumStreak);
            string activeTrapsValue = context.ActiveTrapNames.Length > 0
                ? string.Join(", ", context.ActiveTrapNames)
                : _promptAssets.Render("sim_agent_active_traps_none");

            string personalityBlockValue = "";
            if (!string.IsNullOrEmpty(context.PlayerSystemPrompt))
            {
                personalityBlockValue = _promptAssets.Render("sim_agent_personality_block", new Dictionary<string, string>
                {
                    { "playerSystemPrompt", context.PlayerSystemPrompt }
                });
            }

            var recentHistoryRows = new List<string>();
            if (context.RecentHistory != null && context.RecentHistory.Count > 0)
            {
                int start = Math.Max(0, context.RecentHistory.Count - 6);
                for (int i = start; i < context.RecentHistory.Count; i++)
                {
                    var entry = context.RecentHistory[i];
                    recentHistoryRows.Add(_promptAssets.Render("sim_agent_recent_history_row", new Dictionary<string, string>
                    {
                        { "sender", entry.Sender },
                        { "text", entry.Text }
                    }));
                }
            }
            string recentHistoryBlockValue = recentHistoryRows.Count > 0
                ? _promptAssets.Render("sim_agent_recent_history_block", new Dictionary<string, string>
                {
                    { "recentHistoryRows", string.Join(Environment.NewLine, recentHistoryRows) }
                }) + Environment.NewLine + Environment.NewLine
                : "";

            var shadowStatusSb = new System.Text.StringBuilder();
            if (context.ShadowValues != null)
            {
                foreach (ShadowStatType s in (ShadowStatType[])Enum.GetValues(typeof(ShadowStatType)))
                {
                    if (context.ShadowValues.TryGetValue(s, out int value))
                    {
                        string warning = "";
                        if (value >= t3) warning = _promptAssets.Render("sim_agent_shadow_warning_t3");
                        else if (value >= t2) warning = _promptAssets.Render("sim_agent_shadow_warning_t2");
                        else if (value >= t1) warning = _promptAssets.Render("sim_agent_shadow_warning_t1");
                        else if (value >= 4) warning = _promptAssets.Render("sim_agent_shadow_warning_approaching_t1");
                        shadowStatusSb.AppendLine(_promptAssets.Render("sim_agent_shadow_status_row", new Dictionary<string, string>
                        {
                            { "shadow", s.ToString() },
                            { "value", value.ToString(CultureInfo.InvariantCulture) },
                            { "t3", t3.ToString(CultureInfo.InvariantCulture) },
                            { "warning", warning }
                        }));
                    }
                }
            }
            else
            {
                shadowStatusSb.AppendLine(_promptAssets.Render("sim_agent_shadow_status_unavailable"));
            }
            string shadowStatusBlockValue = shadowStatusSb.ToString();

            string optionsBlockValue = "";
            if (turn != null)
            {
                var optionsSb = new System.Text.StringBuilder();
                char letter = 'A';
                for (int i = 0; i < turn.Options.Length; i++)
                {
                    DialogueOption opt = turn.Options[i];
                    int modifier = context.PlayerStats.GetEffective(opt.Stat);
                    int dc = context.DateeStats.GetDefenceDC(opt.Stat);
                    
                    int momentumBonus;
                    if (_ruleResolver != null)
                    {
                        momentumBonus = _ruleResolver.GetMomentumBonus(context.MomentumStreak) ?? (context.MomentumStreak >= 5 ? 3 : (context.MomentumStreak >= 3 ? 2 : 0));
                    }
                    else
                    {
                        momentumBonus = context.MomentumStreak >= 5 ? 3 : (context.MomentumStreak >= 3 ? 2 : 0);
                    }
                    
                    int tellBonus = opt.HasTellBonus ? 2 : 0;
                    
                    int callbackBonus = opt.CallbackTurnNumber.HasValue
                        ? CallbackBonus.Compute(context.TurnNumber, opt.CallbackTurnNumber.Value)
                        : 0;
                        
                    int totalMod = modifier + context.PlayerLevelBonus + momentumBonus + tellBonus + callbackBonus;
                    int need = dc - totalMod;
                    int pct = Math.Max(0, Math.Min(100, (21 - need) * 5));
                    string riskTier = GetRiskTier(need);
                    string icons = FormatBonusIcons(opt);

                    optionsSb.AppendLine(_promptAssets.Render("sim_agent_option_summary_row", new Dictionary<string, string>
                    {
                        { "letter", letter.ToString() },
                        { "statName", opt.Stat.ToString().ToUpperInvariant() },
                        { "modifier", modifier.ToString(CultureInfo.InvariantCulture) },
                        { "dc", dc.ToString(CultureInfo.InvariantCulture) },
                        { "need", need.ToString(CultureInfo.InvariantCulture) },
                        { "successPct", pct.ToString(CultureInfo.InvariantCulture) },
                        { "riskTier", riskTier },
                        { "icons", icons }
                    }));
                    optionsSb.AppendLine(_promptAssets.Render("sim_agent_option_text_row", new Dictionary<string, string>
                    {
                        { "intendedText", opt.IntendedText }
                    }));

                    string shadowImpact = GetShadowImpact(opt.Stat, context, t1, t2, t3);
                    if (!string.IsNullOrEmpty(shadowImpact))
                    {
                        optionsSb.AppendLine(_promptAssets.Render("sim_agent_option_shadow_row", new Dictionary<string, string>
                        {
                            { "shadowImpact", shadowImpact }
                        }));
                    }

                    if (scores != null && i < scores.Length)
                    {
                        optionsSb.AppendLine(_promptAssets.Render("sim_agent_option_ev_row", new Dictionary<string, string>
                        {
                            { "expectedInterestGain", scores[i].ExpectedInterestGain.ToString("F2", CultureInfo.InvariantCulture) }
                        }));
                    }

                    letter = (char)(letter + 1);
                }
                optionsBlockValue = optionsSb.ToString();
            }

            return new Dictionary<string, string>
            {
                { "playerName", playerNameValue },
                { "dateeName", dateeNameValue },
                { "personalityBlock", personalityBlockValue },
                { "recentHistoryBlock", recentHistoryBlockValue },
                { "currentInterest", context.CurrentInterest.ToString() },
                { "interestState", context.InterestState.ToString() },
                { "modifierNote", modifierNoteValue },
                { "momentumStreak", context.MomentumStreak.ToString() },
                { "momentumNote", momentumNoteValue },
                { "activeTraps", activeTrapsValue },
                { "turnNumber", context.TurnNumber.ToString() },
                { "shadowStatusBlock", shadowStatusBlockValue },
                { "optionsBlock", optionsBlockValue },
                { "successRules", successRules },
                { "failureRules", failureRules },
                { "momentumRules", momentumRules },
                { "t1", t1.ToString() },
                { "t2", t2.ToString() },
                { "t3", t3.ToString() }
            };
        }

        /// <summary>
        /// Returns a shadow impact description for the given stat choice.
        /// </summary>
        private string GetShadowImpact(StatType stat, PlayerAgentContext context, int t1, int t2, int t3)
        {
            if (context.ShadowValues == null) return "";

            var parts = new List<string>();

            ShadowStatType paired = GetPairedShadow(stat);
            if (context.ShadowValues.TryGetValue(paired, out int currentVal))
            {
                if (currentVal >= t3 - 2 && currentVal < t3)
                    parts.Add(_promptAssets.Render("sim_agent_shadow_impact_danger", new Dictionary<string, string>
                    {
                        { "pairedShadow", paired.ToString() },
                        { "currentValue", currentVal.ToString(CultureInfo.InvariantCulture) },
                        { "t3", t3.ToString(CultureInfo.InvariantCulture) }
                    }));
                else if (currentVal >= t2 - 2 && currentVal < t2)
                    parts.Add(_promptAssets.Render("sim_agent_shadow_impact_risk_t2", new Dictionary<string, string>
                    {
                        { "pairedShadow", paired.ToString() },
                        { "currentValue", currentVal.ToString(CultureInfo.InvariantCulture) },
                        { "t2", t2.ToString(CultureInfo.InvariantCulture) }
                    }));
                else if (currentVal >= t1 - 2 && currentVal < t1)
                    parts.Add(_promptAssets.Render("sim_agent_shadow_impact_risk_t1", new Dictionary<string, string>
                    {
                        { "pairedShadow", paired.ToString() },
                        { "currentValue", currentVal.ToString(CultureInfo.InvariantCulture) },
                        { "statName", stat.ToString() },
                        { "t1", t1.ToString(CultureInfo.InvariantCulture) }
                    }));
            }

            if (stat == StatType.Honesty)
            {
                if (context.ShadowValues.TryGetValue(ShadowStatType.Denial, out int denial) && denial > 0)
                    parts.Add(_promptAssets.Render("sim_agent_shadow_impact_honesty_benefit", new Dictionary<string, string>
                    {
                        { "denial", denial.ToString(CultureInfo.InvariantCulture) }
                    }));
            }

            return parts.Count > 0
                ? string.Join(_promptAssets.Render("sim_agent_shadow_impact_separator"), parts)
                : "";
        }

        private static ShadowStatType GetPairedShadow(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm: return ShadowStatType.Madness;
                case StatType.Rizz: return ShadowStatType.Despair;
                case StatType.Honesty: return ShadowStatType.Denial;
                case StatType.Chaos: return ShadowStatType.Fixation;
                case StatType.Wit: return ShadowStatType.Dread;
                case StatType.SelfAwareness: return ShadowStatType.Overthinking;
                default: return ShadowStatType.Madness;
            }
        }

        /// <summary>
        /// Fallback for when tool_use isn't returned — try parsing PICK: from text.
        /// </summary>
        private PlayerDecision HandleTextFallback(
            string responseText,
            TurnStart turn,
            PlayerAgentContext context,
            PlayerDecision scoringDecision)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return MakeFallbackDecision(scoringDecision, "Empty response from LLM", null, turn, context);
            }

            int? pickIndex = ParsePick(responseText, turn.Options.Length);
            if (pickIndex == null)
            {
                // Fallback to scoring agent
                return MakeFallbackDecision(scoringDecision, "Could not parse PICK from response", null, turn, context);
            }

            LastFallbackDiagnostic = null;
            LastExplanation = responseText;
            return new PlayerDecision(pickIndex.Value, responseText, scoringDecision.Scores);
        }

        /// <summary>
        /// Parses "PICK: [A/B/C/D]" from the LLM response text.
        /// Returns the 0-based option index, or null if parsing fails.
        /// Uses the last match if multiple PICK lines exist.
        /// </summary>
        internal static int? ParsePick(string responseText, int optionCount)
        {
            if (string.IsNullOrEmpty(responseText)) return null;

            var matches = Regex.Matches(responseText, @"PICK:\s*\[?([A-Da-d])\]?", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return null;

            Match last = matches[matches.Count - 1];
            char letterChar = char.ToUpperInvariant(last.Groups[1].Value[0]);
            int index = letterChar - 'A';

            if (index < 0 || index >= optionCount) return null;
            return index;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _client.Dispose();
                _disposed = true;
            }
        }

        private PlayerDecision MakeFallbackDecision(
            PlayerDecision scoringDecision,
            string reason,
            Exception? exception,
            TurnStart turn,
            PlayerAgentContext context,
            int? optionIndexOverride = null)
        {
            LastFallbackDiagnostic = new LlmPlayerAgentFallbackDiagnostic(
                reason,
                _model,
                turn?.State?.TurnNumber,
                context?.TurnNumber,
                context?.PlayerName ?? _playerName,
                context?.DateeName ?? _dateeName,
                exception);

            string reasoning = $"[LLM fallback: {reason}] {scoringDecision.Reasoning}";
            LastExplanation = "";
            return new PlayerDecision(optionIndexOverride ?? scoringDecision.OptionIndex, reasoning, scoringDecision.Scores);
        }

        private string GetModifierNote(InterestState state)
        {
            switch (state)
            {
                case InterestState.VeryIntoIt:
                case InterestState.AlmostThere:
                    return _promptAssets.Render("sim_agent_modifier_advantage");
                case InterestState.Bored:
                    return _promptAssets.Render("sim_agent_modifier_disadvantage");
                default:
                    return "";
            }
        }

        private string GetMomentumNote(int streak)
        {
            if (streak >= 5) return _promptAssets.Render("sim_agent_momentum_note_plus3");
            if (streak >= 3) return _promptAssets.Render("sim_agent_momentum_note_plus2");
            return "";
        }

        private string GetRiskTier(int need)
        {
            if (need <= 5) return _promptAssets.Render("sim_agent_risk_tier_safe");
            if (need <= 10) return _promptAssets.Render("sim_agent_risk_tier_medium");
            if (need <= 15) return _promptAssets.Render("sim_agent_risk_tier_hard");
            return _promptAssets.Render("sim_agent_risk_tier_bold");
        }

        private string FormatBonusIcons(DialogueOption opt)
        {
            var icons = new List<string>();
            if (opt.CallbackTurnNumber != null) icons.Add(_promptAssets.Render("sim_agent_icon_callback"));
            if (opt.HasTellBonus) icons.Add(_promptAssets.Render("sim_agent_icon_tell"));
            if (opt.ComboName != null) icons.Add(_promptAssets.Render("sim_agent_icon_combo", new Dictionary<string, string>
            {
                { "comboName", opt.ComboName }
            }));
            if (opt.HasWeaknessWindow) icons.Add(_promptAssets.Render("sim_agent_icon_weakness"));

            return icons.Count > 0 ? " " + string.Join(" ", icons) : "";
        }

        private sealed class SimAgentPromptAssets
        {
            private static readonly string[] RequiredUserTemplateKeys =
            {
                "sim_agent_personality_block",
                "sim_agent_recent_history_block",
                "sim_agent_recent_history_row",
                "sim_agent_modifier_disadvantage",
                "sim_agent_modifier_advantage",
                "sim_agent_momentum_note_plus2",
                "sim_agent_momentum_note_plus3",
                "sim_agent_shadow_status_row",
                "sim_agent_shadow_status_unavailable",
                "sim_agent_shadow_warning_t3",
                "sim_agent_shadow_warning_t2",
                "sim_agent_shadow_warning_t1",
                "sim_agent_shadow_warning_approaching_t1",
                "sim_agent_active_traps_none",
                "sim_agent_option_summary_row",
                "sim_agent_option_text_row",
                "sim_agent_option_shadow_row",
                "sim_agent_option_ev_row",
                "sim_agent_shadow_impact_danger",
                "sim_agent_shadow_impact_risk_t2",
                "sim_agent_shadow_impact_risk_t1",
                "sim_agent_shadow_impact_honesty_benefit",
                "sim_agent_shadow_impact_separator",
                "sim_agent_success_rules",
                "sim_agent_failure_rules",
                "sim_agent_momentum_rules_threshold",
                "sim_agent_risk_tier_safe",
                "sim_agent_risk_tier_medium",
                "sim_agent_risk_tier_hard",
                "sim_agent_risk_tier_bold",
                "sim_agent_icon_callback",
                "sim_agent_icon_tell",
                "sim_agent_icon_combo",
                "sim_agent_icon_weakness"
            };

            private readonly Dictionary<string, string> _userTemplates;

            private SimAgentPromptAssets(string systemPrompt, string userTemplate, Dictionary<string, string> userTemplates)
            {
                SystemPrompt = systemPrompt;
                UserTemplate = userTemplate;
                _userTemplates = userTemplates;
            }

            public string SystemPrompt { get; }
            public string UserTemplate { get; }

            public static SimAgentPromptAssets Load(PromptCatalog catalog)
            {
                if (catalog == null) throw new ArgumentNullException(nameof(catalog));

                var root = catalog.Get("sim_agent");
                if (string.IsNullOrWhiteSpace(root.SystemPrompt))
                {
                    throw new InvalidOperationException("prompt-catalog: key 'sim_agent' has no system_prompt. Check data/prompts/sim_agent.yaml.");
                }

                if (string.IsNullOrWhiteSpace(root.UserTemplate))
                {
                    throw new InvalidOperationException("prompt-catalog: key 'sim_agent' has no user_template. Check data/prompts/sim_agent.yaml.");
                }

                var userTemplates = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string key in RequiredUserTemplateKeys)
                {
                    var entry = catalog.Get(key);
                    if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                    {
                        throw new InvalidOperationException($"prompt-catalog: key '{key}' has no user_template. Check data/prompts/sim_agent.yaml.");
                    }

                    userTemplates[key] = entry.UserTemplate!;
                }

                return new SimAgentPromptAssets(root.SystemPrompt!, root.UserTemplate!, userTemplates);
            }

            public string Render(string key)
            {
                return Render(key, new Dictionary<string, string>());
            }

            public string Render(string key, IReadOnlyDictionary<string, string> values)
            {
                if (!_userTemplates.TryGetValue(key, out string template))
                {
                    throw new KeyNotFoundException($"prompt-catalog: missing sim agent prompt fragment '{key}'");
                }

                return PromptCatalog.Substitute(template, values);
            }
        }
    }
}

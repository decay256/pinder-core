using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
        private static readonly ToolDefinition SubmitChoiceTool = new ToolDefinition
        {
            Name = "submit_choice",
            Description = "Submit your choice of dialogue option with a strategic explanation.",
            InputSchema = LoadToolSchema()
        };

        private readonly AnthropicClient _client;
        private readonly ScoringPlayerAgent _fallback;
        private readonly string _model;
        private readonly string _playerName;
        private readonly string _dateeName;
        private readonly IRuleResolver? _ruleResolver;
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
        /// <param name="ruleResolver">Dynamic rule resolver (optional, falls back to DefaultRuleResolver.Instance).</param>
        /// <exception cref="ArgumentNullException">If options or fallback is null.</exception>
        public LlmPlayerAgent(
            AnthropicOptions options,
            ScoringPlayerAgent fallback,
            string playerName = "the player",
            string dateeName = "the datee",
            IRuleResolver? ruleResolver = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _playerName = playerName ?? "the player";
            _dateeName = dateeName ?? "the datee";
            _model = options.Model;
            _client = new AnthropicClient(options.ApiKey);
            _ruleResolver = ruleResolver ?? DefaultRuleResolver.Instance;
        }

        private static JObject LoadToolSchema()
        {
            try
            {
                string? path = DataFileLocator.FindDataFile(AppContext.BaseDirectory, Path.Combine("data", "schemas", "submit_choice_tool.json"));
                if (path != null && File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }
            catch
            {
                // Fallback gracefully
            }

            return JObject.Parse(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""choice"": {
                        ""type"": ""integer"",
                        ""description"": ""0-based index of the chosen option (0=A, 1=B, 2=C, 3=D)""
                    },
                    ""explanation"": {
                        ""type"": ""string"",
                        ""description"": ""1-2 sentence strategic explanation for this pick. Reference shadow levels, success %, combos, or momentum as relevant.""
                    }
                },
                ""required"": [""choice"", ""explanation""]
            }");
        }

        private static (string SystemPrompt, string UserTemplate) LoadTemplates()
        {
            string? repoRoot = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);
            string promptsDir = repoRoot != null 
                ? Path.Combine(repoRoot, "data", "prompts")
                : Path.Combine(AppContext.BaseDirectory, "data", "prompts");

            if (!Directory.Exists(promptsDir))
            {
                throw new DirectoryNotFoundException($"Required prompt directory was not found: {promptsDir}");
            }

            var catalog = PromptCatalog.LoadFromDirectory(promptsDir);
            var entry = catalog.TryGet("sim_agent")
                ?? throw new KeyNotFoundException("Prompt catalog does not contain required template 'sim_agent'.");

            return (entry.SystemPrompt ?? "", entry.UserTemplate ?? "");
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

        private static string ResolveSuccessRules(IRuleResolver? resolver)
        {
            if (resolver == null) return "beat by 1-4 → +1 interest, 5-9 → +2, 10+ → +3. Nat 20 → +4";

            int? tier1 = resolver.GetSuccessInterestDelta(1, 10);
            int? tier2 = resolver.GetSuccessInterestDelta(5, 10);
            int? tier3 = resolver.GetSuccessInterestDelta(10, 10);
            int? nat20 = resolver.GetSuccessInterestDelta(1, 20);

            string t1Str = tier1.HasValue ? $"+{tier1}" : "+1";
            string t2Str = tier2.HasValue ? $"+{tier2}" : "+2";
            string t3Str = tier3.HasValue ? $"+{tier3}" : "+3";
            string natStr = nat20.HasValue ? $"+{nat20}" : "+4";

            return $"beat by 1-4 → {t1Str} interest, 5-9 → {t2Str}, 10+ → {t3Str}. Nat 20 → {natStr}";
        }

        private static string ResolveFailureRules(IRuleResolver? resolver)
        {
            if (resolver == null) return "miss by 1-2 → Fumble (−1), 3-5 → Misfire (−1), 6-9 → Trope Trap (−2 + trap), 10+ → Catastrophe (−3 + trap). Nat 1 → Legendary Fail (−4)";

            int? fumble = resolver.GetFailureInterestDelta(1, 10);
            int? misfire = resolver.GetFailureInterestDelta(3, 10);
            int? trap = resolver.GetFailureInterestDelta(6, 10);
            int? catastrophe = resolver.GetFailureInterestDelta(10, 10);
            int? legendary = resolver.GetFailureInterestDelta(1, 1);

            string fStr = fumble.HasValue ? $"{fumble}" : "−1";
            string mStr = misfire.HasValue ? $"{misfire}" : "−1";
            string tStr = trap.HasValue ? $"{trap}" : "−2";
            string cStr = catastrophe.HasValue ? $"{catastrophe}" : "−3";
            string lStr = legendary.HasValue ? $"{legendary}" : "−4";

            return $"miss by 1-2 → Fumble ({fStr}), 3-5 → Misfire ({mStr}), 6-9 → Trope Trap ({tStr} + trap), 10+ → Catastrophe ({cStr} + trap). Nat 1 → Legendary Fail ({lStr})";
        }

        private static string ResolveMomentumRules(IRuleResolver? resolver)
        {
            if (resolver == null) return "3+ wins → +2 to next roll. 5+ wins → +3.";

            var activeRules = new List<string>();
            for (int streak = 1; streak <= 10; streak++)
            {
                int? bonus = resolver.GetMomentumBonus(streak);
                if (bonus.HasValue && bonus.Value > 0)
                {
                    int? prevBonus = streak > 1 ? resolver.GetMomentumBonus(streak - 1) : null;
                    if (prevBonus != bonus.Value)
                    {
                        activeRules.Add($"{streak}+ wins → +{bonus.Value} to next roll");
                    }
                }
            }

            if (activeRules.Count > 0)
            {
                return string.Join(". ", activeRules) + ".";
            }
            return "3+ wins → +2 to next roll. 5+ wins → +3.";
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
                    ToolChoice = new ToolChoiceOption { Type = "tool", Name = "submit_choice" }
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
            var (systemTemplate, _) = LoadTemplates();
            var dict = BuildSubstitutionDict(null, context, null);
            return PromptCatalog.Substitute(systemTemplate, dict);
        }

        /// <summary>
        /// Builds the full LLM prompt with game state, shadow analysis, and option details.
        /// </summary>
        internal string BuildPrompt(TurnStart turn, PlayerAgentContext context, OptionScore[] scores = null)
        {
            var (_, userTemplate) = LoadTemplates();
            var dict = BuildSubstitutionDict(turn, context, scores);
            return PromptCatalog.Substitute(userTemplate, dict);
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
                : "none";

            string personalityBlockValue = "";
            if (!string.IsNullOrEmpty(context.PlayerSystemPrompt))
            {
                personalityBlockValue = "\n\nCharacter personality (for voice, not strategy):\n" + context.PlayerSystemPrompt;
            }

            var recentHistorySb = new System.Text.StringBuilder();
            if (context.RecentHistory != null && context.RecentHistory.Count > 0)
            {
                recentHistorySb.AppendLine("## Recent Conversation");
                int start = Math.Max(0, context.RecentHistory.Count - 6);
                for (int i = start; i < context.RecentHistory.Count; i++)
                {
                    var entry = context.RecentHistory[i];
                    recentHistorySb.AppendLine($"{entry.Sender}: \"{entry.Text}\"");
                }
                recentHistorySb.AppendLine();
            }
            string recentHistoryBlockValue = recentHistorySb.ToString();

            var shadowStatusSb = new System.Text.StringBuilder();
            if (context.ShadowValues != null)
            {
                foreach (ShadowStatType s in (ShadowStatType[])Enum.GetValues(typeof(ShadowStatType)))
                {
                    if (context.ShadowValues.TryGetValue(s, out int value))
                    {
                        string warning = "";
                        if (value >= t3) warning = $" ⚠️ T3 CRITICAL — may override choices!";
                        else if (value >= t2) warning = $" ⚠️ T2 — -2 penalty to paired stat rolls";
                        else if (value >= t1) warning = $" ⚠️ T1 — taints delivery";
                        else if (value >= 4) warning = " (approaching T1)";
                        shadowStatusSb.AppendLine($"- {s}: {value}/18{warning}");
                    }
                }
            }
            else
            {
                shadowStatusSb.AppendLine("- Shadow tracking unavailable");
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

                    optionsSb.AppendLine($"{letter}) [{opt.Stat.ToString().ToUpperInvariant()} +{modifier}] DC {dc} | Need {need}+ on d20 | {pct}% success | {riskTier}{icons}");
                    optionsSb.AppendLine($"   Text: \"{opt.IntendedText}\"");

                    string shadowImpact = GetShadowImpact(opt.Stat, context, t1, t2, t3);
                    if (!string.IsNullOrEmpty(shadowImpact))
                        optionsSb.AppendLine($"   Shadow: {shadowImpact}");

                    if (scores != null && i < scores.Length)
                        optionsSb.AppendLine($"   EV: {scores[i].ExpectedInterestGain:F2}");

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
        private static string GetShadowImpact(StatType stat, PlayerAgentContext context, int t1, int t2, int t3)
        {
            if (context.ShadowValues == null) return "";

            var parts = new List<string>();

            ShadowStatType paired = GetPairedShadow(stat);
            if (context.ShadowValues.TryGetValue(paired, out int currentVal))
            {
                if (currentVal >= t3 - 2 && currentVal < t3)
                    parts.Add($"DANGER: {paired} at {currentVal}, may push to T3 (≥{t3})!");
                else if (currentVal >= t2 - 2 && currentVal < t2)
                    parts.Add($"RISK: {paired} at {currentVal}, may push to T2 (≥{t2})");
                else if (currentVal >= t1 - 2 && currentVal < t1)
                    parts.Add($"RISK: {paired} at {currentVal}, using {stat} may push to T1 (≥{t1})");
            }

            if (stat == StatType.Honesty)
            {
                if (context.ShadowValues.TryGetValue(ShadowStatType.Denial, out int denial) && denial > 0)
                    parts.Add($"BENEFIT: Honesty can reduce Denial (currently {denial})");
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : "";
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

        private static string GetModifierNote(InterestState state)
        {
            switch (state)
            {
                case InterestState.VeryIntoIt:
                case InterestState.AlmostThere:
                    return " — grants advantage";
                case InterestState.Bored:
                    return " — grants disadvantage";
                default:
                    return "";
            }
        }

        private static string GetMomentumNote(int streak)
        {
            if (streak >= 5) return " (+3 to next roll)";
            if (streak >= 3) return " (+2 to next roll)";
            return "";
        }

        private static string GetRiskTier(int need)
        {
            if (need <= 5) return "Safe";
            if (need <= 10) return "Medium";
            if (need <= 15) return "Hard";
            return "Bold";
        }

        private static string FormatBonusIcons(DialogueOption opt)
        {
            var icons = new List<string>();
            if (opt.CallbackTurnNumber != null) icons.Add("🔗");
            if (opt.HasTellBonus) icons.Add("📖");
            if (opt.ComboName != null) icons.Add($"⭐ combo: {opt.ComboName}");
            if (opt.HasWeaknessWindow) icons.Add("🔓");

            return icons.Count > 0 ? " " + string.Join(" ", icons) : "";
        }
    }
}

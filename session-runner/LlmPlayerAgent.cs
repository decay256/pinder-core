using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
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
        private const string ShadowRulesReminder =
            "## Shadow Threshold Rules\n" +
            "- T1 (≥6): shadow taints delivery of paired stat.\n" +
            "- T2 (≥12): -2 penalty to paired stat rolls.\n" +
            "- T3 (≥18): shadow may override your choices for paired stat.\n" +
            "\n" +
            "## How to Reduce Shadows\n" +
            "- Madness: −1 on any combo success | −1 on Tell option selected | −1 on Nat 20 CHAOS\n" +
            "- Despair: −1 when SA or HONESTY succeeds at interest >18\n" +
            "- Denial: −1 when HONESTY succeeds at interest ≥15\n" +
            "- Fixation: −1 when 4+ different stats used in session | −1 on CHAOS combo\n" +
            "- Dread: −1 when date secured | −1 on any Nat 20\n" +
            "- Overthinking: −1 when any roll succeeds at interest ≥20\n" +
            "\n" +
            "## What Grows Shadows (avoid when near threshold)\n" +
            "- Madness: every TropeTrap failure +1 | CHARM used 3x in session +1\n" +
            "- Despair: Nat 1 on RIZZ +2 | RIZZ TropeTrap +1 | every 3rd cumulative RIZZ failure +1\n" +
            "- Denial: skipping available HONESTY option +1 | date secured without HONESTY success +1\n" +
            "- Fixation: same stat 3 turns in a row +1 | always picking highest-% 3 turns +1\n" +
            "- Dread: Nat 1 on WIT +1 | catastrophic WIT fail +1 | interest hits 0 +2 | Ghosted +1\n" +
            "- Overthinking: Nat 1 on SA +1 | SA used 3+ times in session +1\n";

        private const string RulesReminder =
            "## Rules Reminder\n" +
            "- Roll d20 + stat modifier + bonuses vs DC. Meet or beat DC = success.\n" +
            "- Success tiers: beat by 1-4 → +1 interest, 5-9 → +2, 10+ → +3. Nat 20 → +4.\n" +
            "- Failure tiers: miss by 1-2 → Fumble (−1), 3-5 → Misfire (−1), 6-9 → Trope Trap (−2 + trap), " +
            "10+ → Catastrophe (−3 + trap). Nat 1 → Legendary Fail (−4).\n" +
            "- Risk tier bonus on success: Hard → +1 interest, Bold → +2 interest.\n" +
            "- Momentum: 3+ wins → +2 to next roll. 5+ wins → +3.\n" +
            "- 🔗 = callback bonus: hidden +1/+2/+3 added to roll.\n" +
            "- 📖 = tell bonus: hidden +2 added to roll.\n" +
            "- ⭐ = combo: +1 interest on success.\n" +
            "- 🔓 = weakness window: DC is already reduced by 2-3.\n";

        private const string StrategyReminder =
            "## Your Strategy\n" +
            "PRIMARY GOAL: Win the date. Raise interest to 25.\n" +
            "SECONDARY: Manage shadows strategically. Trade a losing roll for shadow reduction when it prevents a dangerous threshold.\n" +
            "NARRATIVE GAMBLE: When EV difference between options is < 5 percentage points, you may pick the riskier/more interesting option.\n" +
            "Always explain your reasoning in 1-2 sentences.\n";

        /// <summary>
        /// Tool definition for structured output via Anthropic tool_use.
        /// </summary>
        private static readonly ToolDefinition SubmitChoiceTool = new ToolDefinition
        {
            Name = "submit_choice",
            Description = "Submit your choice of dialogue option with a strategic explanation.",
            InputSchema = JObject.Parse(@"{
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
            }")
        };

        private readonly AnthropicClient _client;
        private readonly ScoringPlayerAgent _fallback;
        private readonly string _model;
        private readonly string _playerName;
        private readonly string _opponentName;
        private bool _disposed;
        private readonly List<CallSummaryStat> _tokenStats = new List<CallSummaryStat>();

        /// <summary>
        /// The last explanation produced by the LLM agent. Empty string if no explanation available.
        /// </summary>
        public string LastExplanation { get; private set; } = "";

        /// <summary>Returns per-call token stats for each llm-player-pick call made.</summary>
        public IReadOnlyList<CallSummaryStat> GetTokenStats() => _tokenStats.AsReadOnly();

        /// <summary>
        /// Creates an LLM-backed player agent.
        /// </summary>
        /// <param name="options">Anthropic API configuration (API key, model, etc.).</param>
        /// <param name="fallback">Deterministic scoring agent used on LLM failure.</param>
        /// <param name="playerName">Player character display name (optional, for prompt immersion).</param>
        /// <param name="opponentName">Opponent character display name (optional, for prompt immersion).</param>
        /// <exception cref="ArgumentNullException">If options or fallback is null.</exception>
        public LlmPlayerAgent(
            AnthropicOptions options,
            ScoringPlayerAgent fallback,
            string playerName = "the player",
            string opponentName = "the opponent")
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _playerName = playerName ?? "the player";
            _opponentName = opponentName ?? "the opponent";
            _model = options.Model;
            _client = new AnthropicClient(options.ApiKey);
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

                var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);

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
                    return HandleTextFallback(responseText, turn, scoringDecision);
                }

                int? choice = toolInput.Value<int?>("choice");
                string explanation = toolInput.Value<string>("explanation") ?? "";

                if (choice == null || choice.Value < 0 || choice.Value >= turn.Options.Length)
                {
                    LastExplanation = "LLM response invalid, defaulting to option 0";
                    return new PlayerDecision(0, LastExplanation, scoringDecision.Scores);
                }

                LastExplanation = !string.IsNullOrWhiteSpace(explanation)
                    ? explanation
                    : "No explanation provided";

                return new PlayerDecision(choice.Value, LastExplanation, scoringDecision.Scores);
            }
            catch (AnthropicApiException ex)
            {
                return MakeFallbackDecision(scoringDecision, $"Anthropic API error ({ex.StatusCode})");
            }
            catch (HttpRequestException ex)
            {
                return MakeFallbackDecision(scoringDecision, $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return MakeFallbackDecision(scoringDecision, "Request timed out");
            }
            catch (Exception ex)
            {
                return MakeFallbackDecision(scoringDecision, $"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the system message with strategic framing and character personality.
        /// </summary>
        private string BuildSystemMessage(PlayerAgentContext context)
        {
            string name = !string.IsNullOrEmpty(context.PlayerName) ? context.PlayerName : _playerName;

            var sb = new System.Text.StringBuilder(512);
            sb.Append($"You are playing as {name} in Pinder, a comedy dating RPG. ");
            sb.Append("You are a strategic player agent. Your PRIMARY goal is to WIN — raise interest to 25. ");
            sb.Append("Your SECONDARY goal is strategic shadow management — prevent shadows from hitting dangerous thresholds. ");
            sb.Append("You make calculated decisions, not random ones.");

            if (!string.IsNullOrEmpty(context.PlayerSystemPrompt))
            {
                // Extract brief personality traits (first 500 chars to keep prompt lean)
                string personality = context.PlayerSystemPrompt.Length > 500
                    ? context.PlayerSystemPrompt.Substring(0, 500) + "..."
                    : context.PlayerSystemPrompt;
                sb.AppendLine();
                sb.AppendLine();
                sb.Append($"Character personality (for voice, not strategy):\n{personality}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the full LLM prompt with game state, shadow analysis, and option details.
        /// </summary>
        internal string BuildPrompt(TurnStart turn, PlayerAgentContext context, OptionScore[] scores = null)
        {
            var sb = new System.Text.StringBuilder(2048);

            string playerLabel = !string.IsNullOrEmpty(context.PlayerName) ? context.PlayerName : _playerName;
            string opponentLabel = !string.IsNullOrEmpty(context.OpponentName) ? context.OpponentName : _opponentName;

            sb.AppendLine($"You are {playerLabel} talking to {opponentLabel}. Choose a dialogue option.");
            sb.AppendLine();

            // Recent conversation context
            if (context.RecentHistory != null && context.RecentHistory.Count > 0)
            {
                sb.AppendLine("## Recent Conversation");
                int start = Math.Max(0, context.RecentHistory.Count - 6);
                for (int i = start; i < context.RecentHistory.Count; i++)
                {
                    var entry = context.RecentHistory[i];
                    sb.AppendLine($"{entry.Sender}: \"{entry.Text}\"");
                }
                sb.AppendLine();
            }

            // Current state
            sb.AppendLine("## Current State");
            string modifierNote = GetModifierNote(context.InterestState);
            sb.AppendLine($"- Interest: {context.CurrentInterest}/25 ({context.InterestState}){modifierNote}");

            string momentumNote = GetMomentumNote(context.MomentumStreak);
            sb.AppendLine($"- Momentum: {context.MomentumStreak} consecutive wins{momentumNote}");

            string trapList = context.ActiveTrapNames.Length > 0
                ? string.Join(", ", context.ActiveTrapNames)
                : "none";
            sb.AppendLine($"- Active traps: {trapList}");
            sb.AppendLine($"- Turn: {context.TurnNumber}");
            sb.AppendLine();

            // Shadow state with threshold warnings
            sb.AppendLine("## Shadow Status");
            if (context.ShadowValues != null)
            {
                foreach (ShadowStatType s in (ShadowStatType[])Enum.GetValues(typeof(ShadowStatType)))
                {
                    if (context.ShadowValues.TryGetValue(s, out int value))
                    {
                        string warning = "";
                        if (value >= 18) warning = " ⚠️ T3 CRITICAL — may override choices!";
                        else if (value >= 12) warning = " ⚠️ T2 — -2 penalty to paired stat rolls";
                        else if (value >= 6) warning = " ⚠️ T1 — taints delivery";
                        else if (value >= 4) warning = " (approaching T1)";
                        sb.AppendLine($"- {s}: {value}/18{warning}");
                    }
                }
            }
            else
            {
                sb.AppendLine("- Shadow tracking unavailable");
            }
            sb.AppendLine();

            // Options with shadow hints
            sb.AppendLine("## Your Options");
            char letter = 'A';
            for (int i = 0; i < turn.Options.Length; i++)
            {
                DialogueOption opt = turn.Options[i];
                int modifier = context.PlayerStats.GetEffective(opt.Stat);
                int dc = context.OpponentStats.GetDefenceDC(opt.Stat);
                int need = dc - modifier;
                int pct = Math.Max(0, Math.Min(100, (21 - need) * 5));
                string riskTier = GetRiskTier(need);
                string icons = FormatBonusIcons(opt);

                sb.AppendLine($"{letter}) [{opt.Stat.ToString().ToUpperInvariant()} +{modifier}] DC {dc} | Need {need}+ on d20 | {pct}% success | {riskTier}{icons}");
                sb.AppendLine($"   Text: \"{opt.IntendedText}\"");

                // Shadow impact hints
                string shadowImpact = GetShadowImpact(opt.Stat, context);
                if (!string.IsNullOrEmpty(shadowImpact))
                    sb.AppendLine($"   Shadow: {shadowImpact}");

                // Include EV from scoring agent if available
                if (scores != null && i < scores.Length)
                    sb.AppendLine($"   EV: {scores[i].ExpectedInterestGain:F2}");

                letter = (char)(letter + 1);
            }
            sb.AppendLine();

            // Rules
            sb.AppendLine(RulesReminder);
            sb.AppendLine(ShadowRulesReminder);
            sb.AppendLine(StrategyReminder);

            sb.AppendLine("Use the submit_choice tool to make your pick. choice is 0-indexed (A=0, B=1, C=2, D=3).");

            return sb.ToString();
        }

        /// <summary>
        /// Returns a shadow impact description for the given stat choice.
        /// </summary>
        private static string GetShadowImpact(StatType stat, PlayerAgentContext context)
        {
            if (context.ShadowValues == null) return "";

            var parts = new List<string>();

            // Shadow growth: using a stat can grow its paired shadow
            ShadowStatType paired = GetPairedShadow(stat);
            if (context.ShadowValues.TryGetValue(paired, out int currentVal))
            {
                if (currentVal >= 4 && currentVal < 6)
                    parts.Add($"RISK: {paired} at {currentVal}, using {stat} may push to T1 (≥6)");
                else if (currentVal >= 10 && currentVal < 12)
                    parts.Add($"RISK: {paired} at {currentVal}, may push to T2 (≥12)");
                else if (currentVal >= 16 && currentVal < 18)
                    parts.Add($"DANGER: {paired} at {currentVal}, may push to T3 (≥18)!");
            }

            // Honesty reduces Denial
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
        private PlayerDecision HandleTextFallback(string responseText, TurnStart turn, PlayerDecision scoringDecision)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return MakeFallbackDecision(scoringDecision, "Empty response from LLM");
            }

            int? pickIndex = ParsePick(responseText, turn.Options.Length);
            if (pickIndex == null)
            {
                return MakeFallbackDecision(scoringDecision, "Could not parse PICK from response");
            }

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

        private PlayerDecision MakeFallbackDecision(PlayerDecision scoringDecision, string reason)
        {
            string reasoning = $"[LLM fallback: {reason}] {scoringDecision.Reasoning}";
            LastExplanation = "";
            return new PlayerDecision(scoringDecision.OptionIndex, reasoning, scoringDecision.Scores);
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

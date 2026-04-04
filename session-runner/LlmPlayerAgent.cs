using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// LLM-backed player agent that sends full game state and rules context to
    /// Anthropic's Claude API and parses a strategic pick from the response.
    /// Falls back to ScoringPlayerAgent on any failure.
    /// </summary>
    public sealed class LlmPlayerAgent : IPlayerAgent, IDisposable
    {
        private const string SystemMessage =
            "You are a strategic player in Pinder, a comedy dating RPG. You analyze game mechanics " +
            "and choose the optimal dialogue option each turn. Your goal is to reach Interest 25 " +
            "(date secured) while avoiding Interest 0 (unmatched/ghosted).";

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

        private readonly AnthropicClient _client;
        private readonly ScoringPlayerAgent _fallback;
        private readonly string _model;
        private readonly string _playerName;
        private readonly string _opponentName;
        private bool _disposed;

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
        /// Sends the full game state to Claude, parses the pick, and returns a decision.
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
                string userMessage = BuildPrompt(turn, context);

                var request = new MessagesRequest
                {
                    Model = _model,
                    MaxTokens = 512,
                    Temperature = 0.3,
                    System = new[] { new ContentBlock { Type = "text", Text = SystemMessage } },
                    Messages = new[] { new Message { Role = "user", Content = userMessage } }
                };

                var response = await _client.SendMessagesAsync(request).ConfigureAwait(false);
                string responseText = response.GetText();

                if (string.IsNullOrWhiteSpace(responseText))
                {
                    return MakeFallbackDecision(scoringDecision, "Empty response from LLM");
                }

                int? pickIndex = ParsePick(responseText, turn.Options.Length);
                if (pickIndex == null)
                {
                    return MakeFallbackDecision(scoringDecision, "Could not parse PICK from response");
                }

                return new PlayerDecision(pickIndex.Value, responseText, scoringDecision.Scores);
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
        /// Builds the full LLM prompt from turn data and agent context.
        /// </summary>
        internal string BuildPrompt(TurnStart turn, PlayerAgentContext context)
        {
            var sb = new System.Text.StringBuilder(2048);

            sb.AppendLine($"You are playing as {_playerName}, a sentient penis on a dating app.");
            sb.AppendLine($"You are talking to {_opponentName}. Choose one of the dialogue options below.");
            sb.AppendLine();

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

            sb.AppendLine($"- Shadow levels: {FormatShadows(context.ShadowValues)}");
            sb.AppendLine($"- Turn: {context.TurnNumber}");
            sb.AppendLine();

            // Options
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

                letter = (char)(letter + 1);
            }
            sb.AppendLine();

            // Rules reminder
            sb.AppendLine(RulesReminder);

            sb.AppendLine("Explain your reasoning step by step, weighing success probability, interest gain, risk,");
            sb.AppendLine("and any active bonuses or traps. Then state your final choice as:");
            sb.AppendLine("PICK: [A/B/C/D]");

            return sb.ToString();
        }

        /// <summary>
        /// Parses "PICK: [A/B/C/D]" from the LLM response text.
        /// Returns the 0-based option index, or null if parsing fails.
        /// Uses the last match if multiple PICK lines exist.
        /// </summary>
        internal static int? ParsePick(string responseText, int optionCount)
        {
            if (string.IsNullOrEmpty(responseText)) return null;

            // Match PICK: followed by optional whitespace and optional brackets around a letter
            var matches = Regex.Matches(responseText, @"PICK:\s*\[?([A-Da-d])\]?", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return null;

            // Use the last match (LLM may revise its choice)
            Match last = matches[matches.Count - 1];
            char letter = char.ToUpperInvariant(last.Groups[1].Value[0]);
            int index = letter - 'A';

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

        private static PlayerDecision MakeFallbackDecision(PlayerDecision scoringDecision, string reason)
        {
            string reasoning = $"[LLM fallback: {reason}] {scoringDecision.Reasoning}";
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

        private static string FormatShadows(Dictionary<ShadowStatType, int>? shadows)
        {
            if (shadows == null) return "unknown";

            var parts = new List<string>();
            // Iterate in enum order for consistent output
            foreach (ShadowStatType s in (ShadowStatType[])Enum.GetValues(typeof(ShadowStatType)))
            {
                if (shadows.TryGetValue(s, out int value))
                {
                    parts.Add($"{s} {value}");
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "unknown";
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
            if (opt.ComboName != null) icons.Add("⭐");
            if (opt.HasWeaknessWindow) icons.Add("🔓");

            return icons.Count > 0 ? " " + string.Join(" ", icons) : "";
        }
    }
}

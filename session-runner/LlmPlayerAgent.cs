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
    /// LLM-backed player agent that sends full game state, character context,
    /// conversation history, and scoring advisory to Anthropic's Claude API
    /// and parses a character-consistent pick from the response.
    /// Falls back to ScoringPlayerAgent on any failure.
    /// </summary>
    public sealed class LlmPlayerAgent : IPlayerAgent, IDisposable
    {
        private const string GenericSystemMessage =
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
        private readonly string _playerSystemPrompt;
        private readonly string _playerTextingStyle;
        private bool _disposed;

        /// <summary>
        /// Creates an LLM-backed player agent with character context.
        /// </summary>
        /// <param name="options">Anthropic API configuration (API key, model, etc.).</param>
        /// <param name="fallback">Deterministic scoring agent used on LLM failure.</param>
        /// <param name="playerName">Player character display name.</param>
        /// <param name="opponentName">Opponent character display name.</param>
        /// <param name="playerSystemPrompt">
        ///   The player character's full assembled system prompt (from CharacterProfile.AssembledSystemPrompt).
        ///   Empty string if not available. Used to give the LLM the character's personality and voice.
        /// </param>
        /// <param name="playerTextingStyle">
        ///   The player character's texting style fragment (from CharacterProfile.TextingStyleFragment).
        ///   Empty string if not available. Used to reinforce character voice in option selection reasoning.
        /// </param>
        public LlmPlayerAgent(
            AnthropicOptions options,
            ScoringPlayerAgent fallback,
            string playerName = "the player",
            string opponentName = "the opponent",
            string playerSystemPrompt = "",
            string playerTextingStyle = "")
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _playerName = playerName ?? "the player";
            _opponentName = opponentName ?? "the opponent";
            _playerSystemPrompt = playerSystemPrompt ?? "";
            _playerTextingStyle = playerTextingStyle ?? "";
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
                string systemMessage = BuildSystemMessage();
                string userMessage = BuildPrompt(turn, context, scoringDecision);

                var request = new MessagesRequest
                {
                    Model = _model,
                    MaxTokens = 512,
                    Temperature = 0.3,
                    System = new[] { new ContentBlock { Type = "text", Text = systemMessage } },
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
        /// Builds the system message, incorporating character identity when available.
        /// Falls back to generic strategic prompt when no character context is provided.
        /// </summary>
        internal string BuildSystemMessage()
        {
            bool hasPrompt = !string.IsNullOrWhiteSpace(_playerSystemPrompt);
            bool hasStyle = !string.IsNullOrWhiteSpace(_playerTextingStyle);

            if (!hasPrompt && !hasStyle)
            {
                return GenericSystemMessage;
            }

            var sb = new System.Text.StringBuilder(4096);
            sb.AppendLine($"You are playing as {_playerName} in Pinder, a comedy dating RPG.");
            sb.AppendLine($"You are talking to {_opponentName}.");
            sb.AppendLine();

            if (hasPrompt)
            {
                sb.AppendLine(_playerSystemPrompt);
                sb.AppendLine();
            }

            if (hasStyle)
            {
                sb.AppendLine(_playerTextingStyle);
                sb.AppendLine();
            }

            sb.AppendLine($"Choose dialogue options that fit {_playerName}'s personality and voice.");
            sb.Append("You also understand game mechanics and consider expected value, but character fit ");
            sb.AppendLine("and narrative moment take priority over pure optimization.");

            return sb.ToString();
        }

        /// <summary>
        /// Builds the full LLM user prompt from turn data, agent context, and scoring advisory.
        /// </summary>
        internal string BuildPrompt(TurnStart turn, PlayerAgentContext context, PlayerDecision? scoringDecision = null)
        {
            var sb = new System.Text.StringBuilder(4096);

            // Conversation history (if available)
            if (context.ConversationHistory != null && context.ConversationHistory.Count > 0)
            {
                sb.AppendLine("## Conversation So Far");
                foreach (var (sender, text) in context.ConversationHistory)
                {
                    sb.AppendLine($"{sender}: {text}");
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

            // Scoring agent advisory (if available)
            if (scoringDecision != null && scoringDecision.Scores.Length > 0)
            {
                sb.AppendLine("## Scoring Agent Advisory (pure EV — use as input, not gospel)");
                char advisoryLetter = 'A';
                for (int i = 0; i < scoringDecision.Scores.Length; i++)
                {
                    var score = scoringDecision.Scores[i];
                    string scorerPick = i == scoringDecision.OptionIndex ? " ← scorer pick" : "";
                    sb.AppendLine($"{advisoryLetter}) Score: {score.Score:F2} | {score.SuccessChance * 100:F0}% success | EV: {score.ExpectedInterestGain:+0.00;-0.00;+0.00}{scorerPick}");
                    advisoryLetter = (char)(advisoryLetter + 1);
                }
                sb.AppendLine();
            }

            // Rules reminder
            sb.AppendLine(RulesReminder);

            // Task instruction
            sb.AppendLine($"Consider: (1) Which option fits {_playerName}'s personality right now?");
            sb.AppendLine("(2) What would make the best narrative moment given the conversation so far?");
            sb.AppendLine("(3) The scoring agent's EV analysis — diverge when character or story demands it.");
            sb.AppendLine();
            sb.AppendLine("Explain your reasoning in 2-4 sentences. Then state your final choice as:");
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

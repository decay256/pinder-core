using System;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Formats player agent reasoning and score tables for playtest markdown output.
    /// </summary>
    public static class PlaytestFormatter
    {
        private static readonly char[] Letters = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H' };

        /// <summary>
        /// Formats the player agent's reasoning as a markdown blockquote.
        /// Each line of the reasoning string is prefixed with "> ".
        /// </summary>
        /// <param name="decision">The PlayerDecision returned by the agent.</param>
        /// <param name="agentTypeName">The simple class name of the agent (e.g. "ScoringPlayerAgent").</param>
        /// <returns>A multi-line string containing the formatted reasoning block.</returns>
        public static string FormatReasoningBlock(PlayerDecision? decision, string agentTypeName)
        {
            if (decision == null)
            {
                return "";
            }

            var sb = new StringBuilder();
            string reasoning = decision.Reasoning;
            if (string.IsNullOrEmpty(reasoning))
            {
                sb.AppendLine("> (no reasoning provided)");
            }
            else
            {
                var lines = reasoning.Split(new[] { '\n' }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    sb.AppendLine($"> {line}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats the option score table as a markdown table.
        /// The chosen option row is marked with ✓ and its score is bold.
        /// </summary>
        /// <param name="decision">The PlayerDecision containing Scores and OptionIndex.</param>
        /// <param name="options">The DialogueOption[] from TurnStart, used for stat labels.</param>
        /// <returns>A multi-line string containing the markdown table, or empty if scores are null.</returns>
        public static string FormatScoreTable(PlayerDecision? decision, DialogueOption[] options)
        {
            if (decision == null)
            {
                return "";
            }

            if (decision.Scores == null)
            {
                Console.Error.WriteLine("Warning: PlayerDecision.Scores is null — skipping score table.");
                return "";
            }

            var sb = new StringBuilder();
            sb.AppendLine("| Option | Stat | Pct | Expected ΔI | Score |");
            sb.AppendLine("|---|---|---|---|---|");

            for (int i = 0; i < options.Length; i++)
            {
                string letter = i < Letters.Length ? Letters[i].ToString() : i.ToString();
                bool isChosen = i == decision.OptionIndex;

                string optionLabel = isChosen ? $"{letter} ✓" : letter;
                string statLabel = StatLabel(options[i].Stat);

                // Find matching score entry
                OptionScore? score = FindScore(decision.Scores, i);

                string pct;
                string expectedDelta;
                string scoreStr;

                if (score != null)
                {
                    float successChance = float.IsNaN(score.SuccessChance) || score.SuccessChance < 0
                        ? 0f : score.SuccessChance;
                    int pctInt = (int)Math.Round(successChance * 100);
                    pct = $"{pctInt}%";

                    float gain = float.IsNaN(score.ExpectedInterestGain) ? 0f : score.ExpectedInterestGain;
                    string bonuses = score.BonusesApplied != null && score.BonusesApplied.Length > 0
                        ? " " + string.Join("", score.BonusesApplied)
                        : "";
                    expectedDelta = $"+{gain:F1}{bonuses}";

                    float scoreVal = float.IsNaN(score.Score) || score.Score < 0 ? 0f : score.Score;
                    scoreStr = isChosen ? $"**{scoreVal:F1}**" : $"{scoreVal:F1}";
                }
                else
                {
                    pct = "—";
                    expectedDelta = "—";
                    scoreStr = "—";
                }

                sb.AppendLine($"| {optionLabel} | {statLabel} | {pct} | {expectedDelta} | {scoreStr} |");
            }

            return sb.ToString();
        }

        private static OptionScore? FindScore(OptionScore[] scores, int optionIndex)
        {
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i].OptionIndex == optionIndex)
                    return scores[i];
            }
            // Fall back to positional if indices don't match
            if (optionIndex < scores.Length)
                return scores[optionIndex];
            return null;
        }

        private static string StatLabel(StatType s)
        {
            switch (s)
            {
                case StatType.Charm: return "CHARM";
                case StatType.Rizz: return "RIZZ";
                case StatType.Honesty: return "HONESTY";
                case StatType.Chaos: return "CHAOS";
                case StatType.Wit: return "WIT";
                case StatType.SelfAwareness: return "SA";
                default: return s.ToString().ToUpperInvariant();
            }
        }

        /// <summary>
        /// Returns a descriptive explanation of the sequence that triggers a given combo.
        /// </summary>
        public static string GetComboSequenceDescription(string comboName)
        {
            switch (comboName)
            {
                case "The Setup": return "You played Wit last turn, then Charm this turn — the sequence earns +1 bonus interest.";
                case "The Reveal": return "You played Charm last turn, then Honesty this turn — the sequence earns +1 bonus interest.";
                case "The Read": return "You played SA last turn, then Honesty this turn — the sequence earns +1 bonus interest.";
                case "The Pivot": return "You played Honesty last turn, then Chaos this turn — the sequence earns +1 bonus interest.";
                case "The Escalation": return "You played Chaos last turn, then Rizz this turn — the sequence earns +1 bonus interest.";
                case "The Disarm": return "You played Wit last turn, then Honesty this turn — the sequence earns +1 bonus interest.";
                case "The Recovery": return "You failed a roll last turn, then played SA this turn — the sequence earns +2 bonus interest.";
                case "The Triple": return "You played 3 different stats in 3 consecutive turns — your next roll gains +1 bonus.";
                default: return "Unknown combo sequence.";
            }
        }

        /// <summary>
        /// Returns a summary of the reward provided by a given combo.
        /// </summary>
        public static string FormatMessageDiff(string? intended, string? delivered)
        {
            string d = delivered ?? "";
            if (string.IsNullOrWhiteSpace(intended) || intended.Trim() == "...")
                return d;
            if (string.Equals(intended.Trim(), d.Trim(), StringComparison.OrdinalIgnoreCase))
                return d;
            return $"*Intended: \"{intended}\"*\n*Delivered:*\n{d}";
        }
        public static string GetComboRewardSummary(string comboName)
        {
            switch (comboName)
            {
                case "The Recovery": return "+2 Interest if success";
                case "The Triple": return "+1 to ALL rolls next turn";
                default: return "+1 Interest if success";
            }
        }

        /// <summary>
        /// Returns a brief (&lt;10 word) rule explanation for a shadow growth/reduction event reason.
        /// </summary>
        public static string GetShadowRuleExplanation(string reason)
        {
            if (reason == null) return "";
            // Normalize for matching
            string r = reason.Trim();
            if (r.StartsWith("Nat 1 on")) return "Nat 1 grows paired shadow";
            if (r == "TropeTrap failure") return "every TropeTrap grows Madness";
            if (r == "RIZZ TropeTrap failure") return "RIZZ TropeTrap also grows Despair";
            if (r.StartsWith("Catastrophic Wit failure")) return "Wit catastrophe grows Dread";
            if (r.StartsWith("Same stat") && r.Contains("3 turns")) return "same stat 3x grows Fixation";
            if (r.Contains("Highest-% option picked")) return "safe picks grow Fixation";
            if (r == "Honesty success at high interest") return "Honesty success reduces Denial";
            if (r == "SA/Honesty success at high interest") return "SA/Honesty success reduces Despair";
            if (r == "Interest hit 0 (unmatch)") return "unmatch grows Dread";
            if (r.StartsWith("SA used 3+")) return "overusing SA grows Overthinking";
            if (r.StartsWith("CHARM used 3+")) return "overusing CHARM grows Madness";
            if (r.StartsWith("Combo success")) return "combo success reduces Madness";
            if (r.StartsWith("CHAOS combo")) return "CHAOS combo reduces Fixation";
            if (r == "Tell option selected") return "tell choice reduces Madness";
            if (r == "Date secured") return "date secured reduces Dread";
            if (r.Contains("without any Honesty")) return "no Honesty grows Denial";
            if (r.Contains("Never picked Chaos")) return "no Chaos grows Fixation";
            if (r.Contains("4+ different stats")) return "variety reduces Fixation";
            if (r == "Ghosted") return "ghosting grows Dread";
            if (r.Contains("3rd cumulative RIZZ failure")) return "repeated RIZZ fails grow Despair";
            if (r.Contains("Wit success variety")) return "Wit variety reduces Overthinking";
            if (r.Contains("Chaos success")) return "Chaos success reduces Overthinking";
            if (r.Contains("Rizz success")) return "Rizz success reduces Dread";
            if (r.Contains("miss acceptance")) return "accepting failure reduces Denial";
            return "";
        }

        /// <summary>
        /// Enriches a shadow event string by appending a brief rule explanation.
        /// Input format: "ShadowType +N (reason)" → "ShadowType +N (reason — explanation)"
        /// </summary>
        public static string EnrichShadowEvent(string shadowEvent)
        {
            if (string.IsNullOrEmpty(shadowEvent)) return shadowEvent;
            // Extract reason from parentheses
            int openParen = shadowEvent.IndexOf('(');
            int closeParen = shadowEvent.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen) return shadowEvent;
            string reason = shadowEvent.Substring(openParen + 1, closeParen - openParen - 1);
            string explanation = GetShadowRuleExplanation(reason);
            if (string.IsNullOrEmpty(explanation)) return shadowEvent;
            // Insert explanation before closing paren
            return shadowEvent.Substring(0, closeParen) + " — " + explanation + ")";
        }

        /// <summary>
        /// Returns a brief rule explanation for momentum display.
        /// </summary>
        public static string GetMomentumExplanation(int streak)
        {
            if (streak >= 5) return "5+ wins grant +3 bonus";
            if (streak >= 3) return "3+ consecutive wins grant bonus";
            return "";
        }

        /// <summary>
        /// Returns a brief combo rule explanation for the interest breakdown line.
        /// </summary>
        public static string GetComboBreakdownExplanation(string comboName)
        {
            switch (comboName)
            {
                case "The Setup": return "Wit→Charm sequence";
                case "The Reveal": return "Charm→Honesty sequence";
                case "The Read": return "SA→Honesty sequence";
                case "The Pivot": return "Honesty→Chaos sequence";
                case "The Escalation": return "Chaos→Rizz sequence";
                case "The Disarm": return "Wit→Honesty sequence";
                case "The Recovery": return "fail→SA sequence";
                case "The Triple": return "3 different stats in a row";
                default: return "combo bonus applied";
            }
        }
    }
}

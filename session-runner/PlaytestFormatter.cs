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
            sb.AppendLine($"**Player reasoning ({agentTypeName}):**");

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
    }
}

using System;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// The result of an IPlayerAgent decision: which option was chosen, why, and score breakdowns.
    /// </summary>
    public sealed class PlayerDecision
    {
        /// <summary>Index into TurnStart.Options (0-based).</summary>
        public int OptionIndex { get; }

        /// <summary>Human-readable explanation of why this option was chosen.</summary>
        public string Reasoning { get; }

        /// <summary>Score breakdown for every option in the TurnStart. Length == TurnStart.Options.Length.</summary>
        public OptionScore[] Scores { get; }

        public PlayerDecision(int optionIndex, string reasoning, OptionScore[] scores)
        {
            if (reasoning == null) throw new ArgumentNullException(nameof(reasoning));
            if (scores == null) throw new ArgumentNullException(nameof(scores));
            if (optionIndex < 0 || optionIndex >= scores.Length)
                throw new ArgumentOutOfRangeException(nameof(optionIndex),
                    $"OptionIndex {optionIndex} is out of range [0, {scores.Length}).");

            OptionIndex = optionIndex;
            Reasoning = reasoning;
            Scores = scores;
        }
    }
}

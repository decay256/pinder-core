using System;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Score breakdown for a single dialogue option, including success probability and expected gain.
    /// </summary>
    public sealed class OptionScore
    {
        /// <summary>Index of the option this score corresponds to.</summary>
        public int OptionIndex { get; }

        /// <summary>Composite score (higher = better pick). Implementation-defined scale.</summary>
        public float Score { get; }

        /// <summary>Estimated probability of beating the DC, as a value 0.0–1.0.</summary>
        public float SuccessChance { get; }

        /// <summary>Expected interest gain (positive or negative), weighting success and failure outcomes.</summary>
        public float ExpectedInterestGain { get; }

        /// <summary>Human-readable list of bonuses factored into the score, e.g. ["callback +2", "tell +2"].</summary>
        public string[] BonusesApplied { get; }

        public OptionScore(
            int optionIndex,
            float score,
            float successChance,
            float expectedInterestGain,
            string[] bonusesApplied)
        {
            if (bonusesApplied == null) throw new ArgumentNullException(nameof(bonusesApplied));

            OptionIndex = optionIndex;
            Score = score;
            SuccessChance = Math.Max(0.0f, Math.Min(1.0f, successChance));
            ExpectedInterestGain = expectedInterestGain;
            BonusesApplied = bonusesApplied;
        }
    }
}

using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Final assembled timing profile for a character.
    /// Produced by summing all TimingModifiers from items and anatomy tiers.
    /// </summary>
    public sealed class TimingProfile
    {
        /// <summary>Base delay in minutes before interest and variance are applied.</summary>
        public int   BaseDelayMinutes   { get; }

        /// <summary>Compounded variance multiplier from all sources.</summary>
        public float VarianceMultiplier { get; }

        /// <summary>Probability (0–1) of entering a dry spell (no reply).</summary>
        public float DrySpellProbability { get; }

        /// <summary>"neutral" | "shows" | "hides"</summary>
        public string ReadReceipt { get; }

        public TimingProfile(int baseDelay, float variance, float drySpell, string readReceipt)
        {
            BaseDelayMinutes    = baseDelay;
            VarianceMultiplier  = variance;
            DrySpellProbability = drySpell;
            ReadReceipt         = readReceipt ?? "neutral";
        }

        /// <summary>
        /// Compute the actual reply delay in minutes for a given interest level.
        ///
        /// Formula:
        ///   1. Interest reduction: at interest=Max, subtract 50 % of base delay.
        ///      adjusted = BaseDelay * (1 – 0.5 * interestLevel / Max)
        ///   2. Variance: roll d100, map [1,100] → [1 – VM*0.5, 1 + VM*0.5], multiply adjusted.
        ///   3. Floor at 1 minute.
        /// </summary>
        public int ComputeDelay(int interestLevel, IDiceRoller dice)
        {
            if (dice == null) throw new ArgumentNullException(nameof(dice));

            // Clamp interest to valid range
            int interest = Math.Max(0, Math.Min(InterestMeter.Max, interestLevel));

            // Step 1 – interest reduction
            float interestFraction = interest / (float)InterestMeter.Max;
            float adjusted         = BaseDelayMinutes * (1.0f - 0.5f * interestFraction);

            // Step 2 – variance
            int   dieRoll      = dice.Roll(100);          // 1..100
            float normalised   = (dieRoll - 1) / 99.0f;  // 0..1
            float varianceFactor = 1.0f + VarianceMultiplier * (normalised - 0.5f);

            float result = adjusted * varianceFactor;

            // Step 3 – floor at 1
            return Math.Max(1, (int)result);
        }
    }
}

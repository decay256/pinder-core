using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Computes simulated opponent reply delay from TimingProfile, interest state,
    /// and shadow stat modifiers. Pure computation — no side effects, no clock.
    /// </summary>
    public static class OpponentTimingCalculator
    {
        /// <summary>
        /// Computes the opponent's reply delay in minutes.
        /// </summary>
        /// <param name="profile">The opponent's assembled TimingProfile.</param>
        /// <param name="interest">Current InterestState of the conversation.</param>
        /// <param name="shadows">
        ///   Map of the opponent's current shadow stat values.
        ///   Only keys with value ≥ 6 affect the result. Missing keys treated as 0.
        ///   Null is treated as empty (no modifiers).
        /// </param>
        /// <param name="dice">Dice roller for randomness (variance, dry spell, madness outlier).</param>
        /// <param name="previousDelay">
        ///   The delay returned by the previous call, if any. Used by the Fixation shadow modifier
        ///   (≥ 6) to return the same delay (rigid schedule). Pass null on first call of a session.
        /// </param>
        /// <returns>Delay in minutes (always ≥ 1.0).</returns>
        public static double ComputeDelayMinutes(
            TimingProfile profile,
            InterestState interest,
            Dictionary<ShadowStatType, int> shadows,
            IDiceRoller dice,
            double? previousDelay = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (dice == null) throw new ArgumentNullException(nameof(dice));

            // Validate interest is a defined enum value
            if (!Enum.IsDefined(typeof(InterestState), interest))
                throw new ArgumentOutOfRangeException(nameof(interest), interest, "Undefined InterestState value.");

            // Terminal states: defensive handling
            if (interest == InterestState.Unmatched) return 999999.0;
            if (interest == InterestState.DateSecured) return 1.0;

            var effectiveShadows = shadows ?? new Dictionary<ShadowStatType, int>();

            // Step 1: Base delay with variance
            int varianceRoll = dice.Roll(100);
            double normalized = (varianceRoll - 1) / 99.0;
            double varianceFactor = 1.0 + profile.VarianceMultiplier * (normalized - 0.5);
            double delay = profile.BaseDelayMinutes * varianceFactor;

            // Step 2: Interest multiplier
            double multiplier = GetInterestMultiplier(interest);
            delay *= multiplier;

            // Step 3: Shadow modifiers (order: Overthinking → Madness → Denial → Fixation)

            // Overthinking ≥ 6: +50%
            if (GetShadowValue(effectiveShadows, ShadowStatType.Overthinking) >= 6)
            {
                delay *= 1.5;
            }

            // Madness ≥ 6: 20% chance of extreme outlier
            if (GetShadowValue(effectiveShadows, ShadowStatType.Madness) >= 6)
            {
                int madnessRoll = dice.Roll(100);
                if (madnessRoll <= 20)
                {
                    // Choose between 1 minute and 240–480 minutes
                    int choiceRoll = dice.Roll(2);
                    delay = choiceRoll == 1 ? 1.0 : 240.0 + dice.Roll(241) - 1;
                }
            }

            // Denial ≥ 6: snap to nearest 5 minutes
            if (GetShadowValue(effectiveShadows, ShadowStatType.Denial) >= 6)
            {
                delay = Math.Round(delay / 5.0) * 5.0;
                if (delay < 5.0) delay = 5.0;
            }

            // Fixation ≥ 6: return previous delay if available
            if (GetShadowValue(effectiveShadows, ShadowStatType.Fixation) >= 6)
            {
                if (previousDelay.HasValue)
                {
                    return Math.Max(1.0, previousDelay.Value);
                }
                // First call with no previous delay — fall through to normal result
            }

            // Step 4: Dry spell check
            float drySpellProb = Math.Max(0.0f, Math.Min(1.0f, profile.DrySpellProbability));
            if (drySpellProb > 0.0f)
            {
                int drySpellRoll = dice.Roll(100);
                if (drySpellRoll <= (int)(drySpellProb * 100))
                {
                    delay = 120.0 + dice.Roll(361) - 1; // [120, 480]
                }
            }

            return Math.Max(1.0, delay);
        }

        private static double GetInterestMultiplier(InterestState state)
        {
            switch (state)
            {
                case InterestState.Bored:       return 5.0;
                case InterestState.Lukewarm:    return 2.0;
                case InterestState.Interested:  return 1.0;
                case InterestState.VeryIntoIt:  return 0.5;
                case InterestState.AlmostThere: return 0.3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state,
                        "Terminal states should be handled before calling GetInterestMultiplier.");
            }
        }

        private static int GetShadowValue(Dictionary<ShadowStatType, int> shadows, ShadowStatType type)
        {
            return shadows.TryGetValue(type, out int val) ? val : 0;
        }
    }
}

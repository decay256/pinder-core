using System;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Pure function: evaluates the interest penalty for the player taking too long
    /// to respond. Penalty is modified by the opponent's personality stats.
    /// </summary>
    public static class PlayerResponseDelayEvaluator
    {
        private const string DefaultTestPrompt = "Opponent noticed the long gap between replies";

        /// <summary>
        /// Evaluates the interest penalty for the player taking <paramref name="delay"/>
        /// to respond. The penalty is modified by the opponent's personality stats.
        /// </summary>
        /// <param name="delay">Time elapsed since the opponent's last message.
        ///     Provided by GameClock (issue #54). Must be non-negative.</param>
        /// <param name="opponentStats">The opponent's full StatBlock, used to check
        ///     Chaos base stat and shadow stat values (Fixation, Overthinking).</param>
        /// <param name="currentInterest">The current InterestState of the conversation,
        ///     used to gate the 15–60 min penalty bucket.</param>
        /// <returns>A DelayPenalty describing the interest delta and optional test trigger.</returns>
        public static DelayPenalty Evaluate(
            TimeSpan delay,
            StatBlock opponentStats,
            InterestState currentInterest)
        {
            if (opponentStats == null)
                throw new ArgumentNullException(nameof(opponentStats));

            // Negative delay = no time has passed
            if (delay.Ticks < 0)
                return new DelayPenalty(0, false, null);

            // Step 1-2: Determine delay bucket and base penalty
            int basePenalty = GetBasePenalty(delay, currentInterest);
            bool isOneToSixHourBucket = IsOneToSixHourBucket(delay);

            // If base penalty is 0, short-circuit — no modifiers needed
            if (basePenalty == 0)
                return new DelayPenalty(0, false, null);

            // Step 3: Chaos override — base stat >= 4 zeroes everything
            if (opponentStats.GetBase(StatType.Chaos) >= 4)
                return new DelayPenalty(0, false, null);

            // Step 4: Fixation doubling
            int penalty = basePenalty;
            if (opponentStats.GetShadow(ShadowStatType.Fixation) >= 6)
                penalty *= 2;

            // Step 5: Overthinking +1 additional penalty
            if (opponentStats.GetShadow(ShadowStatType.Overthinking) >= 6)
                penalty -= 1;

            // Step 6: Trigger test only in 1-6h bucket with non-zero penalty
            bool triggerTest = isOneToSixHourBucket && penalty != 0;
            string? testPrompt = triggerTest ? DefaultTestPrompt : null;

            return new DelayPenalty(penalty, triggerTest, testPrompt);
        }

        private static int GetBasePenalty(TimeSpan delay, InterestState currentInterest)
        {
            double totalMinutes = delay.TotalMinutes;

            if (totalMinutes < 1.0)
                return 0;
            if (totalMinutes < 15.0)
                return 0;
            if (totalMinutes < 60.0)
            {
                // 15-60 min bucket: only applies if interest >= 16
                if (currentInterest == InterestState.VeryIntoIt ||
                    currentInterest == InterestState.AlmostThere ||
                    currentInterest == InterestState.DateSecured)
                    return -1;
                return 0;
            }
            if (totalMinutes < 360.0) // < 6 hours
                return -2;
            if (totalMinutes < 1440.0) // < 24 hours
                return -3;
            return -5; // 24+ hours
        }

        private static bool IsOneToSixHourBucket(TimeSpan delay)
        {
            double totalMinutes = delay.TotalMinutes;
            return totalMinutes >= 60.0 && totalMinutes < 360.0;
        }
    }
}

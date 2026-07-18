using System;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Pure function: evaluates the interest penalty for the player taking too long
    /// to respond. Penalty is modified by the datee's personality stats.
    /// </summary>
    public static class PlayerResponseDelayEvaluator
    {
        private const string DefaultTestPrompt = "Datee noticed the long gap between replies";
        private const double ImmediateReplyUpperMinutes = 1.0;
        private const double ShortReplyUpperMinutes = 15.0;
        private const double InterestedReplyUpperMinutes = 60.0;
        private const double OneToSixHoursUpperMinutes = 360.0;
        private const double TwentyFourHoursUpperMinutes = 1440.0;

        private const int NoDelayPenalty = 0;
        private const int InterestedReplyPenalty = -1;
        private const int OneToSixHoursPenalty = -2;
        private const int SixToTwentyFourHoursPenalty = -3;
        private const int TwentyFourHoursPlusPenalty = -5;

        /// <summary>
        /// Evaluates the interest penalty for the player taking <paramref name="delay"/>
        /// to respond. The penalty is modified by the datee's personality stats.
        /// </summary>
        /// <param name="delay">Time elapsed since the datee's last message.
        ///     Provided by GameClock (issue #54). Must be non-negative.</param>
        /// <param name="dateeStats">The datee's full StatBlock, used to check
        ///     Chaos base stat and shadow stat values (Fixation, Overthinking).</param>
        /// <param name="currentInterest">The current InterestState of the conversation,
        ///     used to gate the 15–60 min penalty bucket.</param>
        /// <returns>A DelayPenalty describing the interest delta and optional test trigger.</returns>
        public static DelayPenalty Evaluate(
            TimeSpan delay,
            StatBlock dateeStats,
            InterestState currentInterest)
        {
            if (dateeStats == null)
                throw new ArgumentNullException(nameof(dateeStats));

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
            if (dateeStats.GetBase(StatType.Chaos) >= 4)
                return new DelayPenalty(0, false, null);

            // Step 4: Fixation doubling
            int penalty = basePenalty;
            if (dateeStats.GetShadow(ShadowStatType.Fixation) >= 6)
                penalty *= 2;

            // Step 5: Overthinking +1 additional penalty
            if (dateeStats.GetShadow(ShadowStatType.Overthinking) >= 6)
                penalty -= 1;

            // Step 6: Trigger test only in 1-6h bucket with non-zero penalty
            bool triggerTest = isOneToSixHourBucket && penalty != 0;
            string? testPrompt = triggerTest ? DefaultTestPrompt : null;

            return new DelayPenalty(penalty, triggerTest, testPrompt);
        }

        private static int GetBasePenalty(TimeSpan delay, InterestState currentInterest)
        {
            double totalMinutes = delay.TotalMinutes;

            if (totalMinutes < ImmediateReplyUpperMinutes)
                return NoDelayPenalty;
            if (totalMinutes < ShortReplyUpperMinutes)
                return NoDelayPenalty;
            if (totalMinutes < InterestedReplyUpperMinutes)
            {
                // 15-60 min bucket: only applies if interest >= 16
                if (currentInterest == InterestState.VeryIntoIt ||
                    currentInterest == InterestState.AlmostThere ||
                    currentInterest == InterestState.DateSecured)
                    return InterestedReplyPenalty;
                return NoDelayPenalty;
            }
            if (totalMinutes < OneToSixHoursUpperMinutes)
                return OneToSixHoursPenalty;
            if (totalMinutes < TwentyFourHoursUpperMinutes)
                return SixToTwentyFourHoursPenalty;
            return TwentyFourHoursPlusPenalty;
        }

        private static bool IsOneToSixHourBucket(TimeSpan delay)
        {
            double totalMinutes = delay.TotalMinutes;
            return totalMinutes >= InterestedReplyUpperMinutes &&
                   totalMinutes < OneToSixHoursUpperMinutes;
        }
    }
}

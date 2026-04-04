using Pinder.Core.Conversation;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Pure function: given game state at cutoff, returns projected outcome text.
    /// </summary>
    internal static class OutcomeProjector
    {
        /// <summary>
        /// Projects the likely outcome when the session ends due to turn cap.
        /// </summary>
        /// <param name="interest">Current interest value (0-25).</param>
        /// <param name="momentum">Current momentum streak count.</param>
        /// <param name="turnNumber">Turn the session ended on.</param>
        /// <param name="maxTurns">The turn cap that was hit.</param>
        /// <param name="interestState">Current interest state enum.</param>
        /// <returns>Human-readable projection string.</returns>
        internal static string Project(int interest, int momentum, int turnNumber, int maxTurns, InterestState interestState)
        {
            // Momentum bonus for next roll: 3-4 streak → +2, 5+ → +3
            int momentumBonus = momentum >= 5 ? 3 : momentum >= 3 ? 2 : 0;

            int remaining = 25 - interest;

            // Estimate average interest gain per successful turn based on state
            // Conservative: assume +1.5 average per turn (mix of successes and failures)
            // With momentum active, bump estimate
            double avgGainPerTurn = 1.5;
            if (momentumBonus > 0) avgGainPerTurn = 2.5;
            if (interestState == InterestState.VeryIntoIt || interestState == InterestState.AlmostThere)
                avgGainPerTurn += 0.5; // advantage helps

            int estimatedTurnsToClose = remaining > 0 ? (int)System.Math.Ceiling(remaining / avgGainPerTurn) : 0;

            if (interestState == InterestState.DateSecured)
            {
                return "DateSecured already achieved.";
            }

            if (interestState == InterestState.Unmatched)
            {
                return "Unmatched — interest hit 0.";
            }

            if (interestState == InterestState.AlmostThere)
            {
                string momentumNote = momentumBonus > 0
                    ? $"Momentum {momentum} (+{momentumBonus} next roll) and "
                    : "";
                return $"Likely DateSecured given {momentumNote}Interest {interest}/25 (need +{remaining} more). "
                     + $"Expected turns to close: {estimatedTurnsToClose} at current rate.";
            }

            if (interestState == InterestState.VeryIntoIt)
            {
                string momentumNote = momentumBonus > 0
                    ? $"Momentum {momentum} (+{momentumBonus} next roll) active. "
                    : "";
                return $"Probable DateSecured — {momentumNote}Interest {interest}/25 with advantage. "
                     + $"Need +{remaining} more, ~{estimatedTurnsToClose} turns at current rate.";
            }

            if (interestState == InterestState.Interested || interestState == InterestState.Lukewarm)
            {
                string outlook = interest >= 12 ? "Possible DateSecured" : "Uncertain outcome";
                string momentumNote = momentumBonus > 0
                    ? $" Momentum {momentum} (+{momentumBonus}) helps."
                    : "";
                return $"{outlook} — Interest {interest}/25, need +{remaining} more.{momentumNote} "
                     + $"~{estimatedTurnsToClose} turns needed at current rate.";
            }

            if (interestState == InterestState.Bored)
            {
                return $"Likely Unmatched — Interest {interest}/25 with disadvantage. "
                     + $"Recovery would require ~{estimatedTurnsToClose} successful turns.";
            }

            return $"Incomplete — Interest {interest}/25, Momentum {momentum}.";
        }
    }
}

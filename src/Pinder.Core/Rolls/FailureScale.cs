namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps failure tiers to negative interest deltas per rules-v3.4 §5.
    /// Fumble → -1, Misfire → -1, TropeTrap → -2, Catastrophe → -3, Legendary → -4.
    /// Returns 0 for success (FailureTier.None).
    /// Additional effects (trap activation, shadow growth) are handled by GameSession.
    /// </summary>
    public static class FailureScale
    {
        /// <summary>
        /// Compute the interest delta for a failed roll.
        /// Returns 0 for successes.
        /// </summary>
        public static int GetInterestDelta(RollResult result)
        {
            switch (result.Tier)
            {
                case FailureTier.Fumble:      return -1;
                case FailureTier.Misfire:     return -1;
                case FailureTier.TropeTrap:   return -2;
                case FailureTier.Catastrophe: return -3;
                case FailureTier.Legendary:   return -4;
                default:                      return 0;
            }
        }
    }
}

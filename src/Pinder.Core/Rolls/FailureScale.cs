namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps failure tiers to negative interest deltas. Prototype defaults per #28.
    /// Fumble → -1, Misfire → -2, TropeTrap → -3, Catastrophe → -4, Legendary → -5.
    /// Returns 0 for success (FailureTier.None).
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
                case FailureTier.Misfire:     return -2;
                case FailureTier.TropeTrap:   return -3;
                case FailureTier.Catastrophe: return -4;
                case FailureTier.Legendary:   return -5;
                default:                      return 0;
            }
        }
    }
}

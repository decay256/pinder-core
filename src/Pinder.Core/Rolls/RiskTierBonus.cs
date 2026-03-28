using System;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Returns the bonus Interest for successful rolls at Hard/Bold risk tiers.
    /// Rules v3.4 §5: Hard → +1, Bold → +2.
    /// </summary>
    public static class RiskTierBonus
    {
        /// <summary>
        /// Returns the bonus Interest delta for a successful roll at the given risk tier.
        /// Returns 0 for failures or Safe/Medium tiers.
        /// </summary>
        /// <param name="result">The roll result to evaluate.</param>
        /// <returns>0, 1, or 2 depending on tier and success.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="result"/> is null.</exception>
        public static int GetInterestBonus(RollResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            if (!result.IsSuccess)
                return 0;

            switch (result.RiskTier)
            {
                case RiskTier.Hard:
                    return 1;
                case RiskTier.Bold:
                    return 2;
                default:
                    return 0;
            }
        }
    }
}

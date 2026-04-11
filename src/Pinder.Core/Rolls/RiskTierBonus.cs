using System;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Returns the bonus Interest for successful rolls, scaled inversely to probability
    /// so that expected value is approximately equal across all tiers.
    /// 
    /// Tier bonuses (Option 3 design):
    ///   Safe     (need 1-7,  ~70-100%): +1
    ///   Medium   (need 8-11, ~50-65%):  +2
    ///   Hard     (need 12-15,~30-45%):  +3
    ///   Bold     (need 16-19,~10-25%):  +5
    ///   Reckless (need 20+,  ~0-5%):    +10
    /// 
    /// EV is approximately 0.9-1.35 across Safe/Medium/Hard; Bold/Reckless are higher-
    /// variance but provide comparable EV in their success range.
    /// </summary>
    public static class RiskTierBonus
    {
        /// <summary>
        /// Returns the bonus Interest delta for a successful roll at the given risk tier.
        /// Returns 0 for failures.
        /// </summary>
        public static int GetInterestBonus(RollResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            if (!result.IsSuccess)
                return 0;

            switch (result.RiskTier)
            {
                case RiskTier.Safe:      return 1;
                case RiskTier.Medium:    return 2;
                case RiskTier.Hard:      return 3;
                case RiskTier.Bold:      return 5;
                case RiskTier.Reckless:  return 10;
                default:                 return 1;
            }
        }
    }
}

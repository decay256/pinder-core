using Pinder.Core.Conversation;
using Pinder.Core.Rolls;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction for data-driven game rule resolution.
    /// When injected into GameSession via GameSessionConfig, replaces
    /// hardcoded static class lookups for §5/§6/§7/§15 rules.
    /// Returns null when no matching rule is found — caller uses
    /// hardcoded fallback.
    /// </summary>
    public interface IRuleResolver
    {
        /// <summary>
        /// §5 failure tier → interest delta.
        /// Returns null if no matching rule found.
        /// </summary>
        /// <param name="missMargin">How much the roll missed by (positive int).</param>
        /// <param name="naturalRoll">The natural d20 value (1-20). 1 = Legendary fail.</param>
        int? GetFailureInterestDelta(int missMargin, int naturalRoll);

        /// <summary>
        /// §5 success scale → interest delta.
        /// Returns null if no matching rule found.
        /// </summary>
        /// <param name="beatMargin">How much the roll beat DC by (positive int).</param>
        /// <param name="naturalRoll">The natural d20 value (1-20). 20 = crit.</param>
        int? GetSuccessInterestDelta(int beatMargin, int naturalRoll);

        /// <summary>
        /// §6 interest value → InterestState mapping.
        /// Returns null if no matching rule found.
        /// </summary>
        InterestState? GetInterestState(int interest);

        /// <summary>
        /// §7 shadow value → threshold level (0/1/2/3).
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetShadowThresholdLevel(int shadowValue);

        /// <summary>
        /// §15 momentum streak → roll bonus.
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetMomentumBonus(int streak);

        /// <summary>
        /// §15 risk tier → XP multiplier.
        /// Returns null if no matching rule found.
        /// </summary>
        double? GetRiskTierXpMultiplier(RiskTier riskTier);
    }
}

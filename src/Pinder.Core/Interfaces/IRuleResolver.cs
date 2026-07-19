using Pinder.Core.Conversation;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction for data-driven game rule resolution.
    /// Hosts provide an implementation at composition boundaries; Core owns
    /// the contract and does not fabricate missing rule data.
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

        /// <summary>
        /// §10 terminal outcome → XP multiplier.
        /// Returns null if no matching rule found.
        /// </summary>
        double? GetTerminalOutcomeMultiplier(GameOutcome outcome);

        /// <summary>
        /// Success base XP based on DC.
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetSuccessBaseXp(int dc);

        /// <summary>
        /// DC bucket thresholds used to label successful roll XP ledger entries.
        /// Returns null if no matching rule found.
        /// </summary>
        SuccessDcLabelThresholds? GetSuccessDcLabelThresholds();

        /// <summary>
        /// Flat XP award based on string type (e.g. Nat20, Nat1, Failure).
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetFlatXpAward(string awardType);

        /// <summary>
        /// XP required to REACH this 1-based level
        /// </summary>
        int? GetXpThresholdForLevel(int level);

        /// <summary>
        /// flat d20 bonus at this 1-based level
        /// </summary>
        int? GetLevelRollBonus(int level);

        /// <summary>
        /// build points granted on reaching this level
        /// </summary>
        int? GetBuildPointsForLevel(int level);

        /// <summary>
        /// max item slots at this level
        /// </summary>
        int? GetItemSlotsForLevel(int level);

        /// <summary>
        /// failure pool tier minimum level based on tier name (e.g. intermediate_min, advanced_min, legendary_min)
        /// </summary>
        int? GetFailurePoolTierMinLevel(string tierName);

        /// <summary>
        /// Currency awarded per persisted XP at terminal settlement.
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetProgressionCurrencyPerXp();

        /// <summary>
        /// Explicit policy: whether callers (e.g. <see cref="Pinder.Core.Progression.LevelTable"/> and
        /// SessionXpRecorder) are allowed to fall back to <see cref="DefaultRuleResolver.Instance"/> when
        /// this resolver returns null for a given lookup.
        /// Real, data-driven resolvers should return false: production config is authoritative,
        /// and missing values must throw rather than quietly resolve to hardcoded defaults. Only
        /// explicit test/dev resolvers that intentionally support partial data should return true.
        /// This is an explicit, caller-visible capability flag — it must never be inferred by sniffing
        /// the resolver's runtime type name or declaring assembly name.
        /// </summary>
        bool AllowDefaultFallback { get; }
    }

    /// <summary>
    /// Host-managed provider for a default rules resolver.
    /// </summary>
    public static class DefaultRuleResolver
    {
        private static IRuleResolver? _instance;

        public static IRuleResolver? Instance
        {
            get { return _instance; }
            set
            {
                _instance = value;
            }
        }
    }

}

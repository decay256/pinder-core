using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Progression;
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
    /// Global provider for the default rules resolver.
    /// </summary>
    public static class DefaultRuleResolver
    {
        private static IRuleResolver? _instance = CoreDefaultRuleResolver.Instance;

        public static IRuleResolver? Instance
        {
            get { return _instance ?? CoreDefaultRuleResolver.Instance; }
            set
            {
                _instance = value;
            }
        }
    }

    internal sealed class CoreDefaultRuleResolver : IRuleResolver
    {
        internal static readonly CoreDefaultRuleResolver Instance = new CoreDefaultRuleResolver();

        private readonly IReadOnlyDictionary<string, int> _xpFlatAwards = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "nat20", 25 },
            { "nat1", 10 },
            { "failure", 2 },
        };

        private readonly IReadOnlyDictionary<string, int> _xpSuccessBase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "dc_low_max", 16 },
            { "dc_low_xp", 5 },
            { "dc_mid_max", 20 },
            { "dc_mid_xp", 10 },
            { "dc_high_xp", 15 },
        };

        private readonly IReadOnlyDictionary<string, double> _riskMultipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "safe", 1.0 },
            { "medium", 1.5 },
            { "hard", 2.0 },
            { "bold", 3.0 },
            { "reckless", 10.0 },
        };

        private readonly IReadOnlyDictionary<string, double> _terminalMultipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "date_secured", 3.0 },
            { "unmatched", 1.0 },
            { "ghosted", 1.0 },
        };

        private readonly IReadOnlyDictionary<int, int> _xpThresholds = new Dictionary<int, int>
        {
            { 1, 0 }, { 2, 50 }, { 3, 150 }, { 4, 300 }, { 5, 500 }, { 6, 750 },
            { 7, 1100 }, { 8, 1500 }, { 9, 2000 }, { 10, 2750 }, { 11, 3500 },
        };

        private readonly IReadOnlyDictionary<int, int> _buildPoints = new Dictionary<int, int>
        {
            { 1, 0 }, { 2, 2 }, { 3, 2 }, { 4, 2 }, { 5, 3 }, { 6, 3 },
            { 7, 3 }, { 8, 4 }, { 9, 4 }, { 10, 5 }, { 11, 0 },
        };

        private readonly IReadOnlyDictionary<int, int> _levelBonuses = new Dictionary<int, int>
        {
            { 1, 0 }, { 2, 0 }, { 3, 1 }, { 4, 1 }, { 5, 2 }, { 6, 2 },
            { 7, 3 }, { 8, 3 }, { 9, 4 }, { 10, 4 }, { 11, 5 },
        };

        private readonly IReadOnlyDictionary<int, int> _itemSlots = new Dictionary<int, int>
        {
            { 1, 2 }, { 2, 2 }, { 3, 3 }, { 4, 3 }, { 5, 4 }, { 6, 4 },
            { 7, 5 }, { 8, 5 }, { 9, 6 }, { 10, 6 }, { 11, 6 },
        };

        private readonly IReadOnlyDictionary<string, int> _failurePoolTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "intermediate_min", 4 },
            { "advanced_min", 7 },
            { "legendary_min", 10 },
        };

        private CoreDefaultRuleResolver()
        {
        }

        public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
        public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => null;
        public InterestState? GetInterestState(int interest) => null;
        public int? GetShadowThresholdLevel(int shadowValue) => null;
        public int? GetMomentumBonus(int streak) => null;

        public double? GetRiskTierXpMultiplier(RiskTier riskTier)
        {
            string key = riskTier.ToString().ToLowerInvariant();
            return _riskMultipliers.TryGetValue(key, out double value) ? value : (double?)null;
        }

        public double? GetTerminalOutcomeMultiplier(GameOutcome outcome)
        {
            string key;
            switch (outcome)
            {
                case GameOutcome.DateSecured: key = "date_secured"; break;
                case GameOutcome.Unmatched: key = "unmatched"; break;
                case GameOutcome.Ghosted: key = "ghosted"; break;
                default: return null;
            }
            return _terminalMultipliers.TryGetValue(key, out double value) ? value : (double?)null;
        }

        public int? GetSuccessBaseXp(int dc)
        {
            var thresholds = GetSuccessDcLabelThresholds();
            if (!thresholds.HasValue) return null;
            if (dc <= thresholds.Value.LowMax) return _xpSuccessBase["dc_low_xp"];
            if (dc <= thresholds.Value.MidMax) return _xpSuccessBase["dc_mid_xp"];
            return _xpSuccessBase["dc_high_xp"];
        }

        public SuccessDcLabelThresholds? GetSuccessDcLabelThresholds()
            => new SuccessDcLabelThresholds(_xpSuccessBase["dc_low_max"], _xpSuccessBase["dc_mid_max"]);

        public int? GetFlatXpAward(string awardType)
        {
            if (awardType == null) throw new ArgumentNullException(nameof(awardType));
            return _xpFlatAwards.TryGetValue(awardType, out int value) ? value : (int?)null;
        }

        public int? GetXpThresholdForLevel(int level)
        {
            if (_xpThresholds.TryGetValue(level, out int value)) return value;
            return level > 11 ? (int?)null : throw new KeyNotFoundException($"Missing progression XP threshold for level {level}.");
        }

        public int? GetLevelRollBonus(int level)
            => _levelBonuses.TryGetValue(level, out int value) ? value : (int?)null;

        public int? GetBuildPointsForLevel(int level)
            => _buildPoints.TryGetValue(level, out int value) ? value : (int?)null;

        public int? GetItemSlotsForLevel(int level)
            => _itemSlots.TryGetValue(level, out int value) ? value : (int?)null;

        public int? GetFailurePoolTierMinLevel(string tierName)
        {
            if (tierName == null) throw new ArgumentNullException(nameof(tierName));
            return _failurePoolTiers.TryGetValue(tierName, out int value) ? value : (int?)null;
        }

        public bool AllowDefaultFallback => false;
    }
}

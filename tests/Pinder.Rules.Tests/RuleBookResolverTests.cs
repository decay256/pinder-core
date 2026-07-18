using System.IO;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Rules;

namespace Pinder.Rules.Tests
{
    public class RuleBookResolverTests
    {
        private static string LoadRulesV3Yaml()
        {
            // Walk up from test bin to repo root
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "rules", "extracted", "rules-v3-enriched.yaml")))
                dir = Directory.GetParent(dir)?.FullName;

            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!, "rules", "extracted", "rules-v3-enriched.yaml"));
        }

        private static string LoadRiskRewardYaml()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "rules", "extracted", "risk-reward-and-hidden-depth-enriched.yaml")))
                dir = Directory.GetParent(dir)?.FullName;

            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!, "rules", "extracted", "risk-reward-and-hidden-depth-enriched.yaml"));
        }

        private static RuleBookResolver CreateResolver()
        {
            return RuleBookResolver.FromYaml(LoadRulesV3Yaml(), LoadRiskRewardYaml());
        }

        // =====================================================================
        // §5 — Failure Interest Deltas
        // =====================================================================

        [Fact]
        public void GetFailureInterestDelta_Fumble_MissBy1_ReturnsNeg1()
        {
            var resolver = CreateResolver();
            Assert.Equal(-1, resolver.GetFailureInterestDelta(1, 10));
        }

        [Fact]
        public void GetFailureInterestDelta_Fumble_MissBy2_ReturnsNeg1()
        {
            var resolver = CreateResolver();
            Assert.Equal(-1, resolver.GetFailureInterestDelta(2, 10));
        }

        [Fact]
        public void GetFailureInterestDelta_Misfire_MissBy4_ReturnsNeg1()
        {
            var resolver = CreateResolver();
            Assert.Equal(-1, resolver.GetFailureInterestDelta(4, 10));
        }

        [Fact]
        public void GetFailureInterestDelta_TropeTrap_MissBy7_ReturnsNeg2()
        {
            var resolver = CreateResolver();
            Assert.Equal(-2, resolver.GetFailureInterestDelta(7, 10));
        }

        [Fact]
        public void GetFailureInterestDelta_Catastrophe_MissBy11_ReturnsNeg3()
        {
            var resolver = CreateResolver();
            Assert.Equal(-3, resolver.GetFailureInterestDelta(11, 10));
        }

        [Fact]
        public void GetFailureInterestDelta_Legendary_Nat1_ReturnsNeg4()
        {
            var resolver = CreateResolver();
            Assert.Equal(-4, resolver.GetFailureInterestDelta(14, 1));
        }

        // =====================================================================
        // §5 — Success Interest Deltas
        // =====================================================================

        [Fact]
        public void GetSuccessInterestDelta_BeatBy3_ReturnsPlus1()
        {
            var resolver = CreateResolver();
            Assert.Equal(1, resolver.GetSuccessInterestDelta(3, 15));
        }

        [Fact]
        public void GetSuccessInterestDelta_BeatBy7_ReturnsPlus2()
        {
            var resolver = CreateResolver();
            Assert.Equal(2, resolver.GetSuccessInterestDelta(7, 18));
        }

        [Fact]
        public void GetSuccessInterestDelta_BeatBy12_ReturnsPlus3()
        {
            var resolver = CreateResolver();
            Assert.Equal(3, resolver.GetSuccessInterestDelta(12, 19));
        }

        [Fact]
        public void GetSuccessInterestDelta_Nat20_ReturnsPlus4()
        {
            var resolver = CreateResolver();
            Assert.Equal(4, resolver.GetSuccessInterestDelta(7, 20));
        }

        // =====================================================================
        // §6 — Interest States
        // =====================================================================

        [Fact]
        public void GetInterestState_0_ReturnsUnmatched()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.Unmatched, resolver.GetInterestState(0));
        }

        [Fact]
        public void GetInterestState_3_ReturnsBored()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.Bored, resolver.GetInterestState(3));
        }

        [Fact]
        public void GetInterestState_7_ReturnsLukewarm()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.Lukewarm, resolver.GetInterestState(7));
        }

        [Fact]
        public void GetInterestState_12_ReturnsInterested()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.Interested, resolver.GetInterestState(12));
        }

        [Fact]
        public void GetInterestState_18_ReturnsVeryIntoIt()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.VeryIntoIt, resolver.GetInterestState(18));
        }

        [Fact]
        public void GetInterestState_22_ReturnsAlmostThere()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.AlmostThere, resolver.GetInterestState(22));
        }

        [Fact]
        public void GetInterestState_25_ReturnsDateSecured()
        {
            var resolver = CreateResolver();
            Assert.Equal(InterestState.DateSecured, resolver.GetInterestState(25));
        }

        // =====================================================================
        // §7 — Shadow Thresholds
        // =====================================================================

        [Fact]
        public void GetShadowThresholdLevel_5_Returns0()
        {
            var resolver = CreateResolver();
            Assert.Equal(0, resolver.GetShadowThresholdLevel(5));
        }

        [Fact]
        public void GetShadowThresholdLevel_6_Returns1()
        {
            var resolver = CreateResolver();
            Assert.Equal(1, resolver.GetShadowThresholdLevel(6));
        }

        [Fact]
        public void GetShadowThresholdLevel_11_Returns1()
        {
            var resolver = CreateResolver();
            Assert.Equal(1, resolver.GetShadowThresholdLevel(11));
        }

        [Fact]
        public void GetShadowThresholdLevel_12_Returns2()
        {
            var resolver = CreateResolver();
            Assert.Equal(2, resolver.GetShadowThresholdLevel(12));
        }

        [Fact]
        public void GetShadowThresholdLevel_17_Returns2()
        {
            var resolver = CreateResolver();
            Assert.Equal(2, resolver.GetShadowThresholdLevel(17));
        }

        [Fact]
        public void GetShadowThresholdLevel_18_Returns3()
        {
            var resolver = CreateResolver();
            Assert.Equal(3, resolver.GetShadowThresholdLevel(18));
        }

        [Fact]
        public void GetShadowThresholdLevel_25_Returns3()
        {
            var resolver = CreateResolver();
            Assert.Equal(3, resolver.GetShadowThresholdLevel(25));
        }

        // =====================================================================
        // §15 — Momentum Bonuses
        // =====================================================================

        [Fact]
        public void GetMomentumBonus_0_ReturnsNull()
        {
            var resolver = CreateResolver();
            Assert.Null(resolver.GetMomentumBonus(0));
        }

        [Fact]
        public void GetMomentumBonus_1_ReturnsNull()
        {
            var resolver = CreateResolver();
            Assert.Null(resolver.GetMomentumBonus(1));
        }

        [Fact]
        public void GetMomentumBonus_2_ReturnsNull()
        {
            var resolver = CreateResolver();
            // 2-wins has "effect: none" and no roll_bonus
            var result = resolver.GetMomentumBonus(2);
            // The rule matches but has no roll_bonus key → null
            Assert.Null(result);
        }

        [Fact]
        public void GetMomentumBonus_3_Returns2()
        {
            var resolver = CreateResolver();
            Assert.Equal(2, resolver.GetMomentumBonus(3));
        }

        [Fact]
        public void GetMomentumBonus_4_Returns2()
        {
            var resolver = CreateResolver();
            Assert.Equal(2, resolver.GetMomentumBonus(4));
        }

        [Fact]
        public void GetMomentumBonus_5_Returns3()
        {
            var resolver = CreateResolver();
            Assert.Equal(3, resolver.GetMomentumBonus(5));
        }

        [Fact]
        public void GetMomentumBonus_10_Returns3()
        {
            var resolver = CreateResolver();
            Assert.Equal(3, resolver.GetMomentumBonus(10));
        }

        // =====================================================================
        // §15 — Risk Tier XP Multipliers
        // =====================================================================

        [Fact]
        public void GetRiskTierXpMultiplier_Safe_Returns1()
        {
            var resolver = CreateResolver();
            Assert.Equal(1.0, resolver.GetRiskTierXpMultiplier(RiskTier.Safe));
        }

        [Fact]
        public void GetRiskTierXpMultiplier_Medium_Returns1_5()
        {
            var resolver = CreateResolver();
            Assert.Equal(1.5, resolver.GetRiskTierXpMultiplier(RiskTier.Medium));
        }

        [Fact]
        public void GetRiskTierXpMultiplier_Hard_Returns2()
        {
            var resolver = CreateResolver();
            Assert.Equal(2.0, resolver.GetRiskTierXpMultiplier(RiskTier.Hard));
        }

        [Fact]
        public void GetRiskTierXpMultiplier_Bold_Returns3()
        {
            var resolver = CreateResolver();
            Assert.Equal(3.0, resolver.GetRiskTierXpMultiplier(RiskTier.Bold));
        }

        [Fact]
        public void GetSuccessDcLabelThresholds_ReturnsConfiguredCutoffs()
        {
            var resolver = CreateResolver();

            var thresholds = resolver.GetSuccessDcLabelThresholds();

            Assert.NotNull(thresholds);
            Assert.Equal(16, thresholds!.Value.LowMax);
            Assert.Equal(20, thresholds.Value.MidMax);
        }

        [Theory]
        [InlineData(13, 5)]
        [InlineData(16, 5)]
        [InlineData(17, 10)]
        [InlineData(20, 10)]
        [InlineData(21, 15)]
        public void GetSuccessBaseXp_UsesConfiguredUpperBoundRanges(int dc, int expectedXp)
        {
            var resolver = CreateResolver();

            Assert.Equal(expectedXp, resolver.GetSuccessBaseXp(dc));
        }

        [Theory]
        [InlineData("intermediate_min", 4)]
        [InlineData("advanced_min", 7)]
        [InlineData("legendary_min", 10)]
        public void GetFailurePoolTierMinLevel_ReturnsConfiguredLevel(string tierName, int expectedLevel)
        {
            var resolver = CreateResolver();

            Assert.Equal(expectedLevel, resolver.GetFailurePoolTierMinLevel(tierName));
        }

        [Theory]
        [InlineData(3, FailurePoolTier.Basic)]
        [InlineData(4, FailurePoolTier.Intermediate)]
        [InlineData(7, FailurePoolTier.Advanced)]
        [InlineData(10, FailurePoolTier.Legendary)]
        public void LevelTable_UsesPopulatedRuleBookFailurePoolTiers(int level, FailurePoolTier expectedTier)
        {
            var resolver = CreateResolver();

            Assert.Equal(expectedTier, LevelTable.GetFailurePoolTier(level, resolver));
        }

        // =====================================================================
        // Fallback: empty RuleBook returns null
        // =====================================================================

        [Fact]
        public void EmptyRuleBook_AllMethodsReturnNull()
        {
            var emptyYaml = "- id: empty\n  section: test\n  title: test\n  type: test\n  description: test\n";
            var resolver = RuleBookResolver.FromYaml(emptyYaml);

            Assert.False(resolver.AllowDefaultFallback);
            Assert.Null(resolver.GetFailureInterestDelta(5, 10));
            Assert.Null(resolver.GetSuccessInterestDelta(5, 15));
            Assert.Null(resolver.GetInterestState(10));
            Assert.Null(resolver.GetShadowThresholdLevel(12));
            Assert.Null(resolver.GetMomentumBonus(3));
            Assert.Null(resolver.GetRiskTierXpMultiplier(RiskTier.Hard));
            Assert.Null(resolver.GetSuccessDcLabelThresholds());
            Assert.Null(resolver.GetSuccessBaseXp(13));
            Assert.Null(resolver.GetFailurePoolTierMinLevel("intermediate_min"));
        }

        [Fact]
        public void EmptyRuleBook_LevelTableThrowsInsteadOfUsingDefaultThresholds()
        {
            var emptyYaml = "- id: empty\n  section: test\n  title: test\n  type: test\n  description: test\n";
            var resolver = RuleBookResolver.FromYaml(emptyYaml);

            var ex = Assert.Throws<System.InvalidOperationException>(() => LevelTable.GetLevel(50, resolver));

            Assert.Contains("progression XP threshold", ex.Message, System.StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // Equivalence with hardcoded C#
        // =====================================================================

        [Theory]
        [InlineData(1, -1)]   // Fumble
        [InlineData(2, -1)]   // Fumble
        [InlineData(3, -1)]   // Misfire
        [InlineData(5, -1)]   // Misfire
        [InlineData(6, -2)]   // TropeTrap
        [InlineData(9, -2)]   // TropeTrap
        [InlineData(10, -3)]  // Catastrophe
        [InlineData(15, -3)]  // Catastrophe
        public void FailureDeltas_MatchHardcoded(int missMargin, int expectedDelta)
        {
            var resolver = CreateResolver();
            Assert.Equal(expectedDelta, resolver.GetFailureInterestDelta(missMargin, 10));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(9, 2)]
        [InlineData(10, 3)]
        [InlineData(15, 3)]
        public void SuccessDeltas_MatchHardcoded(int beatMargin, int expectedDelta)
        {
            var resolver = CreateResolver();
            Assert.Equal(expectedDelta, resolver.GetSuccessInterestDelta(beatMargin, 15));
        }

        [Fact]
        public void FromYaml_InvalidYaml_ThrowsFormatException()
        {
            Assert.Throws<System.FormatException>(() => RuleBookResolver.FromYaml("not: valid: yaml: ["));
        }
    }
}

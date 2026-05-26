using Xunit;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;
using Pinder.Core.Progression;

namespace Pinder.Core.Tests.RulesSpec
{
    public partial class RulesSpecValidationTests
    {
        // =====================================================================
        // §7 — Shadow Threshold: boundary edge cases
        // =====================================================================

        // Mutation: would catch if shadow=0 returned tier 1 instead of 0
        [Fact]
        public void Edge_S7_Shadow0_Tier0()
        {
            Assert.Equal(0, ShadowThresholdEvaluator.GetThresholdLevel(0));
        }

        // Mutation: would catch if shadow=5 was tier 1 instead of 0
        [Fact]
        public void Edge_S7_Shadow5_Tier0_UpperBound()
        {
            Assert.Equal(0, ShadowThresholdEvaluator.GetThresholdLevel(5));
        }

        // Mutation: would catch if shadow=6 was tier 0 instead of 1
        [Fact]
        public void Edge_S7_Shadow6_Tier1_LowerBound()
        {
            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(6));
        }

        // Mutation: would catch if shadow=11 was tier 2 instead of 1
        [Fact]
        public void Edge_S7_Shadow11_Tier1_UpperBound()
        {
            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(11));
        }

        // Mutation: would catch if shadow=12 was tier 1 instead of 2
        [Fact]
        public void Edge_S7_Shadow12_Tier2_LowerBound()
        {
            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(12));
        }

        // Mutation: would catch if shadow=17 was tier 3 instead of 2
        [Fact]
        public void Edge_S7_Shadow17_Tier2_UpperBound()
        {
            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(17));
        }

        // Mutation: would catch if shadow=18 was tier 2 instead of 3
        [Fact]
        public void Edge_S7_Shadow18_Tier3_LowerBound()
        {
            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(18));
        }

        // Mutation: would catch if very high shadow (100) was not tier 3
        [Fact]
        public void Edge_S7_Shadow100_StillTier3()
        {
            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(100));
        }

        // =====================================================================
        // §10 — Progression: edge cases
        // =====================================================================

        // Mutation: would catch if XP just below level 2 threshold returned 2
        [Fact]
        public void Edge_S10_XP49_StillLevel1()
        {
            Assert.Equal(1, LevelTable.GetLevel(49));
        }

        // Mutation: would catch if XP at exact level 2 threshold returned 1
        [Fact]
        public void Edge_S10_XP50_ExactLevel2()
        {
            Assert.Equal(2, LevelTable.GetLevel(50));
        }

        // Mutation: would catch if XP just below level 3 threshold returned 3
        [Fact]
        public void Edge_S10_XP149_StillLevel2()
        {
            Assert.Equal(2, LevelTable.GetLevel(149));
        }

        // Mutation: would catch if very high XP didn't map to level 11
        [Fact]
        public void Edge_S10_XP9999_Level11()
        {
            Assert.Equal(11, LevelTable.GetLevel(9999));
        }

        // =====================================================================
        // §5 — ExternalBonus = 0 default verification
        // =====================================================================

        // Mutation: would catch if externalBonus != 0 changed success determination unexpectedly
        [Fact]
        public void Edge_S5_ExternalBonus_Zero_Default_NoEffect()
        {
            // With externalBonus=0, FinalTotal should equal Total
            var result = new RollResult(
                dieRoll: 10, secondDieRoll: null, usedDieRoll: 10,
                stat: StatType.Charm, statModifier: 2, levelBonus: 0,
                dc: 13, tier: FailureTier.Fumble, activatedTrap: null, externalBonus: 0
            );
            // Total = 10 + 2 = 12, FinalTotal = 12 + 0 = 12
            // With 0 external bonus, FinalTotal should be same as base total
            Assert.Equal(12, result.FinalTotal);
        }

        // =====================================================================
        // §5 — RiskTierBonus: failure always returns 0
        // =====================================================================

        // Mutation: would catch if Bold failure returned 2 instead of 0
        [Fact]
        public void Edge_S5_RiskBonus_BoldFailure_Zero()
        {
            var result = MakeRisk(18, false);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(result));
        }

        // Safe success now returns +1
        [Fact]
        public void Edge_S5_RiskBonus_SafeSuccess_Zero()
        {
            var result = MakeRisk(3, true);
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(result)); // Safe now returns +1
        }
    }
}

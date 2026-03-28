using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public class RiskTierTests
    {
        // Helper: build a RollResult with specific modifiers and DC to control "need"
        private static RollResult MakeResult(int dc, int statMod, int levelBonus, int dieRoll)
        {
            return new RollResult(
                dieRoll: dieRoll,
                secondDieRoll: null,
                usedDieRoll: dieRoll,
                stat: StatType.Charm,
                statModifier: statMod,
                levelBonus: levelBonus,
                dc: dc,
                tier: FailureTier.Fumble); // tier doesn't matter for risk tier computation
        }

        // ============================================================
        // RiskTier computation on RollResult
        // ============================================================

        [Theory]
        [InlineData(10, 6, 2)]    // need = 10-(6+2) = 2
        [InlineData(10, 5, 0)]    // need = 5
        [InlineData(10, 10, 0)]   // need = 0
        [InlineData(5, 10, 0)]    // need = -5 (negative)
        public void RollResult_RiskTier_Safe(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
        }

        [Theory]
        [InlineData(10, 4, 0)]  // need = 6
        [InlineData(14, 4, 0)]  // need = 10
        [InlineData(12, 4, 1)]  // need = 7
        public void RollResult_RiskTier_Medium(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        [Theory]
        [InlineData(14, 3, 0)]    // need = 11
        [InlineData(18, 3, 0)]    // need = 15
        [InlineData(16, 2, 1)]    // need = 13
        public void RollResult_RiskTier_Hard(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
        }

        [Theory]
        [InlineData(16, 0, 0)]    // need = 16
        [InlineData(20, 0, 0)]    // need = 20
        [InlineData(25, 0, 0)]    // need = 25 (impossible without nat-20)
        public void RollResult_RiskTier_Bold(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
        }

        // Boundary values
        [Fact]
        public void RollResult_RiskTier_BoundaryNeed5_IsSafe()
        {
            var r = MakeResult(5, 0, 0, 10); // need = 5
            Assert.Equal(RiskTier.Safe, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed6_IsMedium()
        {
            var r = MakeResult(6, 0, 0, 10); // need = 6
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed10_IsMedium()
        {
            var r = MakeResult(10, 0, 0, 10); // need = 10
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed11_IsHard()
        {
            var r = MakeResult(11, 0, 0, 10); // need = 11
            Assert.Equal(RiskTier.Hard, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed15_IsHard()
        {
            var r = MakeResult(15, 0, 0, 10); // need = 15
            Assert.Equal(RiskTier.Hard, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed16_IsBold()
        {
            var r = MakeResult(16, 0, 0, 10); // need = 16
            Assert.Equal(RiskTier.Bold, r.RiskTier);
        }

        // ============================================================
        // RiskTierBonus
        // ============================================================

        [Fact]
        public void RiskTierBonus_SafeSuccess_ReturnsZero()
        {
            // need = 2, roll 20 → success
            var r = MakeResult(5, 3, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_MediumSuccess_ReturnsZero()
        {
            // need = 8, roll 20 → success
            var r = MakeResult(10, 2, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Medium, r.RiskTier);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_HardSuccess_ReturnsOne()
        {
            // need = 15, roll 20 → success (nat-20)
            var r = MakeResult(18, 3, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_BoldSuccess_ReturnsTwo()
        {
            // need = 20, roll 20 → success (nat-20)
            var r = MakeResult(20, 0, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
            Assert.Equal(2, RiskTierBonus.GetInterestBonus(r));
        }

        // Failures → always 0 regardless of tier
        [Theory]
        [InlineData(5, 3, 0, 1)]   // Safe, nat-1 = failure
        [InlineData(10, 2, 0, 1)]  // Medium, nat-1 = failure
        [InlineData(18, 3, 0, 1)]  // Hard, nat-1 = failure
        [InlineData(20, 0, 0, 1)]  // Bold, nat-1 = failure
        public void RiskTierBonus_Failure_ReturnsZero(int dc, int statMod, int levelBonus, int dieRoll)
        {
            var r = MakeResult(dc, statMod, levelBonus, dieRoll);
            Assert.False(r.IsSuccess);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_NullResult_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => RiskTierBonus.GetInterestBonus(null!));
        }

        // ============================================================
        // Integration example from spec
        // ============================================================

        [Fact]
        public void Spec_Example1_HardSuccess()
        {
            // Stat +2, level +1, DC 18 → need=15 → Hard
            // Roll 16 → total=19 → success, margin=1 → SuccessScale +1
            // RiskTierBonus: +1. Total = +2
            var r = new RollResult(16, null, 16, StatType.Charm, 2, 1, 18, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
            Assert.Equal(1, SuccessScale.GetInterestDelta(r));
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void Spec_Example2_BoldNat20()
        {
            // Stat +0, level +0, DC 20 → need=20 → Bold
            // Roll 20 → nat-20 → success → SuccessScale +4
            // RiskTierBonus: +2. Total = +6
            var r = new RollResult(20, null, 20, StatType.Charm, 0, 0, 20, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
            Assert.Equal(4, SuccessScale.GetInterestDelta(r));
            Assert.Equal(2, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void Spec_Example3_SafeSuccess_NoBonus()
        {
            // Stat +4, level +2, DC 10 → need=4 → Safe
            // Roll 8 → total=14 → success, margin=4 → SuccessScale +1
            var r = new RollResult(8, null, 8, StatType.Charm, 4, 2, 10, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
            Assert.Equal(1, SuccessScale.GetInterestDelta(r));
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }
    }
}

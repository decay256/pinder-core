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
        [InlineData(12, 4, 0)]  // need = 8
        [InlineData(14, 4, 0)]  // need = 10
        [InlineData(15, 4, 0)]  // need = 11 (≤11 = Medium)
        public void RollResult_RiskTier_Medium(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        [Theory]
        [InlineData(14, 3, 0)]    // need = 11 (≤11 = Medium; was Hard with old ≤10 boundary)
        [InlineData(18, 3, 0)]    // need = 15
        [InlineData(16, 2, 1)]    // need = 13
        public void RollResult_RiskTier_Hard(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            // need=11 is Medium (≤11); need=15 and need=13 are Hard (12-15)
            int need = dc - (statMod + levelBonus);
            var expectedTier = need <= 11 ? RiskTier.Medium : RiskTier.Hard;
            Assert.Equal(expectedTier, r.RiskTier);
        }

        [Theory]
        [InlineData(16, 0, 0)]    // need = 16
        [InlineData(20, 0, 0)]    // need = 20 (≥20 = Reckless with new boundaries)
        [InlineData(25, 0, 0)]    // need = 25 (≥20 = Reckless)
        public void RollResult_RiskTier_Bold(int dc, int statMod, int levelBonus)
        {
            var r = MakeResult(dc, statMod, levelBonus, 15);
            // need=16..19 = Bold; need≥20 = Reckless
            int need = dc - (statMod + levelBonus);
            var expectedTier = need <= 19 ? RiskTier.Bold : RiskTier.Reckless;
            Assert.Equal(expectedTier, r.RiskTier);
        }

        // Boundary values
        [Fact]
        public void RollResult_RiskTier_BoundaryNeed5_IsSafe()
        {
            var r = MakeResult(5, 0, 0, 10); // need = 5
            Assert.Equal(RiskTier.Safe, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed8_IsMedium()
        {
            var r = MakeResult(8, 0, 0, 10); // need = 8
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed11_IsMedium()
        {
            var r = MakeResult(11, 0, 0, 10); // need = 11
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        [Fact]
        public void RollResult_RiskTier_BoundaryNeed12_IsHard()
        {
            var r = MakeResult(12, 0, 0, 10); // need = 12
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
        public void RiskTierBonus_SafeSuccess_ReturnsOne()
        {
            // need = 5 (Safe), roll 20 → success
            var r = MakeResult(5, 0, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_MediumSuccess_ReturnsTwo()
        {
            // need = 9 (Medium), roll 20 → success
            var r = MakeResult(9, 0, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Medium, r.RiskTier);
            Assert.Equal(2, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_HardSuccess_ReturnsThree()
        {
            // need = 13 (Hard), roll 20 → success (nat-20)
            var r = MakeResult(13, 0, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
            Assert.Equal(3, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_BoldSuccess_ReturnsFive()
        {
            // need = 17 (Bold), roll 20 → success (nat-20)
            var r = MakeResult(17, 0, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
            Assert.Equal(5, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void RiskTierBonus_RecklessSuccess_ReturnsTen()
        {
            // need = 21 (Reckless), roll 20 → success (nat-20)
            var r = MakeResult(21, 0, 0, 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Reckless, r.RiskTier);
            Assert.Equal(10, RiskTierBonus.GetInterestBonus(r));
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
            // RiskTierBonus: Hard=+3. Total = +4
            var r = new RollResult(16, null, 16, StatType.Charm, 2, 1, 18, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
            Assert.Equal(1, SuccessScale.GetInterestDelta(r));
            Assert.Equal(3, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void Spec_Example2_BoldNat20()
        {
            // Stat +0, level +0, DC 20 → need=20 → Reckless (≥20)
            // Roll 20 → nat-20 → success → SuccessScale +4
            // RiskTierBonus: Reckless=+10. Total = +14
            var r = new RollResult(20, null, 20, StatType.Charm, 0, 0, 20, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Reckless, r.RiskTier);
            Assert.Equal(4, SuccessScale.GetInterestDelta(r));
            Assert.Equal(10, RiskTierBonus.GetInterestBonus(r));
        }

        [Fact]
        public void Spec_Example3_SafeSuccess_NoBonus()
        {
            // Stat +4, level +2, DC 10 → need=4 → Safe (≤7)
            // Roll 8 → total=14 → success, margin=4 → SuccessScale +1
            // RiskTierBonus: Safe=+1
            var r = new RollResult(8, null, 8, StatType.Charm, 4, 2, 10, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
            Assert.Equal(1, SuccessScale.GetInterestDelta(r));
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(r));
        }
    }
}

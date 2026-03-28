using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #42: RollEngine risk tier interest bonus on success.
    /// 
    /// Source: GitHub Issue #42 acceptance criteria + architecture doc §5.
    /// 
    /// Risk tier thresholds (need = dc - (statMod + levelBonus)):
    ///   Need ≤ 5  → Safe    → +0 bonus
    ///   Need 6–10 → Medium  → +0 bonus
    ///   Need 11–15→ Hard    → +1 bonus on success
    ///   Need ≥ 16 → Bold    → +2 bonus on success
    /// 
    /// On failure, bonus is always 0 regardless of tier.
    /// </summary>
    public class RiskTierSpecTests
    {
        // ============================================================
        // Helper: construct a RollResult with controlled parameters
        // ============================================================
        private static RollResult MakeRollResult(int dc, int statMod, int levelBonus, int dieRoll)
        {
            // FailureTier.None for successes; actual tier is determined by the constructor
            // for failures. We use a high enough roll for success or low for failure.
            return new RollResult(
                dieRoll: dieRoll,
                secondDieRoll: null,
                usedDieRoll: dieRoll,
                stat: StatType.Charm,
                statModifier: statMod,
                levelBonus: levelBonus,
                dc: dc,
                tier: FailureTier.None);
        }

        private static RollResult MakeFailedResult(int dc, int statMod, int levelBonus, int dieRoll, FailureTier tier)
        {
            return new RollResult(
                dieRoll: dieRoll,
                secondDieRoll: null,
                usedDieRoll: dieRoll,
                stat: StatType.Charm,
                statModifier: statMod,
                levelBonus: levelBonus,
                dc: dc,
                tier: tier);
        }

        // ============================================================
        // AC: RollResult exposes RiskTier enum (Safe/Medium/Hard/Bold)
        // ============================================================

        // What: RollResult has a RiskTier property that returns the correct enum value
        // Mutation: would catch if RiskTier property is missing or returns wrong type
        [Fact]
        public void AC1_RollResult_ExposeRiskTier_PropertyExists()
        {
            var r = MakeRollResult(dc: 15, statMod: 2, levelBonus: 0, dieRoll: 18);
            RiskTier tier = r.RiskTier; // must compile and return a RiskTier
            Assert.IsType<RiskTier>(tier);
        }

        // What: RiskTier enum has all four values per spec
        // Mutation: would catch if any enum member is missing
        [Fact]
        public void AC1_RiskTier_EnumHasFourValues()
        {
            var values = Enum.GetValues(typeof(RiskTier));
            Assert.Contains(RiskTier.Safe, (RiskTier[])values);
            Assert.Contains(RiskTier.Medium, (RiskTier[])values);
            Assert.Contains(RiskTier.Hard, (RiskTier[])values);
            Assert.Contains(RiskTier.Bold, (RiskTier[])values);
        }

        // ============================================================
        // AC: Risk tier computation: need = dc - (statMod + levelBonus)
        // ============================================================

        // What: Need ≤ 5 → Safe
        // Mutation: would catch if threshold is != 5 or formula is wrong
        [Theory]
        [InlineData(10, 5, 0, 15)]   // need = 10-(5+0) = 5 → Safe (boundary)
        [InlineData(10, 6, 0, 15)]   // need = 4 → Safe
        [InlineData(10, 3, 3, 15)]   // need = 4 → Safe
        [InlineData(5, 5, 5, 15)]    // need = -5 → Safe (negative)
        [InlineData(8, 2, 1, 15)]    // need = 5 → Safe (exact boundary)
        public void AC1_Need_LessOrEqual5_IsSafe(int dc, int statMod, int levelBonus, int dieRoll)
        {
            var r = MakeRollResult(dc, statMod, levelBonus, dieRoll);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
        }

        // What: Need 6–10 → Medium
        // Mutation: would catch if lower or upper boundary is wrong
        [Theory]
        [InlineData(10, 4, 0, 15)]   // need = 6 → Medium (lower boundary)
        [InlineData(14, 4, 0, 15)]   // need = 10 → Medium (upper boundary)
        [InlineData(12, 3, 1, 15)]   // need = 8 → Medium (middle)
        public void AC1_Need_6To10_IsMedium(int dc, int statMod, int levelBonus, int dieRoll)
        {
            var r = MakeRollResult(dc, statMod, levelBonus, dieRoll);
            Assert.Equal(RiskTier.Medium, r.RiskTier);
        }

        // What: Need 11–15 → Hard
        // Mutation: would catch if Hard boundaries are wrong
        [Theory]
        [InlineData(14, 3, 0, 20)]   // need = 11 → Hard (lower boundary)
        [InlineData(18, 3, 0, 20)]   // need = 15 → Hard (upper boundary)
        [InlineData(16, 2, 1, 20)]   // need = 13 → Hard (middle)
        public void AC1_Need_11To15_IsHard(int dc, int statMod, int levelBonus, int dieRoll)
        {
            var r = MakeRollResult(dc, statMod, levelBonus, dieRoll);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
        }

        // What: Need ≥ 16 → Bold
        // Mutation: would catch if Bold lower boundary is wrong
        [Theory]
        [InlineData(16, 0, 0, 20)]   // need = 16 → Bold (boundary)
        [InlineData(20, 0, 0, 20)]   // need = 20 → Bold
        [InlineData(25, 0, 0, 20)]   // need = 25 → Bold (extreme)
        public void AC1_Need_16Plus_IsBold(int dc, int statMod, int levelBonus, int dieRoll)
        {
            var r = MakeRollResult(dc, statMod, levelBonus, dieRoll);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
        }

        // ============================================================
        // AC: Hard success = +1 bonus Interest on top of SuccessScale delta
        // ============================================================

        // What: Hard tier success gives +1 interest bonus
        // Mutation: would catch if Hard bonus is 0 or 2 instead of 1
        [Fact]
        public void AC2_HardSuccess_PlusOneBonus()
        {
            // dc=18, statMod=3, levelBonus=0 → need=15 → Hard
            // dieRoll=20 (nat 20) → success
            var r = MakeRollResult(dc: 18, statMod: 3, levelBonus: 0, dieRoll: 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(r));
        }

        // What: Hard tier at lower boundary (need=11) still gives +1
        // Mutation: would catch off-by-one at Hard lower boundary
        [Fact]
        public void AC2_HardSuccess_LowerBoundary_PlusOneBonus()
        {
            // dc=14, statMod=3, levelBonus=0 → need=11 → Hard
            var r = MakeRollResult(dc: 14, statMod: 3, levelBonus: 0, dieRoll: 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(r));
        }

        // ============================================================
        // AC: Bold success = +2 bonus Interest on top of SuccessScale delta
        // ============================================================

        // What: Bold tier success gives +2 interest bonus
        // Mutation: would catch if Bold bonus is 0 or 1 instead of 2
        [Fact]
        public void AC3_BoldSuccess_PlusTwoBonus()
        {
            // dc=20, statMod=0, levelBonus=0 → need=20 → Bold
            // dieRoll=20 (nat 20) → success
            var r = MakeRollResult(dc: 20, statMod: 0, levelBonus: 0, dieRoll: 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
            Assert.Equal(2, RiskTierBonus.GetInterestBonus(r));
        }

        // What: Bold at exact boundary (need=16) gives +2
        // Mutation: would catch off-by-one at Bold lower boundary
        [Fact]
        public void AC3_BoldSuccess_LowerBoundary_PlusTwoBonus()
        {
            // dc=16, statMod=0, levelBonus=0 → need=16 → Bold
            var r = MakeRollResult(dc: 16, statMod: 0, levelBonus: 0, dieRoll: 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Bold, r.RiskTier);
            Assert.Equal(2, RiskTierBonus.GetInterestBonus(r));
        }

        // ============================================================
        // AC: Safe/Medium success = no bonus
        // ============================================================

        // What: Safe tier success gives 0 bonus
        // Mutation: would catch if Safe incorrectly gives a bonus
        [Fact]
        public void AC4_SafeSuccess_ZeroBonus()
        {
            // dc=5, statMod=3, levelBonus=0 → need=2 → Safe
            var r = MakeRollResult(dc: 5, statMod: 3, levelBonus: 0, dieRoll: 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        // What: Medium tier success gives 0 bonus
        // Mutation: would catch if Medium incorrectly gives a bonus
        [Fact]
        public void AC4_MediumSuccess_ZeroBonus()
        {
            // dc=10, statMod=2, levelBonus=0 → need=8 → Medium
            var r = MakeRollResult(dc: 10, statMod: 2, levelBonus: 0, dieRoll: 20);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Medium, r.RiskTier);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        // ============================================================
        // AC: Tests cover all four tiers (covered above + failures)
        // ============================================================

        // What: All tier failures return 0 bonus (not just successes)
        // Mutation: would catch if failure path incorrectly returns non-zero
        [Theory]
        [InlineData(5, 3, 0, 1, FailureTier.Legendary)]     // Safe, nat 1
        [InlineData(10, 2, 0, 2, FailureTier.Fumble)]        // Medium, fumble
        [InlineData(18, 3, 0, 3, FailureTier.Misfire)]       // Hard, misfire
        [InlineData(20, 0, 0, 1, FailureTier.Legendary)]     // Bold, nat 1
        public void AC5_AllTierFailures_ZeroBonus(int dc, int statMod, int levelBonus, int dieRoll, FailureTier tier)
        {
            var r = MakeFailedResult(dc, statMod, levelBonus, dieRoll, tier);
            Assert.False(r.IsSuccess);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        // ============================================================
        // Boundary tests: exact tier transitions
        // ============================================================

        // What: need=5 is Safe, need=6 is Medium (boundary between Safe/Medium)
        // Mutation: would catch off-by-one at the 5/6 boundary
        [Fact]
        public void Boundary_Need5Safe_Need6Medium()
        {
            var safe = MakeRollResult(dc: 5, statMod: 0, levelBonus: 0, dieRoll: 20);
            var medium = MakeRollResult(dc: 6, statMod: 0, levelBonus: 0, dieRoll: 20);
            Assert.Equal(RiskTier.Safe, safe.RiskTier);
            Assert.Equal(RiskTier.Medium, medium.RiskTier);
        }

        // What: need=10 is Medium, need=11 is Hard (boundary between Medium/Hard)
        // Mutation: would catch off-by-one at the 10/11 boundary
        [Fact]
        public void Boundary_Need10Medium_Need11Hard()
        {
            var medium = MakeRollResult(dc: 10, statMod: 0, levelBonus: 0, dieRoll: 20);
            var hard = MakeRollResult(dc: 11, statMod: 0, levelBonus: 0, dieRoll: 20);
            Assert.Equal(RiskTier.Medium, medium.RiskTier);
            Assert.Equal(RiskTier.Hard, hard.RiskTier);
        }

        // What: need=15 is Hard, need=16 is Bold (boundary between Hard/Bold)
        // Mutation: would catch off-by-one at the 15/16 boundary
        [Fact]
        public void Boundary_Need15Hard_Need16Bold()
        {
            var hard = MakeRollResult(dc: 15, statMod: 0, levelBonus: 0, dieRoll: 20);
            var bold = MakeRollResult(dc: 16, statMod: 0, levelBonus: 0, dieRoll: 20);
            Assert.Equal(RiskTier.Hard, hard.RiskTier);
            Assert.Equal(RiskTier.Bold, bold.RiskTier);
        }

        // ============================================================
        // Edge case: negative need values
        // ============================================================

        // What: When statMod + levelBonus > dc, need is negative → still Safe
        // Mutation: would catch if negative need crashes or returns wrong tier
        [Fact]
        public void EdgeCase_NegativeNeed_IsSafe()
        {
            // dc=5, statMod=10, levelBonus=5 → need = 5-(10+5) = -10
            var r = MakeRollResult(dc: 5, statMod: 10, levelBonus: 5, dieRoll: 20);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(r));
        }

        // ============================================================
        // Edge case: need = 0
        // ============================================================

        // What: need=0 is Safe
        // Mutation: would catch if zero is treated differently from positive ≤5
        [Fact]
        public void EdgeCase_NeedZero_IsSafe()
        {
            // dc=5, statMod=5, levelBonus=0 → need=0
            var r = MakeRollResult(dc: 5, statMod: 5, levelBonus: 0, dieRoll: 20);
            Assert.Equal(RiskTier.Safe, r.RiskTier);
        }

        // ============================================================
        // Null guard
        // ============================================================

        // What: RiskTierBonus.GetInterestBonus throws on null input
        // Mutation: would catch if null guard is missing
        [Fact]
        public void NullGuard_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => RiskTierBonus.GetInterestBonus(null!));
        }

        // ============================================================
        // Integration: combined SuccessScale + RiskTierBonus
        // ============================================================

        // What: Hard success total = SuccessScale delta + 1
        // Mutation: would catch if bonus is not additive with SuccessScale
        [Fact]
        public void Integration_HardSuccess_TotalDelta()
        {
            // Stat +2, level +1, DC 18 → need = 18-(2+1) = 15 → Hard
            // Roll 16 → total = 16+2+1 = 19 → beats DC 18 by 1 → SuccessScale +1
            // RiskTierBonus: +1 → total interest delta = +2
            var r = new RollResult(16, null, 16, StatType.Charm, 2, 1, 18, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Hard, r.RiskTier);

            int successDelta = SuccessScale.GetInterestDelta(r);
            int riskBonus = RiskTierBonus.GetInterestBonus(r);
            Assert.Equal(1, successDelta);
            Assert.Equal(1, riskBonus);
            Assert.Equal(2, successDelta + riskBonus);
        }

        // What: Bold nat-20 total = SuccessScale +4 + RiskTierBonus +2 = +6
        // Mutation: would catch if nat-20 bonus or Bold bonus is wrong
        [Fact]
        public void Integration_BoldNat20_TotalDelta()
        {
            // Stat +0, level +0, DC 20 → need = 20 → Bold
            // Roll 20 → nat 20 → SuccessScale +4, RiskTierBonus +2 → total +6
            var r = new RollResult(20, null, 20, StatType.Charm, 0, 0, 20, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Bold, r.RiskTier);

            int successDelta = SuccessScale.GetInterestDelta(r);
            int riskBonus = RiskTierBonus.GetInterestBonus(r);
            Assert.Equal(4, successDelta);
            Assert.Equal(2, riskBonus);
            Assert.Equal(6, successDelta + riskBonus);
        }

        // What: Safe success, no bonus added
        // Mutation: would catch if safe tier accidentally adds bonus
        [Fact]
        public void Integration_SafeSuccess_NoBonusAdded()
        {
            // Stat +4, level +2, DC 10 → need = 10-(4+2) = 4 → Safe
            // Roll 8 → total = 8+4+2 = 14 → beats DC 10 by 4 → SuccessScale +1
            // RiskTierBonus: 0 → total = +1
            var r = new RollResult(8, null, 8, StatType.Charm, 4, 2, 10, FailureTier.None);
            Assert.True(r.IsSuccess);
            Assert.Equal(RiskTier.Safe, r.RiskTier);

            int successDelta = SuccessScale.GetInterestDelta(r);
            int riskBonus = RiskTierBonus.GetInterestBonus(r);
            Assert.Equal(1, successDelta);
            Assert.Equal(0, riskBonus);
            Assert.Equal(1, successDelta + riskBonus);
        }

        // ============================================================
        // Integration: GameSession wires risk tier bonus into interest delta
        // ============================================================

        // What: GameSession.ResolveTurnAsync adds RiskTierBonus to total interest delta
        // Mutation: would catch if GameSession doesn't call RiskTierBonus at all
        [Fact]
        public async Task Integration_GameSession_HardSuccess_IncludesRiskBonus()
        {
            // Player stats all=2, opponent stats all=2
            // DC = 13 (base) + 2 (opponent defence mod) = 15
            // need = 15 - (2 + 0) = 13 → Hard → +1 risk bonus
            // Roll 15 → total = 15+2+0 = 17 ≥ 15 → success, beats by 2 → SuccessScale +1
            // Total delta = +1 (success) + +1 (risk) = +2
            var dice = new FixedDice(15, 50);  // d20=15, d100=50 (timing)
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var session = new GameSession(player, opponent, new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Hard, result.Roll.RiskTier);
            // Without risk bonus this would be 1. With risk bonus it's 2.
            Assert.Equal(2, result.InterestDelta);
            Assert.Equal(12, result.StateAfter.Interest); // 10 + 2
        }

        // What: GameSession with Safe-tier roll has no risk bonus contribution
        // Mutation: would catch if GameSession applies bonus to Safe tier
        [Fact]
        public async Task Integration_GameSession_SafeSuccess_NoRiskBonus()
        {
            // Player stats all=6, level=9 (levelBonus=+4), opponent stats all=2
            // DC = 13 (base) + 2 (opponent defence mod) = 15
            // need = 15 - (6 + 4) = 5 → Safe
            // Roll d20=15 → total = 15 + 6 + 4 = 25, beats DC 15 by 10 → SuccessScale +3
            // RiskTierBonus = 0 (Safe) → total interest delta = +3
            // Starting interest 10 → 13
            var dice = new FixedDice(15, 50);  // d20=15, d100=50 (timing)
            var player = MakeProfile("Player", allStats: 6, level: 9);
            var opponent = MakeProfile("Opponent");
            var session = new GameSession(player, opponent, new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(RiskTier.Safe, result.Roll.RiskTier);
            // Safe tier: no risk bonus. Delta should be SuccessScale only (+3 for margin 10).
            Assert.Equal(3, result.InterestDelta);
            Assert.Equal(13, result.StateAfter.Interest); // 10 + 3
        }

        // What: GameSession failure path has no risk bonus regardless of tier
        // Mutation: would catch if failure path incorrectly adds risk bonus
        [Fact]
        public async Task Integration_GameSession_Failure_NoRiskBonus()
        {
            // Player stats all=2, level=1, opponent stats all=2
            // DC = 15, need = 13 → Hard tier, but roll fails
            // d20=5 → total = 5+2+0 = 7 < 15 → failure
            // No risk bonus on failure; only failure scale applies
            var dice = new FixedDice(5, 50); // d20=5, d100=50 (timing)
            var player = MakeProfile("Player");
            var opponent = MakeProfile("Opponent");
            var session = new GameSession(player, opponent, new NullLlmAdapter(), dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Failure: no risk bonus should be applied; only failure scale applies
            Assert.True(result.InterestDelta < 0);
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static CharacterProfile MakeProfile(string name, int allStats = 2, int level = 1)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: level);
        }
    }
}

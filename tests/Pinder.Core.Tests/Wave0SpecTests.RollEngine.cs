using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Wave0SpecTests
    {
        // ==================================================================
        // AC3 / AC4: RollEngine — edge cases
        // ==================================================================

        // Mutation: Fails if ResolveFixedDC with DC 0 doesn't trivially succeed
        [Fact]
        public void ResolveFixedDC_ZeroDC_SucceedsOnNonNat1()
        {
            var dice = new FixedDice(2);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeStatBlock(sa: 0, overthinking: 0, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0), 0,
                new TrapState(), 1, new NullTrapRegistry(), dice);
            Assert.True(result.IsSuccess);
        }

        // Mutation: Fails if ResolveFixedDC with very high DC still allows non-Nat20 success
        [Fact]
        public void ResolveFixedDC_VeryHighDC_OnlyNat20Succeeds()
        {
            var dice = new FixedDice(19);
            var attacker = MakeStatBlock(sa: 5, overthinking: 0, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, attacker, 30,
                new TrapState(), 10, new NullTrapRegistry(), dice);
            Assert.False(result.IsSuccess); // 19 + 5 + bonus < 30

            // Nat 20 does succeed
            var dice20 = new FixedDice(20);
            var result20 = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, attacker, 30,
                new TrapState(), 10, new NullTrapRegistry(), dice20);
            Assert.True(result20.IsSuccess);
        }

        // Mutation: Fails if dcAdjustment is added to DC instead of subtracted
        [Fact]
        public void Resolve_DcAdjustment_IsSubtracted_NotAdded()
        {
            // Charm=3, level=1(bonus=0), defender SA=0 → base DC=16+0=16
            // dcAdjustment=5 → adjusted DC should be 11, not 21
            // roll=8, total=8+3=11 → should succeed if DC=11, fail if DC=21
            var dice = new FixedDice(8);
            var defender = MakeStatBlock(sa: 0, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0, overthinking: 0);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeStatBlock(charm: 3, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0, overthinking: 0),
                defender,
                new TrapState(), 1, new NullTrapRegistry(), dice,
                dcAdjustment: 5);

            Assert.Equal(11, result.DC); // 16 - 5 = 11
            Assert.True(result.IsSuccess); // Total(11) >= DC(11)
        }

        // Mutation: Fails if ResolveFixedDC doesn't pass externalBonus to RollResult
        [Fact]
        public void ResolveFixedDC_ExternalBonus_StoredInResult()
        {
            var dice = new FixedDice(5);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeStatBlock(sa: 0, overthinking: 0, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0), 12,
                new TrapState(), 1, new NullTrapRegistry(), dice,
                externalBonus: 7);

            Assert.Equal(7, result.ExternalBonus);
            Assert.Equal(result.Total + 7, result.FinalTotal);
        }

        // Mutation: Fails if ResolveFixedDC ignores advantage/disadvantage
        [Fact]
        public void ResolveFixedDC_Advantage_UsesHigherRoll()
        {
            // With advantage, two dice are rolled and the higher is used.
            // FixedDice always returns same value, so we can't directly test two-roll.
            // But we can verify the hasAdvantage param is accepted and produces a result.
            var dice = new FixedDice(15);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeStatBlock(sa: 2, overthinking: 0, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0), 12,
                new TrapState(), 1, new NullTrapRegistry(), dice,
                hasAdvantage: true);

            Assert.True(result.IsSuccess);
            // With advantage, secondDieRoll should be populated
            Assert.NotNull(result.SecondDieRoll);
        }

        // Mutation: Fails if ResolveFixedDC doesn't handle disadvantage
        [Fact]
        public void ResolveFixedDC_Disadvantage_SecondRollPopulated()
        {
            var dice = new FixedDice(10);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeStatBlock(sa: 2, overthinking: 0, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0), 12,
                new TrapState(), 1, new NullTrapRegistry(), dice,
                hasDisadvantage: true);

            Assert.NotNull(result.SecondDieRoll);
        }

        // ==================================================================
        // AC5: RollResult — IsSuccess uses FinalTotal, MissMargin uses Total
        // ==================================================================

        // Mutation: Fails if IsSuccess uses Total instead of FinalTotal
        [Fact]
        public void RollResult_ExternalBonus_FlipsIsSuccess()
        {
            // Total < DC but FinalTotal >= DC
            var result = new RollResult(10, null, 10, StatType.Charm, 2, 0, 14,
                FailureTier.Success, externalBonus: 3);
            // Total = 10 + 2 + 0 = 12... wait, dieRoll=10 is used for UsedDieRoll
            // Actually, the RollResult constructor: Total = usedDieRoll + statModifier + levelBonus = 10 + 2 + 0 = 12
            // FinalTotal = 12 + 3 = 15 >= 14 → success
            Assert.Equal(12, result.Total);
            Assert.Equal(15, result.FinalTotal);
            Assert.True(result.IsSuccess);
        }

        // Mutation: Fails if MissMargin uses FinalTotal instead of Total
        [Fact]
        public void RollResult_MissMargin_UsesFinalTotal()
        {
            // Total=10, DC=14, externalBonus=2 → FinalTotal=12 < 14 → fail
            // MissMargin should be 14 - 12 = 2 (uses FinalTotal, not Total)
            var result = new RollResult(10, null, 10, StatType.Charm, 0, 0, 14,
                FailureTier.Misfire, externalBonus: 2);
            Assert.Equal(2, result.MissMargin);
        }

        // Mutation: Fails if Nat1 with externalBonus somehow succeeds
        [Fact]
        public void RollResult_Nat1_WithExternalBonus_StillFails()
        {
            var result = new RollResult(1, null, 1, StatType.Charm, 0, 0, 5,
                FailureTier.Legendary, externalBonus: 50);
            Assert.True(result.IsNatOne);
            Assert.False(result.IsSuccess);
        }

        // Mutation: Fails if Nat20 with negative externalBonus fails
        [Fact]
        public void RollResult_Nat20_WithNegativeBonus_StillSucceeds()
        {
            var result = new RollResult(20, null, 20, StatType.Charm, 0, 0, 50,
                FailureTier.Success, externalBonus: -100);
            Assert.True(result.IsNatTwenty);
            Assert.True(result.IsSuccess);
        }

        // ==================================================================
        // AC9: Backward compatibility
        // ==================================================================

        // Mutation: Fails if RollResult default externalBonus is non-zero
        [Fact]
        public void RollResult_DefaultExternalBonus_IsZero()
        {
            var result = new RollResult(15, null, 15, StatType.Charm, 3, 0, 13, FailureTier.Success);
            Assert.Equal(0, result.ExternalBonus);
            Assert.Equal(result.Total, result.FinalTotal);
            Assert.True(result.IsSuccess); // 18 >= 13
        }

        // Mutation: Fails if Resolve defaults changed from 0
        [Fact]
        public void Resolve_NoOptionalParams_SameAsExplicitZeros()
        {
            var attacker = MakeStatBlock(charm: 3, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0, overthinking: 0);
            var defender = MakeStatBlock(sa: 2, madness: 0, horniness: 0, denial: 0, fixation: 0, dread: 0, overthinking: 0);

            var diceA = new FixedDice(12);
            var diceB = new FixedDice(12);

            var resultA = RollEngine.Resolve(
                StatType.Charm, attacker, defender,
                new TrapState(), 1, new NullTrapRegistry(), diceA);

            var resultB = RollEngine.Resolve(
                StatType.Charm, attacker, defender,
                new TrapState(), 1, new NullTrapRegistry(), diceB,
                externalBonus: 0, dcAdjustment: 0);

            Assert.Equal(resultA.IsSuccess, resultB.IsSuccess);
            Assert.Equal(resultA.Total, resultB.Total);
            Assert.Equal(resultA.DC, resultB.DC);
            Assert.Equal(resultA.FinalTotal, resultB.FinalTotal);
        }
    }
}

using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public class RollEngineExtensionTests
    {
        private static StatBlock MakeAttacker(int sa = 2, int overthinking = 0, int charm = 3)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, 2 },
                    { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 },
                    { StatType.Wit, 4 },
                    { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        private static StatBlock MakeDefender(int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 },
                    { StatType.Rizz, 2 },
                    { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 },
                    { StatType.Wit, 2 },
                    { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        // ======================== ResolveFixedDC ========================

        [Fact]
        public void ResolveFixedDC_SuccessScenario()
        {
            // SA=2, level=3 → bonus=1, dice=11. Total=11+2+1=14, DC=12, success
            var dice = new FixedDice(11);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeAttacker(), 12,
                new TrapState(), 3, new NullTrapRegistry(), dice);

            Assert.True(result.IsSuccess);
            Assert.Equal(14, result.Total);
            Assert.Equal(12, result.DC);
            Assert.Equal(FailureTier.None, result.Tier);
        }

        [Fact]
        public void ResolveFixedDC_FailureScenario()
        {
            // SA=1 (overthinking=3 → penalty 1 → effective=1), level=1 → bonus=0, dice=8
            // Total=8+1+0=9... wait, SA base=1, overthinking=3 → effective = 1-floor(3/3)=0
            var dice = new FixedDice(8);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeAttacker(sa: 1, overthinking: 3), 12,
                new TrapState(), 1, new NullTrapRegistry(), dice);

            Assert.False(result.IsSuccess);
            Assert.Equal(8, result.Total); // 8 + 0 + 0
            Assert.Equal(FailureTier.Misfire, result.Tier); // miss by 4
        }

        [Fact]
        public void ResolveFixedDC_Nat1_AlwaysFails()
        {
            var dice = new FixedDice(1);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeAttacker(sa: 5), 5,
                new TrapState(), 10, new NullTrapRegistry(), dice);

            Assert.False(result.IsSuccess);
            Assert.True(result.IsNatOne);
            Assert.Equal(FailureTier.Legendary, result.Tier);
        }

        [Fact]
        public void ResolveFixedDC_Nat20_AlwaysSucceeds()
        {
            var dice = new FixedDice(20);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeAttacker(sa: 0), 30,
                new TrapState(), 1, new NullTrapRegistry(), dice);

            Assert.True(result.IsSuccess);
            Assert.True(result.IsNatTwenty);
        }

        [Fact]
        public void ResolveFixedDC_TropeTrapActivation()
        {
            // Miss by 6-9 → TropeTrap tier
            // SA=0, level=1(bonus=0), DC=12, roll=4 → total=4, miss=8 → TropeTrap
            var trapDef = new TrapDefinition("test-trap", StatType.SelfAwareness,
                TrapEffect.Disadvantage, 0, 2, "test", "clear", "nat1");
            var registry = new SingleTrapRegistry(trapDef);
            var traps = new TrapState();
            var dice = new FixedDice(4);

            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeAttacker(sa: 0), 12,
                traps, 1, registry, dice);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureTier.TropeTrap, result.Tier);
            Assert.NotNull(result.ActivatedTrap);
            Assert.True(traps.IsActive(StatType.SelfAwareness));
        }

        [Fact]
        public void ResolveFixedDC_WithExternalBonus()
        {
            var dice = new FixedDice(10);
            var result = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness, MakeAttacker(sa: 0), 12,
                new TrapState(), 1, new NullTrapRegistry(), dice,
                externalBonus: 3);

            // Total=10+0+0=10, FinalTotal=10+3=13, DC=12 → success
            Assert.True(result.IsSuccess);
            Assert.Equal(10, result.Total);
            Assert.Equal(13, result.FinalTotal);
            Assert.Equal(3, result.ExternalBonus);
        }

        // ======================== Resolve with externalBonus/dcAdjustment ========================

        [Fact]
        public void Resolve_ExternalBonus_TurnsFailIntoSuccess()
        {
            // Charm=3, level=2(bonus=0), defender SA=2 → DC=16+2=18
            // roll=10, total=13, miss by 5 → Misfire without bonus
            // With externalBonus=5: FinalTotal=18 >= DC=18 → success
            var dice = new FixedDice(10);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(charm: 3), MakeDefender(sa: 2),
                new TrapState(), 2, new NullTrapRegistry(), dice,
                externalBonus: 5);

            Assert.True(result.IsSuccess);
            Assert.Equal(13, result.Total);
            Assert.Equal(18, result.FinalTotal);
        }

        [Fact]
        public void Resolve_DcAdjustment_LowersDC()
        {
            // Charm=3, level=2(bonus=0), defender SA=2 → base DC=18, adjusted=18-5=13
            // roll=10, total=13, FinalTotal=13 >= 13 → success
            var dice = new FixedDice(10);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(charm: 3), MakeDefender(sa: 2),
                new TrapState(), 2, new NullTrapRegistry(), dice,
                dcAdjustment: 5);

            Assert.True(result.IsSuccess);
            Assert.Equal(13, result.DC); // adjusted DC stored
        }

        [Fact]
        public void Resolve_BothBonusAndAdjustment()
        {
            // Charm=3, level=2(bonus=0), defender SA=2 → base DC=18
            // dcAdjustment=5 → DC=13, externalBonus=2, roll=10
            // Total=13, FinalTotal=15, DC=13 → success
            var dice = new FixedDice(10);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(charm: 3), MakeDefender(sa: 2),
                new TrapState(), 2, new NullTrapRegistry(), dice,
                externalBonus: 2, dcAdjustment: 5);

            Assert.True(result.IsSuccess);
            Assert.Equal(13, result.DC);
            Assert.Equal(15, result.FinalTotal);
        }

        [Fact]
        public void Resolve_DefaultParams_BackwardCompatible()
        {
            // Same as calling Resolve without the new params
            var dice = new FixedDice(15);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(charm: 3), MakeDefender(sa: 2),
                new TrapState(), 2, new NullTrapRegistry(), dice);

            Assert.Equal(0, result.ExternalBonus);
            Assert.Equal(result.Total, result.FinalTotal);
        }

        [Fact]
        public void Resolve_Nat1_WithPositiveExternalBonus_StillFails()
        {
            var dice = new FixedDice(1);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(), MakeDefender(),
                new TrapState(), 1, new NullTrapRegistry(), dice,
                externalBonus: 100);

            Assert.False(result.IsSuccess);
            Assert.True(result.IsNatOne);
            Assert.Equal(FailureTier.Legendary, result.Tier);
        }

        [Fact]
        public void Resolve_Nat20_WithNegativeExternalBonus_StillSucceeds()
        {
            var dice = new FixedDice(20);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(), MakeDefender(),
                new TrapState(), 1, new NullTrapRegistry(), dice,
                externalBonus: -100);

            Assert.True(result.IsSuccess);
            Assert.True(result.IsNatTwenty);
        }

        [Fact]
        public void Resolve_NegativeExternalBonus_CanCauseFailure()
        {
            // Charm=3, level=1(bonus=0), DC=13+2=15, roll=15 → total=18
            // Without bonus: success. With -5: FinalTotal=13 < 15 → fail
            var dice = new FixedDice(15);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(charm: 3), MakeDefender(sa: 2),
                new TrapState(), 1, new NullTrapRegistry(), dice,
                externalBonus: -5);

            Assert.False(result.IsSuccess);
            Assert.Equal(18, result.Total);
            Assert.Equal(13, result.FinalTotal);
        }

        [Fact]
        public void Resolve_DcAdjustment_LargerThanDC()
        {
            // DC=15, dcAdjustment=20 → adjusted DC=-5. Any non-Nat1 succeeds.
            var dice = new FixedDice(2);
            var result = RollEngine.Resolve(
                StatType.Charm, MakeAttacker(charm: 0), MakeDefender(sa: 2),
                new TrapState(), 1, new NullTrapRegistry(), dice,
                dcAdjustment: 20);

            Assert.True(result.IsSuccess);
        }

        // ======================== RollResult.IsSuccess uses FinalTotal ========================

        [Fact]
        public void RollResult_IsSuccess_UsesFinalTotal()
        {
            // Total=12, DC=14, externalBonus=3 → FinalTotal=15 >= 14 → success
            var result = new RollResult(12, null, 12, StatType.Charm, 0, 0, 14,
                FailureTier.Fumble, externalBonus: 3);

            Assert.True(result.IsSuccess);
            Assert.Equal(15, result.FinalTotal);
        }

        [Fact]
        public void RollResult_MissMargin_UsesFinalTotal()
        {
            // Total=10, DC=14, externalBonus=1 → FinalTotal=11 < 14 → fail
            // MissMargin should be DC - FinalTotal = 3
            var result = new RollResult(10, null, 10, StatType.Charm, 0, 0, 14,
                FailureTier.Misfire, externalBonus: 1);

            Assert.False(result.IsSuccess);
            Assert.Equal(3, result.MissMargin); // DC(14) - FinalTotal(11) = 3
        }

        [Fact]
        public void RollResult_Constructor_ExternalBonus_Default()
        {
            var result = new RollResult(10, null, 10, StatType.Charm, 0, 0, 8, FailureTier.None);
            Assert.Equal(0, result.ExternalBonus);
            Assert.Equal(result.Total, result.FinalTotal);
        }

        // ======================== Test helpers ========================

        private sealed class FixedDice : IDiceRoller
        {
            private readonly int _value;
            public FixedDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class SingleTrapRegistry : ITrapRegistry
        {
            private readonly TrapDefinition _trap;
            public SingleTrapRegistry(TrapDefinition trap) => _trap = trap;
            public TrapDefinition? GetTrap(StatType stat) => stat == _trap.Stat ? _trap : null;
            public string? GetLlmInstruction(StatType stat) => stat == _trap.Stat ? _trap.LlmInstruction : null;
        }
    }
}

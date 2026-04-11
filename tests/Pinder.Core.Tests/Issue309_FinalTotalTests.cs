using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using System.Collections.Generic;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for Issue #309: SuccessScale, failure tier, beatDcBy, and MissMargin
    /// must all use FinalTotal (including external bonuses), not base Total.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue309_FinalTotalTests
    {
        private class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private static StatBlock MakeStats(int charm = 0)
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm,         charm },
                { StatType.Rizz,          0     },
                { StatType.Honesty,       0     },
                { StatType.Chaos,         0     },
                { StatType.Wit,           0     },
                { StatType.SelfAwareness, 0     },
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness,      0 },
                { ShadowStatType.Despair,    0 },
                { ShadowStatType.Denial,       0 },
                { ShadowStatType.Fixation,     0 },
                { ShadowStatType.Dread,        0 },
                { ShadowStatType.Overthinking, 0 },
            };
            return new StatBlock(baseStats, shadowStats);
        }

        // --- SuccessScale uses FinalTotal ---

        [Fact]
        public void SuccessScale_ExternalBonus_IncreasesMarginTier()
        {
            // Total = 14, DC = 13 → margin 1 → +1 interest (without bonus)
            // FinalTotal = 14 + 5 = 19, DC = 13 → margin 6 → +2 interest (with bonus)
            var result = new RollResult(
                dieRoll: 10, secondDieRoll: null, usedDieRoll: 10,
                stat: StatType.Charm, statModifier: 4, levelBonus: 0,
                dc: 13, tier: FailureTier.None, externalBonus: 5);

            Assert.True(result.IsSuccess);
            Assert.Equal(14, result.Total);
            Assert.Equal(19, result.FinalTotal);

            int delta = SuccessScale.GetInterestDelta(result);
            // margin = 19 - 13 = 6 → +2
            Assert.Equal(2, delta);
        }

        [Fact]
        public void SuccessScale_LargeExternalBonus_ReachesCritTier()
        {
            // Total = 14, DC = 13 → margin 1 → +1 (without bonus)
            // FinalTotal = 14 + 10 = 24 → margin 11 → +3 (crit tier)
            var result = new RollResult(
                dieRoll: 10, secondDieRoll: null, usedDieRoll: 10,
                stat: StatType.Charm, statModifier: 4, levelBonus: 0,
                dc: 13, tier: FailureTier.None, externalBonus: 10);

            Assert.Equal(3, SuccessScale.GetInterestDelta(result));
        }

        [Fact]
        public void SuccessScale_ZeroExternalBonus_SameAsTotal()
        {
            // Backward compat: no external bonus, margin = Total - DC
            var result = new RollResult(
                dieRoll: 15, secondDieRoll: null, usedDieRoll: 15,
                stat: StatType.Charm, statModifier: 3, levelBonus: 0,
                dc: 13, tier: FailureTier.None, externalBonus: 0);

            // Total = 18, FinalTotal = 18, margin = 5 → +2
            Assert.Equal(2, SuccessScale.GetInterestDelta(result));
        }

        // --- Failure tier uses FinalTotal ---

        [Fact]
        public void FailureTier_ExternalBonus_ReducesMissMargin()
        {
            // Without bonus: roll 5 + mod 0 + level 0 = 5 vs DC 16 → miss 11 → Catastrophe (10+)
            // With +3 bonus: finalTotal = 8 vs DC 16 → miss 8 → TropeTrap (6-9)
            var attacker = MakeStats(charm: 0);
            var defender = MakeStats(); // DC = 16 + 0 = 16
            var traps = new TrapState();
            var dice = new FixedDice(5); // roll a 5

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                new EmptyTrapRegistry(), dice,
                externalBonus: 3);

            Assert.False(result.IsSuccess);
            // miss = 16 - (5 + 0 + 0 + 3) = 8 → TropeTrap
            Assert.Equal(FailureTier.TropeTrap, result.Tier);
        }

        [Fact]
        public void FailureTier_ExternalBonus_TurnsMissIntoFumble()
        {
            // roll 8 + mod 2 = 10 vs DC 16 → miss 6 → TropeTrap (without bonus)
            // With +4 bonus: finalTotal = 14, miss = 2 → Fumble (1-2)
            var attacker = MakeStats(charm: 2);
            var defender = MakeStats();
            var traps = new TrapState();
            var dice = new FixedDice(8);

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                new EmptyTrapRegistry(), dice,
                externalBonus: 4);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureTier.Fumble, result.Tier);
        }

        [Fact]
        public void FailureTier_ZeroExternalBonus_SameAsBefore()
        {
            // Backward compat: no bonus, miss = dc - total
            var attacker = MakeStats(charm: 0);
            var defender = MakeStats();
            var traps = new TrapState();
            var dice = new FixedDice(5); // total = 5, miss = 16-5 = 11 → Catastrophe

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                new EmptyTrapRegistry(), dice,
                externalBonus: 0);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureTier.Catastrophe, result.Tier);
        }

        [Fact]
        public void FailureTier_ExternalBonus_CanTurnFailureIntoSuccess()
        {
            // roll 8 + mod 2 = 10 vs DC 16 → fail (without bonus)
            // With +6 bonus: finalTotal = 16 → success
            var attacker = MakeStats(charm: 2);
            var defender = MakeStats();
            var traps = new TrapState();
            var dice = new FixedDice(8);

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                new EmptyTrapRegistry(), dice,
                externalBonus: 6);

            Assert.True(result.IsSuccess);
            Assert.Equal(FailureTier.None, result.Tier);
        }

        // --- MissMargin uses FinalTotal ---

        [Fact]
        public void MissMargin_UseFinalTotal()
        {
            // Total = 5, ExternalBonus = 3, FinalTotal = 8, DC = 13
            // MissMargin should be 13 - 8 = 5 (not 13 - 5 = 8)
            var result = new RollResult(
                dieRoll: 5, secondDieRoll: null, usedDieRoll: 5,
                stat: StatType.Charm, statModifier: 0, levelBonus: 0,
                dc: 13, tier: FailureTier.Misfire, externalBonus: 3);

            Assert.False(result.IsSuccess);
            Assert.Equal(5, result.MissMargin);
        }

        [Fact]
        public void MissMargin_ZeroOnSuccess()
        {
            var result = new RollResult(
                dieRoll: 15, secondDieRoll: null, usedDieRoll: 15,
                stat: StatType.Charm, statModifier: 0, levelBonus: 0,
                dc: 13, tier: FailureTier.None, externalBonus: 0);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.MissMargin);
        }

        // --- Catastrophe boundary with external bonus ---

        [Fact]
        public void FailureTier_ExternalBonus_PreventsLargeMissFromCatastrophe()
        {
            // roll 2 + mod 0 = 2 vs DC 16 → miss 14 → Catastrophe (without bonus)
            // With +7 bonus: finalTotal = 9, miss = 7 → TropeTrap (6-9)
            var attacker = MakeStats(charm: 0);
            var defender = MakeStats();
            var traps = new TrapState();
            var dice = new FixedDice(2);

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                new EmptyTrapRegistry(), dice,
                externalBonus: 7);

            Assert.False(result.IsSuccess);
            Assert.Equal(FailureTier.TropeTrap, result.Tier);
        }
    }
}

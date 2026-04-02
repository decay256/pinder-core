using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using System.Collections.Generic;
using Xunit;

namespace Pinder.Core.Tests
{
    public class RollEngineTests
    {
        // Fixed-value dice roller for deterministic tests
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

        private class SingleTrapRegistry : ITrapRegistry
        {
            private readonly TrapDefinition _trap;
            public SingleTrapRegistry(TrapDefinition trap) => _trap = trap;
            public TrapDefinition? GetTrap(StatType stat) => stat == _trap.Stat ? _trap : null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private static StatBlock MakeStats(int charm = 0, int selfAwareness = 0, int madness = 0)
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm,         charm         },
                { StatType.Rizz,          0             },
                { StatType.Honesty,       0             },
                { StatType.Chaos,         0             },
                { StatType.Wit,           0             },
                { StatType.SelfAwareness, selfAwareness },
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness,       madness },
                { ShadowStatType.Horniness,     0       },
                { ShadowStatType.Denial,        0       },
                { ShadowStatType.Fixation,      0       },
                { ShadowStatType.Dread,         0       },
                { ShadowStatType.Overthinking,  0       },
            };
            return new StatBlock(baseStats, shadowStats);
        }

        [Fact]
        public void Nat20_IsAlwaysSuccess()
        {
            var attacker = MakeStats(charm: -5);  // terrible modifier
            var defender = MakeStats(selfAwareness: 10); // DC 23 (13 + SA mod 10)
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(20));

            Assert.True(result.IsNatTwenty);
            Assert.True(result.IsSuccess);
            Assert.Equal(FailureTier.None, result.Tier);
        }

        [Fact]
        public void Nat1_IsLegendaryFail()
        {
            var attacker = MakeStats(charm: 10); // great modifier
            var defender = MakeStats();           // DC 13 (13 + all stats 0)
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(1));

            Assert.True(result.IsNatOne);
            Assert.False(result.IsSuccess);
            Assert.Equal(FailureTier.Legendary, result.Tier);
        }

        [Fact]
        public void MissByOne_IsFumble()
        {
            // DC = 13 + 0 = 13. Roll 11 + 0 + 0 = 11. Miss by 2 → Fumble
            var attacker = MakeStats();
            var defender = MakeStats();
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(11));

            Assert.Equal(13, result.DC);
            Assert.Equal(2, result.MissMargin);
            Assert.Equal(FailureTier.Fumble, result.Tier);
        }

        [Fact]
        public void MissByFive_IsMisfire()
        {
            var attacker = MakeStats();
            var defender = MakeStats();
            // Roll 8: total 8, DC 13, miss 5 → Misfire
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(8));

            Assert.Equal(FailureTier.Misfire, result.Tier);
        }

        [Fact]
        public void MissBySeven_IsTropeTrap()
        {
            var attacker = MakeStats();
            var defender = MakeStats();
            // Roll 6: total 6, DC 13, miss 7 → TropeTrap
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(6));

            Assert.Equal(FailureTier.TropeTrap, result.Tier);
        }

        [Fact]
        public void MissByTen_IsCatastrophe()
        {
            var attacker = MakeStats();
            var defender = MakeStats();
            // Roll 0 not possible, but with -1 modifier: roll 10 + (-1) = 9 vs DC 13
            // Roll 0 not possible, but with -1 modifier: roll 9 + (-1) + 0 = 8 vs DC 20
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, -1 }, { StatType.Rizz, 0 }, { StatType.Honesty, 0 },
                { StatType.Chaos, 0 }, { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 }
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            };
            var weakAttacker = new StatBlock(baseStats, shadowStats);
            var strongDefender = MakeStats(selfAwareness: 10); // DC 23
            // Roll 9 - 1 + 0 = 8, vs DC 23, miss 15 → Catastrophe
            var result = RollEngine.Resolve(
                StatType.Charm, weakAttacker, strongDefender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(9));

            Assert.Equal(FailureTier.Catastrophe, result.Tier);
        }

        [Fact]
        public void Disadvantage_UsesMinium()
        {
            // Rolls: 15, 4 → min = 4
            var attacker = MakeStats(charm: 3);
            var defender = MakeStats();
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(15, 4),
                hasDisadvantage: true);

            Assert.Equal(4, result.UsedDieRoll);
        }

        [Fact]
        public void Advantage_UsesMaximum()
        {
            var attacker = MakeStats(charm: 0);
            var defender = MakeStats();
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 1,
                new EmptyTrapRegistry(), new FixedDice(4, 15),
                hasAdvantage: true);

            Assert.Equal(15, result.UsedDieRoll);
        }

        [Fact]
        public void ShadowPenalty_ReducesModifier()
        {
            // Madness 9 → penalty 3, Charm base +5 → effective +2
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, 5 }, { StatType.Rizz, 0 }, { StatType.Honesty, 0 },
                { StatType.Chaos, 0 }, { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 }
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 9 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            };
            var attacker = new StatBlock(baseStats, shadowStats);
            Assert.Equal(2, attacker.GetEffective(StatType.Charm));
        }

        [Fact]
        public void LevelBonus_AppliesToRoll()
        {
            // Level 5 → bonus +2. Roll 12 + 0 + 2 = 14 vs DC 13 → success
            var attacker = MakeStats();
            var defender = MakeStats();
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, new TrapState(), 5,
                new EmptyTrapRegistry(), new FixedDice(12));

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.LevelBonus);
        }

        [Fact]
        public void Catastrophe_ActivatesTrap()
        {
            // Charm=-1, defender SA=10 → DC=23. Roll 9: total=9+(-1)+0=8, miss=15 → Catastrophe
            // Trap should be activated on Catastrophe just like TropeTrap
            var trapDef = new TrapDefinition("charm-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 2, "you're trapped", "cleared", "nat1 clear");
            var registry = new SingleTrapRegistry(trapDef);
            var traps = new TrapState();

            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, -1 }, { StatType.Rizz, 0 }, { StatType.Honesty, 0 },
                { StatType.Chaos, 0 }, { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 }
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            };
            var weakAttacker = new StatBlock(baseStats, shadowStats);
            var strongDefender = MakeStats(selfAwareness: 10); // DC 23

            var result = RollEngine.Resolve(
                StatType.Charm, weakAttacker, strongDefender, traps, 1,
                registry, new FixedDice(9));

            Assert.Equal(FailureTier.Catastrophe, result.Tier);
            Assert.NotNull(result.ActivatedTrap);
            Assert.Equal("charm-trap", result.ActivatedTrap!.Id);
            Assert.True(traps.IsActive(StatType.Charm));
        }

        [Fact]
        public void Catastrophe_DoesNotActivateTrap_WhenAlreadyActive()
        {
            // If a trap is already active on the stat, Catastrophe should not replace it
            var trapDef = new TrapDefinition("charm-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 2, "you're trapped", "cleared", "nat1 clear");
            var registry = new SingleTrapRegistry(trapDef);
            var traps = new TrapState();
            // Pre-activate a trap
            traps.Activate(trapDef);

            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, -1 }, { StatType.Rizz, 0 }, { StatType.Honesty, 0 },
                { StatType.Chaos, 0 }, { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 }
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            };
            var weakAttacker = new StatBlock(baseStats, shadowStats);
            var strongDefender = MakeStats(selfAwareness: 10); // DC 23

            var result = RollEngine.Resolve(
                StatType.Charm, weakAttacker, strongDefender, traps, 1,
                registry, new FixedDice(9));

            Assert.Equal(FailureTier.Catastrophe, result.Tier);
            // No new trap activated since one was already active
            Assert.Null(result.ActivatedTrap);
            Assert.True(traps.IsActive(StatType.Charm));
        }

        [Fact]
        public void TropeTrap_StillActivatesTrap()
        {
            // Ensure existing TropeTrap trap activation still works after the Catastrophe fix
            // Charm=0, defender SA=0 → DC=13. Roll 6: total=6, miss=7 → TropeTrap
            var trapDef = new TrapDefinition("charm-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 2, "you're trapped", "cleared", "nat1 clear");
            var registry = new SingleTrapRegistry(trapDef);
            var traps = new TrapState();

            var result = RollEngine.Resolve(
                StatType.Charm, MakeStats(), MakeStats(), traps, 1,
                registry, new FixedDice(6));

            Assert.Equal(FailureTier.TropeTrap, result.Tier);
            Assert.NotNull(result.ActivatedTrap);
            Assert.Equal("charm-trap", result.ActivatedTrap!.Id);
            Assert.True(traps.IsActive(StatType.Charm));
        }
    }
}

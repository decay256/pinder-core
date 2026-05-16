using System.Collections.Generic;
using System.Text.Json;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests.Issue906
{
    /// <summary>
    /// Issue #906: <see cref="RollResult.DefendingStat"/> —
    /// must equal <c>StatBlock.DefenceTable[Stat]</c>; serializes as
    /// <c>defending_stat</c>.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue906_DefendingStatTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private static StatBlock MakeStatBlock(int val = 2)
        {
            var stats = new System.Collections.Generic.Dictionary<StatType, int>
            {
                { StatType.Charm, val }, { StatType.Rizz, val }, { StatType.Honesty, val },
                { StatType.Chaos, val }, { StatType.Wit, val }, { StatType.SelfAwareness, val }
            };
            var shadow = new System.Collections.Generic.Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 }, { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 }, { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            };
            return new StatBlock(stats, shadow);
        }

        private static RollResult DoResolve(StatType attackStat, int dieRoll = 15)
        {
            var attacker = MakeStatBlock(3);
            var defender = MakeStatBlock(2);
            var traps = new TrapState();
            return RollEngine.Resolve(
                stat:            attackStat,
                attacker:        attacker,
                defender:        defender,
                attackerTraps:   traps,
                level:           1,
                trapRegistry:    new EmptyTrapRegistry(),
                dice:            new FixedDice(dieRoll));
        }

        // ── Per-stat mapping (6 stats) ────────────────────────────────────────

        [Theory]
        [InlineData(StatType.Charm,         StatType.SelfAwareness)]
        [InlineData(StatType.Rizz,          StatType.Wit)]
        [InlineData(StatType.Honesty,       StatType.Chaos)]
        [InlineData(StatType.Chaos,         StatType.Charm)]
        [InlineData(StatType.Wit,           StatType.Rizz)]
        [InlineData(StatType.SelfAwareness, StatType.Honesty)]
        public void DefendingStat_MatchesDefenceTable(StatType attackStat, StatType expectedDefending)
        {
            var result = DoResolve(attackStat);
            Assert.Equal(expectedDefending, result.DefendingStat);
        }

        // ── Invariant: DefendingStat always equals DefenceTable[Stat] ─────────

        [Fact]
        public void DefendingStat_AlwaysEqualsDefenceTableLookup_OnResolve()
        {
            foreach (StatType stat in System.Enum.GetValues(typeof(StatType)))
            {
                var result = DoResolve(stat);
                Assert.Equal(StatBlock.DefenceTable[result.Stat], result.DefendingStat);
            }
        }

        // ── Serialization: [JsonPropertyName("defending_stat")] ──────────────

        [Theory]
        [InlineData(StatType.Charm,         "SelfAwareness")]
        [InlineData(StatType.Rizz,          "Wit")]
        [InlineData(StatType.Honesty,       "Chaos")]
        [InlineData(StatType.Chaos,         "Charm")]
        [InlineData(StatType.Wit,           "Rizz")]
        [InlineData(StatType.SelfAwareness, "Honesty")]
        public void Serialization_ContainsDefendingStatKey(StatType attackStat, string expectedValue)
        {
            var result = DoResolve(attackStat);
            string json = JsonSerializer.Serialize(result);
            Assert.Contains($"\"defending_stat\":\"{expectedValue}\"", json);
        }

        // ── ResolveFixedDC also populates DefendingStat ────────────────────────

        [Fact]
        public void ResolveFixedDC_PopulatesDefendingStat()
        {
            var attacker = MakeStatBlock(3);
            var traps = new TrapState();
            var result = RollEngine.ResolveFixedDC(
                stat:          StatType.Rizz,
                attacker:      attacker,
                fixedDc:       14,
                attackerTraps: traps,
                level:         1,
                trapRegistry:  new EmptyTrapRegistry(),
                dice:          new FixedDice(12));

            Assert.Equal(StatType.Wit, result.DefendingStat);  // DefenceTable[Rizz] = Wit
        }
    }
}

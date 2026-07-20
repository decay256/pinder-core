using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #903: <see cref="DateeDefenseSnapshot"/> — one entry per
    /// <see cref="StatType"/>, keyed on attacking stat; each entry carries
    /// <see cref="DateeDefenseEntry.DefendingStat"/>,
    /// <see cref="DateeDefenseEntry.EffectiveModifier"/>, and
    /// <see cref="DateeDefenseEntry.BaseModifier"/>.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue903_DateeDefenseSnapshotTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static CharacterProfile MakeProfile(string name, StatBlock? stats = null)
        {
            stats ??= TestHelpers.MakeStatBlock(2);
            return TestHelpers.MakeCharacterProfile(
                stats: stats,
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static GameSession MakeSession(CharacterProfile? datee = null, int startingInterest = 10)
        {
            datee ??= MakeProfile("Datee");
            return new GameSession(
                MakeProfile("Player"),
                datee,
                new NullLlmAdapter(),
                new FixedDice903(5, 5),
                new NullTrapRegistry903(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: startingInterest));
        }

        // ── AC1: 6 entries, one per StatType ─────────────────────────────────

        [Fact]
        public async Task StartTurnAsync_DefenseSnapshot_HasExactlyOneEntryPerStatType()
        {
            var session = MakeSession();
            var turnStart = await session.StartTurnAsync();

            Assert.NotNull(turnStart.DateeDefenseSnapshot);
            var entries = turnStart.DateeDefenseSnapshot!.ByAttackerStat;
            var allStats = (StatType[])Enum.GetValues(typeof(StatType));

            Assert.Equal(allStats.Length, entries.Count);
            foreach (var stat in allStats)
                Assert.True(entries.ContainsKey(stat), $"Missing entry for attacker stat {stat}");
        }

        // ── AC2: DefenceTable mapping ─────────────────────────────────────────

        [Fact]
        public async Task StartTurnAsync_DefenseSnapshot_DefendingStatMatchesDefenceTable()
        {
            var session = MakeSession();
            var turnStart = await session.StartTurnAsync();
            var entries = turnStart.DateeDefenseSnapshot!.ByAttackerStat;

            foreach (var (attackerStat, entry) in entries)
            {
                var expectedDefender = StatBlock.DefenceTable[attackerStat];
                Assert.Equal(expectedDefender, entry.DefendingStat);
            }
        }

        // ── AC3: shadow-reflected — EffectiveModifier reflects shadow penalty ─

        [Fact]
        public async Task StartTurnAsync_DefenseSnapshot_ShadowReflectedInEffectiveModifier()
        {
            // Charm attacks → SelfAwareness defends.
            // SelfAwareness's shadow pair = Overthinking.
            // Give datee SelfAwareness=3, Overthinking=6 → penalty = 6/3 = 2 → Effective=1.
            var dateeStats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm,         2 },
                    { StatType.Rizz,          2 },
                    { StatType.Honesty,       2 },
                    { StatType.Chaos,         2 },
                    { StatType.Wit,           2 },
                    { StatType.SelfAwareness, 3 }, // defender for Charm attacks
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness,      0 },
                    { ShadowStatType.Despair,      0 },
                    { ShadowStatType.Denial,       0 },
                    { ShadowStatType.Fixation,     0 },
                    { ShadowStatType.Dread,        0 },
                    { ShadowStatType.Overthinking, 6 }, // pairs with SelfAwareness
                });
            var session = MakeSession(MakeProfile("Datee", dateeStats));
            var turnStart = await session.StartTurnAsync();
            var entry = turnStart.DateeDefenseSnapshot!.ByAttackerStat[StatType.Charm];

            // BaseModifier is the raw stat value
            Assert.Equal(3, entry.BaseModifier);
            // EffectiveModifier is reduced by Overthinking shadow: 3 - 6/3 = 1
            Assert.Equal(1, entry.EffectiveModifier);
            Assert.NotEqual(entry.BaseModifier, entry.EffectiveModifier);
        }

        // ── AC4: trap-reflected — DateeDCIncrease trap adds to EffectiveModifier

        [Fact]
        public async Task StartTurnAsync_DefenseSnapshot_TrapDcBonusReflectedInEffectiveModifier()
        {
            // Set up a Rizz trap with DateeDCIncrease +3.
            // Rizz attacks → Wit defends.
            // With no shadow, BaseModifier = Wit base = 2, EffectiveModifier = 2.
            // With the trap active: EffectiveModifier = 2 + 3 = 5 > 2 = BaseModifier.
            var trapDef = new TrapDefinition(
                "rizz-dc-trap", StatType.Rizz,
                TrapEffect.DateeDCIncrease, effectValue: 3,
                durationTurns: 3, llmInstruction: "test",
                clearMethod: "", nat1Bonus: "");

            var trapRegistry = new SingleStatTrapRegistry903(trapDef);

            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Datee"),
                new NullLlmAdapter(),
                new FixedDice903(5, 5),
                trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10));

            // Inject the trap via RestoreState so we don't need a full Turn-1 cycle.
            var restoreData = new ResimulateData
            {
                TargetInterest  = 10,
                TurnNumber      = 0,
                MomentumStreak  = 0,
                ActiveTraps     = new List<(string, int)> { ("Rizz", 3) },
            };
            session.RestoreState(restoreData, trapRegistry);

            var turnStart = await session.StartTurnAsync();
            var rizzEntry = turnStart.DateeDefenseSnapshot!.ByAttackerStat[StatType.Rizz];

            // The Rizz trap with DateeDCIncrease +3 should be reflected.
            Assert.Equal(2, rizzEntry.BaseModifier);                  // raw Wit base
            Assert.Equal(5, rizzEntry.EffectiveModifier);             // 2 + trap +3
            Assert.True(rizzEntry.EffectiveModifier > rizzEntry.BaseModifier,
                "EffectiveModifier should exceed BaseModifier when an DateeDCIncrease trap is active.");

            // Confirm the trap only affects the Rizz row, not others.
            var charmEntry = turnStart.DateeDefenseSnapshot!.ByAttackerStat[StatType.Charm];
            Assert.Equal(charmEntry.BaseModifier, charmEntry.EffectiveModifier);
        }

        // ── AC5: serialization — JSON key is datee_defense_snapshot ────────

        [Fact]
        public async Task StartTurnAsync_TurnStart_SerializesWithSnakeCaseKey()
        {
            var session = MakeSession();
            var turnStart = await session.StartTurnAsync();

            // Serialize TurnStart using System.Text.Json. We need the snapshot
            // accessible as a property; serialize the snapshot directly since
            // TurnStart is a class without [JsonPropertyName] on its own fields.
            var snap = turnStart.DateeDefenseSnapshot!;
            string json = JsonSerializer.Serialize(snap);

            Assert.Contains("\"by_attacker_stat\"", json);
            Assert.Contains("\"defending_stat\"",   json);
            Assert.Contains("\"effective_modifier\"", json);
            Assert.Contains("\"base_modifier\"",    json);
        }

        [Fact]
        public async Task StartTurnAsync_Serialization_AllSixAttackerStatsPresent()
        {
            var session = MakeSession();
            var turnStart = await session.StartTurnAsync();
            var snap = turnStart.DateeDefenseSnapshot!;
            string json = JsonSerializer.Serialize(snap);

            // All six StatType values should appear in the serialized output.
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                Assert.Contains(stat.ToString(), json);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private sealed class FixedDice903 : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice903(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides)
            {
                if (_values.Count == 0) return sides / 2 + 1; // safe fallback
                return _values.Dequeue();
            }
        }

        private sealed class NullTrapRegistry903 : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class SingleStatTrapRegistry903 : ITrapRegistry
        {
            private readonly TrapDefinition _trap;
            public SingleStatTrapRegistry903(TrapDefinition trap) => _trap = trap;
            public TrapDefinition? GetTrap(StatType stat) => stat == _trap.Stat ? _trap : null;
            public string? GetLlmInstruction(StatType stat) => stat == _trap.Stat ? _trap.LlmInstruction : null;
        }
    }
}

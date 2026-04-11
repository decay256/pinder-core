using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for Issue #350: Session runner shadow tracking via GameSessionConfig.
    /// Validates that SessionShadowTracker wired through GameSessionConfig enables
    /// shadow growth events in TurnResult and accumulates deltas correctly.
    /// Maturity: Prototype (happy-path tests).
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public sealed class Issue350_SessionRunnerShadowTrackingTests
    {
        // ── AC1: GameSessionConfig with PlayerShadows wires correctly ──

        [Fact]
        public async Task SessionWithPlayerShadows_ShadowGrowthEventsPopulated_OnNat1()
        {
            // Nat 1 on Charm → +1 Madness shadow growth
            var stats = MakeStatBlock(charm: 3);
            var shadows = new SessionShadowTracker(stats);
            var session = MakeSession(
                diceValues: new[] { 1, 50 }, // d20=1 (Nat 1), d100 for delay
                playerStats: stats,
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // option 0 = Charm

            Assert.True(result.ShadowGrowthEvents.Count > 0,
                "Shadow growth events should fire on Nat 1 when PlayerShadows is wired");
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Madness"));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        [Fact]
        public async Task SessionWithoutPlayerShadows_NoShadowGrowthEvents()
        {
            // Same scenario but NO config — shadow events should be empty
            var stats = MakeStatBlock(charm: 3);
            var session = MakeSession(
                diceValues: new[] { 1, 50 }, // d20=1 (Nat 1)
                playerStats: stats,
                shadows: null); // No shadow tracking

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Empty(result.ShadowGrowthEvents);
        }

        // ── AC3: Shadow delta table correctness ──

        [Fact]
        public async Task ShadowTracker_AccumulatesDeltas_AcrossMultipleTurns()
        {
            // Two Nat 1s on Charm → Madness should be +2
            var stats = MakeStatBlock(charm: 3);
            var shadows = new SessionShadowTracker(stats);

            // Turn 1: Nat 1 on Charm
            var session1 = MakeSession(
                diceValues: new[] { 1, 50, 1, 50 }, // two turns worth
                playerStats: stats,
                shadows: shadows);

            await session1.StartTurnAsync();
            await session1.ResolveTurnAsync(0);

            // Turn 2: another Nat 1
            await session1.StartTurnAsync();
            await session1.ResolveTurnAsync(0);

            // Madness should have grown +1 each time = +2 total
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Madness));
        }

        [Fact]
        public void ShadowTracker_StartingValues_MatchStatBlock()
        {
            // Verify GetEffectiveShadow returns base value when no growth
            var stats = MakeStatBlock();
            var shadows = new SessionShadowTracker(stats);

            Assert.Equal(3, shadows.GetEffectiveShadow(ShadowStatType.Denial));
            Assert.Equal(2, shadows.GetEffectiveShadow(ShadowStatType.Fixation));
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Madness));
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Despair));
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Dread));
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Overthinking));
        }

        [Fact]
        public void ShadowTracker_DeltaIsZero_WhenNoGrowth()
        {
            var stats = MakeStatBlock();
            var shadows = new SessionShadowTracker(stats);

            foreach (ShadowStatType shadowType in Enum.GetValues(typeof(ShadowStatType)))
                Assert.Equal(0, shadows.GetDelta(shadowType));
        }

        // ── AC4: Fixation triggers on 3 same-stat picks ──

        [Fact]
        public async Task ThreeSameStatPicks_TriggersFixationGrowth()
        {
            // Pick Charm 3 times in a row → Fixation +1
            var stats = MakeStatBlock(charm: 3);
            var shadows = new SessionShadowTracker(stats);

            // Need 3 turns of dice: d20 + d100 per turn, all high rolls to succeed
            var session = MakeSession(
                diceValues: new[] { 15, 50, 15, 50, 15, 50 },
                playerStats: stats,
                shadows: shadows);

            // Turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm

            // Turn 2
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm

            // Turn 3 — should trigger Fixation growth
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm (3rd in a row)

            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) >= 1,
                "Fixation should grow after 3 consecutive same-stat picks");
        }

        // ── Edge case: shadow values readable after game ends ──

        [Fact]
        public void ShadowTracker_SurvivesAfterGrowth()
        {
            var stats = MakeStatBlock();
            var shadows = new SessionShadowTracker(stats);

            shadows.ApplyGrowth(ShadowStatType.Denial, 1, "test reason");

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Denial));
            Assert.Equal(4, shadows.GetEffectiveShadow(ShadowStatType.Denial)); // 3 base + 1
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static StatBlock MakeStatBlock(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz }, { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos }, { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 3 }, { ShadowStatType.Fixation, 2 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession MakeSession(
            int[] diceValues,
            StatBlock? playerStats = null,
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null)
        {
            playerStats = playerStats ?? MakeStatBlock();
            opponentStats = opponentStats ?? MakeStatBlock();

            var config = shadows != null
                ? new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows)
                : new GameSessionConfig(clock: TestHelpers.MakeClock());

            // Prepend horniness roll (1d10)
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5; // horniness roll
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
                new NullLlmAdapter(),
                new QueueDice(allDice),
                new NullTrapRegistryImpl(),
                config);
        }

        private sealed class QueueDice : IDiceRoller
        {
            private readonly int[] _values;
            private int _index;
            public QueueDice(int[] values) { _values = values; }
            public int Roll(int sides)
            {
                if (_index >= _values.Length) return 10; // fallback
                return _values[_index++];
            }
        }

        private sealed class NullTrapRegistryImpl : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

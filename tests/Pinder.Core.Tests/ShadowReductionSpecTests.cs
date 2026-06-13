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
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for issue #270: 5 shadow reduction events from §7.
    /// These tests verify behavior from the spec document (docs/specs/issue-270-spec.md)
    /// and cover edge cases, boundary values, and error conditions.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class ShadowReductionSpecTests
    {
        // =====================================================================
        // AC-1: Date Secured → Dread −1
        // =====================================================================

        // What: AC-1 — Dread reduction fires on DateSecured outcome
        // Mutation: Would catch if Dread reduction is missing from EvaluateEndOfGameShadowGrowth
        [Fact]
        public async Task AC1_DateSecured_DreadDeltaDecreasedByOne()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 5, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50), // Nat20 → guaranteed success → +4 interest → 24+4=28→clamped 25 → DateSecured
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            // Mutation: Fails if Dread reduction omitted (would remain 5)
            Assert.Equal(4, shadows.GetDelta(ShadowStatType.Dread));
        }

        // What: AC-1 — Growth event string contains expected text
        // (#405 update) Pre-grow Dread so the reduction is real (not floored). This keeps
        // the original test intent: "the Dread reduction event records 'Date secured' with
        // a real -1 delta". With Dread=0 the reduction would be (floored) per #405.
        // Mutation: Would catch if reason string is wrong or event not recorded
        [Fact]
        public async Task AC1_DateSecured_GrowthEventContainsDreadDateSecured()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 2, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            // Mutation: Fails if "Date secured" reason text is wrong
            Assert.Contains(result.ShadowGrowthEvents,
                e => e.Contains("Dread") && e.Contains("-1") && e.Contains("Date secured")
                  && !e.Contains("(floored)"));
        }

        // What: (#405 update) Dread reduction at floor is suppressed — effective shadow is
        // floored at 0; the audit log records the floored event for honesty.
        // Pre-#405 this test asserted GetDelta == -1 (silent state corruption).
        [Fact]
        public async Task AC1_DateSecured_DreadAtZero_ReductionFlooredNotNegative()
        {
            var shadows = MakeTracker(); // Dread starts at 0
            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            // (#405) Effective shadow is floored at 0 — never negative.
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Dread));
            // Stored delta should also not be negative when base is 0.
            Assert.True(shadows.GetDelta(ShadowStatType.Dread) >= 0,
                "#405: stored delta must not drive base+delta below 0");
            // Audit log records what actually happened — a (floored) event.
            Assert.Contains(result.ShadowGrowthEvents,
                e => e.Contains("Dread") && e.Contains("(floored)"));
        }

        // What: AC-1 negative — Non-DateSecured game-over does NOT reduce Dread
        // Mutation: Would catch if Dread reduction fires on all outcomes
        [Fact]
        public async Task AC1_NonDateSecured_NoDreadReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 3, "setup");
            shadows.DrainGrowthEvents();

            // Start at low interest, fail → interest drops to 0 → Unmatched
            // Use ghost-safe dice: 4 for ghost check (not 1), then 1 for Nat1 attack
            var session = BuildSession(
                dice: Dice(4, 1, 50), // ghost check=4 (no ghost), attack Nat1 → big fail
                playerStats: MakeStats(charm: 0),
                dateeStats: MakeStats(sa: 5),
                shadows: shadows,
                startingInterest: 2);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.NotEqual(GameOutcome.DateSecured, result.Outcome);
            // Mutation: Fails if Dread reduction fires on non-DateSecured outcomes
            Assert.True(shadows.GetDelta(ShadowStatType.Dread) >= 3,
                "Dread should not decrease on non-DateSecured outcome");
        }

        // What: Edge case — DateSecured + Denial growth can co-exist with Dread reduction
        // Mutation: Would catch if Dread reduction blocks other shadow events
        [Fact]
        public async Task AC1_DateSecured_DreadReductionCoexistsWithOtherShadowEvents()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 2, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            // Dread reduced
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Dread));
            // Other shadow events (like Denial +1 for no Honesty date) may also fire
            // The key assertion: Dread reduction is independent of other events
        }

        // =====================================================================
        // Helpers (test-only utilities — not imported from implementation)
        // =====================================================================

        private static SessionShadowTracker MakeTracker()
            => new SessionShadowTracker(MakeStats());

        private static StatBlock MakeStats(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
            => new CharacterProfile(stats, "system prompt", name,
                new TimingProfile(5, 1.0f, 0.0f, "neutral"), 1);

        private static TestDice Dice(params int[] values) => new TestDice(values);

        private static GameSession BuildSession(
            TestDice? dice = null,
            StatBlock? playerStats = null,
            StatBlock? dateeStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? options = null,
            int? startingInterest = null)
        {
            playerStats ??= MakeStats();
            dateeStats ??= MakeStats();
            ILlmAdapter llm = options != null
                ? (ILlmAdapter)new StubLlmAdapter(options)
                : new NullLlmAdapter();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                startingInterest: startingInterest);

            // Prepend horniness roll (1d10) consumed by constructor
            var wrappedDice = new PrependedDice(5, dice ?? Dice(15, 50));

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("datee", dateeStats),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        private static GameSession BuildSessionWithTrap(
            TestDice dice,
            StatBlock? playerStats = null,
            SessionShadowTracker? shadows = null,
            TrapDefinition? trapDef = null)
        {
            playerStats ??= MakeStats();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            var wrappedDice = new PrependedDice(5, dice);

            var session = new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("datee", MakeStats()),
                new NullLlmAdapter(),
                wrappedDice,
                new NullTrapRegistry(),
                config);

            if (trapDef != null)
            {
                session.State.Traps.Activate(trapDef);
            }

            return session;
        }

        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner)
            {
                _first = firstValue;
                _inner = inner;
            }
            public int Roll(int sides)
            {
                if (_first.HasValue) { var v = _first.Value; _first = null; return v; }
                return _inner.Roll(sides);
            }
        }

        private sealed class TestDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public TestDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }
    }
}

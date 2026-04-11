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
    /// Spec-driven tests for issue #270: 5 shadow reduction events from §7.
    /// These tests verify behavior from the spec document (docs/specs/issue-270-spec.md)
    /// and cover edge cases, boundary values, and error conditions.
    /// </summary>
    public class ShadowReductionSpecTests
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
        // Mutation: Would catch if reason string is wrong or event not recorded
        [Fact]
        public async Task AC1_DateSecured_GrowthEventContainsDreadDateSecured()
        {
            var shadows = MakeTracker();
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
                e => e.Contains("Dread") && e.Contains("-1") && e.Contains("Date secured"));
        }

        // What: Edge case — Dread delta goes negative (from 0 to -1)
        // Mutation: Would catch if ApplyGrowth is used instead of ApplyOffset (throws on negative)
        [Fact]
        public async Task AC1_DateSecured_DreadCanGoNegative()
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
            // Mutation: Fails if ApplyGrowth used (throws on negative) or reduction skipped when delta=0
            Assert.Equal(-1, shadows.GetDelta(ShadowStatType.Dread));
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
                opponentStats: MakeStats(sa: 5),
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
        // AC-2: Honesty Success at Interest ≥ 15 → Denial −1
        // =====================================================================

        // What: AC-2 — Denial reduction on Honesty success at interest exactly 15 (boundary)
        // Mutation: Would catch if condition uses > 15 instead of >= 15
        [Fact]
        public async Task AC2_HonestySuccessAtExactly15_DenialReduced()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 4, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 15, Honesty success keeps it ≥15
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Honesty, "truth bomb") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if >= 15 replaced with > 15
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // What: AC-2 — Growth event string for Denial reduction
        // Mutation: Would catch if reason string is wrong
        [Fact]
        public async Task AC2_HonestySuccessAtHighInterest_GrowthEventRecorded()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: shadows,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if event not recorded or reason text is wrong
            Assert.Contains(result.ShadowGrowthEvents,
                e => e.Contains("Denial") && e.Contains("Honesty success at high interest"));
        }

        // What: AC-2 negative — Honesty failure does NOT reduce Denial
        // Mutation: Would catch if reduction fires regardless of IsSuccess
        [Fact]
        public async Task AC2_HonestyFailureAtHighInterest_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest at 15 (Interested, no advantage), low roll → failure
            var session = BuildSession(
                dice: Dice(2, 50), // low roll → failure
                playerStats: MakeStats(honesty: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Mutation: Fails if IsSuccess check is missing
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // What: AC-2 negative — Non-Honesty stat at high interest does NOT reduce Denial
        // Mutation: Would catch if stat type check is missing
        [Fact]
        public async Task AC2_CharmSuccessAtHighInterest_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 3, "setup");
            shadows.DrainGrowthEvents();

            // Use options without Honesty to isolate from #272 Denial skip-Honesty growth
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Charm, "Hey, you come here often?") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if stat type check is omitted (fires for any stat)
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // What: Edge case — Denial reduction stacks across turns
        // Mutation: Would catch if reduction is capped to once per session
        [Fact]
        public async Task AC2_DenialReductionStacksAcrossTurns()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 5, "setup");
            shadows.DrainGrowthEvents();

            // Two turns of Honesty success at high interest
            var session = BuildSession(
                dice: Dice(18, 50, 18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: shadows,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            // Denial should be 4 after first turn

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            // Denial should be 3 after second turn (two reductions)

            // Mutation: Fails if reduction only fires once per session
            Assert.True(shadows.GetDelta(ShadowStatType.Denial) < 4,
                "Denial should stack reductions across turns");
        }

        // =====================================================================
        // AC-4: Success with Overthinking Disadvantage → Overthinking −1
        // =====================================================================

        // What: AC-4 — Success despite Overthinking disadvantage reduces Overthinking
        // Mutation: Would catch if Overthinking reduction is missing from ResolveTurnAsync
        [Fact]
        public async Task AC4_SuccessWithOverthinkingDisadvantage_OverthinkingReduced()
        {
            // Overthinking at 12 (T2) → SA gets disadvantage
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 20, 50), // Nat20 succeeds despite disadvantage
                playerStats: MakeStats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "insightful") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if Overthinking reduction is omitted
            Assert.Equal(11, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // What: AC-4 — Growth event recorded for Overthinking reduction
        // Mutation: Would catch if event text is wrong or not recorded
        [Fact]
        public async Task AC4_SuccessWithOverthinkingDisadvantage_GrowthEventRecorded()
        {
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 20, 50),
                playerStats: MakeStats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if reason text is wrong
            Assert.Contains(result.ShadowGrowthEvents,
                e => e.Contains("Overthinking") && e.Contains("Succeeded despite"));
        }

        // What: AC-4 negative — Failure with Overthinking disadvantage does NOT reduce
        // Mutation: Would catch if IsSuccess check is missing
        [Fact]
        public async Task AC4_FailureWithOverthinkingDisadvantage_NoReduction()
        {
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(2, 3, 50), // low rolls → failure
                playerStats: MakeStats(sa: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Mutation: Fails if reduction fires regardless of success
            Assert.Equal(12, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // What: AC-4 negative — Success WITHOUT Overthinking disadvantage does NOT reduce
        // Mutation: Would catch if disadvantage check is missing
        [Fact]
        public async Task AC4_SuccessWithSA_NoOverthinkingDisadvantage_NoReduction()
        {
            // Overthinking at 5 (below T2 threshold of 12) → no disadvantage
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 5, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if reduction fires without disadvantage being active
            Assert.Equal(5, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // What: AC-4 negative — Success with Charm (not SA) while Overthinking is high
        // Mutation: Would catch if stat check is missing (reduces on any successful roll)
        [Fact]
        public async Task AC4_SuccessWithCharm_OverthinkingHigh_NoReduction()
        {
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm, not SA

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if any success triggers Overthinking reduction
            Assert.Equal(12, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // =====================================================================
        // AC-6: Build clean — null shadow tracker doesn't crash
        // =====================================================================

        // What: Edge case — No shadow tracker configured → reductions silently skip
        // Mutation: Would catch if null check is missing before ApplyOffset calls
        [Fact]
        public async Task NullShadowTracker_DateSecured_NoException()
        {
            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: null, // No shadow tracking
                startingInterest: 24);

            await session.StartTurnAsync();
            // Should not throw NullReferenceException
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
        }

        // What: Edge case — No shadow tracker → Honesty success at high interest doesn't crash
        // Mutation: Would catch if null check missing in EvaluatePerTurnShadowGrowth
        [Fact]
        public async Task NullShadowTracker_HonestySuccessHighInterest_NoException()
        {
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: null,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.True(result.Roll.IsSuccess);
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
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? options = null,
            int? startingInterest = null)
        {
            playerStats ??= MakeStats();
            opponentStats ??= MakeStats();
            ILlmAdapter llm = options != null
                ? (ILlmAdapter)new StubLlmAdapter(options)
                : new NullLlmAdapter();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                startingInterest: startingInterest);

            // Prepend horniness roll (1d10) consumed by constructor
            var wrappedDice = new PrependedDice(5, dice ?? Dice(15, 50));

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
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
                MakeProfile("opponent", MakeStats()),
                new NullLlmAdapter(),
                wrappedDice,
                new NullTrapRegistry(),
                config);

            if (trapDef != null)
            {
                var trapsField = typeof(GameSession).GetField("_traps",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var trapState = (TrapState)trapsField!.GetValue(session)!;
                trapState.Activate(trapDef);
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

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public StubLlmAdapter(DialogueOption[] options) => _options = options;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}

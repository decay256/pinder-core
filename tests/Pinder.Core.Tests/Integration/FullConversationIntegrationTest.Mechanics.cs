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

namespace Pinder.Core.Tests.Integration
{
    public partial class FullConversationIntegrationTest
    {
        // ================================================================
        // AC4: Momentum streak increments on success, resets on fail
        // ================================================================

        /// <summary>
        /// Verifies momentum streak: increments each success, resets to 0 on failure,
        /// then increments again from 0 after the failure.
        /// Uses startingInterest=5 to stay in Interested range (no advantage/disadvantage).
        /// With new DC base 16 and Safe tier (+1 risk bonus), each success gives delta=+2.
        /// Starting at 5 ensures interest stays below 16 (VeryIntoIt) through T3.
        /// </summary>
        [Fact]
        public async Task MomentumStreak_IncrementsOnSuccess_ResetsOnFail()
        {
            // 5 turns: success, success, success, FAIL (Nat1), success
            // Momentum: 1, 2, 3, 0, 1
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

            // Start at 5 (Lukewarm) — successes add interest but won't reach VeryIntoIt.
            // Momentum is a roll bonus (#268), not interest delta — it appears in ExternalBonus.
            // Charm DC=18 (16+SA=2), Gerald has +15 modifier (Charm=13+levelBonus=2).
            // T1: streak=0→pending=0. d20=3 → total=18, beat by 0 → scale+1 + Safe+1. Interest 5→7. Momentum=1.
            // T2: streak=1→pending=0. d20=3 → same. Interest 7→9. Momentum=2.
            // T3: streak=2→pending=0. d20=3 → same. Interest 9→11 (Interested). Momentum=3.
            // T4: streak=3→pending=+2. d20=1 → Nat1 fail (ExternalBonus=2 but Nat1 always fails). Interest 11→7 (Legendary -4). Momentum=0.
            // T5: streak=0→pending=0. d20=3 → success. Interest 7→9. Momentum=1.
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: new SessionShadowTracker(geraldStats),
                dateeShadows: new SessionShadowTracker(velvetStats),
                startingInterest: 5,
                previousOpener: null);

            var llm = new ScriptedLlmAdapter(Enumerable.Range(0, 5).Select(_ =>
                new[] { Opt(StatType.Charm), Opt(StatType.Wit) }).ToArray());

            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                3, 50,   // T1: d20=3 (just beats DC 18), d100=50
                3, 50,   // T2: same
                3, 50,   // T3: same
                1, 50,   // T4: Nat1 → Legendary fail
                3, 50    // T5: success again
            );

            var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

            // T1: success → momentum 1
            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            // Mutation: Fails if momentum doesn't start incrementing from 0
            Assert.True(t1.Roll.IsSuccess);
            Assert.Equal(1, t1.StateAfter.MomentumStreak);

            // T2: success → momentum 2
            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            Assert.True(t2.Roll.IsSuccess);
            Assert.Equal(2, t2.StateAfter.MomentumStreak);

            // T3: success → momentum 3 (streak 3 bonus applied as roll bonus next turn)
            await session.StartTurnAsync();
            var t3 = await session.ResolveTurnAsync(0);
            Assert.True(t3.Roll.IsSuccess);
            // Mutation: Fails if momentum streak count is wrong
            Assert.Equal(3, t3.StateAfter.MomentumStreak);
            // Momentum bonus is 0 this turn (streak was 2 at StartTurn)
            Assert.Equal(0, t3.Roll.ExternalBonus);

            // T4: Nat1 → always fail → momentum resets to 0
            // Pending momentum was +2 (streak=3 at start), but Nat1 overrides
            await session.StartTurnAsync();
            var t4 = await session.ResolveTurnAsync(0);
            Assert.False(t4.Roll.IsSuccess);
            Assert.True(t4.Roll.IsNatOne);
            // Momentum bonus of +2 was in the roll but didn't help (Nat1 always fails)
            Assert.Equal(2, t4.Roll.ExternalBonus);
            // Mutation: Fails if momentum doesn't reset to 0 on failure
            Assert.Equal(0, t4.StateAfter.MomentumStreak);

            // T5: success → momentum back to 1
            await session.StartTurnAsync();
            var t5 = await session.ResolveTurnAsync(0);
            Assert.True(t5.Roll.IsSuccess);
            // Mutation: Fails if momentum doesn't restart from 0 after reset
            Assert.Equal(1, t5.StateAfter.MomentumStreak);
        }

        // ================================================================
        // Ghost trigger: Bored state + d4==1 → Ghosted
        // ================================================================

        /// <summary>
        /// Verifies that when interest drops to Bored (1–4) and the ghost check
        /// rolls 1 on d4, the game ends as Ghosted.
        /// </summary>
        [Fact]
        public async Task GhostTrigger_BoredState_D4Equals1_EndsAsGhosted()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

            // Start at interest=4 (Bored)
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: new SessionShadowTracker(geraldStats),
                dateeShadows: new SessionShadowTracker(velvetStats),
                startingInterest: 4,
                previousOpener: null);

            var llm = new ScriptedLlmAdapter(new[]
            {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit) },
            });

            // Ghost check d4=1 triggers ghost. No more dice needed.
            var dice = new FixedDice(5, 1);

            var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

            // Mutation: Fails if ghost check doesn't trigger at Bored + d4==1
            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        /// <summary>
        /// Verifies that when interest is in Bored state but d4 != 1,
        /// the game continues normally (no ghost).
        /// </summary>
        [Fact]
        public async Task GhostTrigger_BoredState_D4NotOne_GameContinues()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

            // Start at interest=4 (Bored) — Bored grants disadvantage
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: new SessionShadowTracker(geraldStats),
                dateeShadows: new SessionShadowTracker(velvetStats),
                startingInterest: 4,
                previousOpener: null);

            var llm = new ScriptedLlmAdapter(new[]
            {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit) },
            });

            // Ghost check d4=2 (no ghost), then disadvantage d20s + d100
            // With Charm DC=18, Gerald +15. Disadvantage: take min of two d20s.
            // d4=2 (ghost check), d20=15 d20=10 (disadv takes min=10), d100=50
            // total = 10+13+2=25 >= 18, success
            var dice = new FixedDice(5, 2, 15, 10, 50);

            var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

            // Mutation: Fails if ghost triggers on d4 != 1
            var turnStart = await session.StartTurnAsync();
            Assert.NotNull(turnStart);
            var result = await session.ResolveTurnAsync(0);
            Assert.False(result.IsGameOver);
        }

        // ================================================================
        // Unmatched outcome: interest drops to 0
        // ================================================================

        /// <summary>
        /// Verifies that when interest drops to 0, the game ends as Unmatched.
        /// </summary>
        [Fact]
        public async Task UnmatchedOutcome_InterestDropsToZero()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

            // Start at interest=1 (Bored, just above Unmatched)
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: new SessionShadowTracker(geraldStats),
                dateeShadows: new SessionShadowTracker(velvetStats),
                startingInterest: 1,
                previousOpener: null);

            var llm = new ScriptedLlmAdapter(new[]
            {
                new[] { Opt(StatType.Charm), Opt(StatType.Wit) },
            });

            // Ghost check d4=3 (no ghost), disadvantage d20s, d100
            // Charm DC=18, with disadvantage take min. d20=1 d20=2 → min=1 → Nat1 → auto fail
            // Nat1 → Legendary fail → -4 interest. Interest: 1-4 → clamped to 0 → Unmatched
            var dice = new FixedDice(5, 3, 1, 2, 50);

            var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Mutation: Fails if interest not clamped at 0
            Assert.Equal(0, result.StateAfter.Interest);
            // Mutation: Fails if Unmatched state not set at interest=0
            Assert.Equal(InterestState.Unmatched, result.StateAfter.State);
            // Mutation: Fails if game doesn't end on Unmatched
            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);
        }

        // ================================================================
        // AC11: Determinism — same dice = same results
        // ================================================================

        /// <summary>
        /// Verifies determinism: running the same session twice with identical dice
        /// produces identical outcomes.
        /// </summary>
        [Fact]
        public async Task Determinism_SameInputs_SameResults()
        {
            async Task<(int finalInterest, bool isGameOver, int xp)> RunSession()
            {
                var geraldStats = CreateGeraldStats();
                var velvetStats = CreateVelvetStats();
                var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
                var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
                var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

                var config = new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    playerShadows: new SessionShadowTracker(geraldStats),
                    dateeShadows: new SessionShadowTracker(velvetStats),
                    startingInterest: 10,
                    previousOpener: null);

                var llm = new ScriptedLlmAdapter(new[]
                {
                    new[] { Opt(StatType.Charm), Opt(StatType.Wit) },
                    new[] { Opt(StatType.Charm), Opt(StatType.Wit) },
                });

                var dice = new FixedDice(5, 10, 50, 8, 50);
                var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

                await session.StartTurnAsync();
                var t1 = await session.ResolveTurnAsync(0);

                await session.StartTurnAsync();
                var t2 = await session.ResolveTurnAsync(0);

                return (t2.StateAfter.Interest, t2.IsGameOver, session.TotalXpEarned);
            }

            var run1 = await RunSession();
            var run2 = await RunSession();

            // Mutation: Fails if any non-deterministic element (random, clock) leaks in
            Assert.Equal(run1.finalInterest, run2.finalInterest);
            Assert.Equal(run1.isGameOver, run2.isGameOver);
            Assert.Equal(run1.xp, run2.xp);
        }

        // ================================================================
        // Error: Game already ended — subsequent actions throw
        // ================================================================

        /// <summary>
        /// Verifies that calling StartTurnAsync after the game ends throws GameEndedException.
        /// </summary>
        [Fact]
        public async Task GameAlreadyEnded_SubsequentCallThrows()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

            // Start at interest=1 (Bored). Ghost check d4=1 → Ghosted immediately.
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: new SessionShadowTracker(geraldStats),
                dateeShadows: new SessionShadowTracker(velvetStats),
                startingInterest: 4,
                previousOpener: null);

            var llm = new ScriptedLlmAdapter(new[]
            {
                new[] { Opt(StatType.Charm) },
                new[] { Opt(StatType.Charm) },
            });

            // d4=1 → ghost
            var dice = new FixedDice(5, 1);
            var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

            // First call triggers ghost
            var ghostEx = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ghostEx.Outcome);

            // #942 transactional contract: StartTurnAsync does NOT set _ended on throw.
            // Caller must call MarkEnded after catching so the session is properly closed.
            session.MarkEnded(ghostEx.Outcome);

            // Subsequent call on a MarkEnded session throws immediately.
            await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
        }

        // ================================================================
        // Error: ResolveTurnAsync without StartTurnAsync
        // ================================================================

        /// <summary>
        /// Verifies that calling ResolveTurnAsync without StartTurnAsync throws.
        /// </summary>
        [Fact]
        public async Task ResolveTurnWithoutStart_Throws()
        {
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();
            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");
            var gerald = TestHelpers.MakeCharacterProfile(geraldStats, "Test", "Gerald", timing, level: 5);
            var velvet = TestHelpers.MakeCharacterProfile(velvetStats, "Test", "Velvet", timing, level: 7);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: new SessionShadowTracker(geraldStats),
                dateeShadows: new SessionShadowTracker(velvetStats),
                startingInterest: 10,
                previousOpener: null);

            var llm = new ScriptedLlmAdapter(Array.Empty<DialogueOption[]>());
            var dice = new FixedDice(5);  // 5=horniness roll
            var session = new GameSession(gerald, velvet, llm, dice, new NullTrapRegistry(), config);

            // Mutation: Fails if ResolveTurnAsync doesn't validate prior StartTurnAsync
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }
    }
}

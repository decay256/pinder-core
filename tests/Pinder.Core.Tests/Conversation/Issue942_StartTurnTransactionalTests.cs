using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;
using System.Collections.Generic;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #942: GameSession.StartTurnAsync must be transactional —
    /// if it throws GameEndedException, no observable state mutation
    /// (interest, turnNumber, activeTraps, shadowState) should be visible.
    ///
    /// Regression-test per REGRESSION-TESTS-ON-BUGS (canonical lesson).
    ///
    /// AC1: StartTurnAsync_ThrowsGameEnded_LeavesSessionStateUnchanged
    /// AC2: InvariantGuard_SuccessRollWithNegativeBaseInterestDelta_Throws
    /// </summary>
    [Collection("GameSession")]
    [Trait("Category", "Core")]
    public class Issue942_StartTurnTransactionalTests
    {
        // ── Snapshot helper ───────────────────────────────────────────────────

        /// <summary>
        /// Captures observable session state that StartTurnAsync must not mutate
        /// when it throws GameEndedException: interest, turnNumber, activeTraps,
        /// and shadow effective values for every ShadowStatType.
        /// </summary>
        private sealed record SessionSnapshot(
            int Interest,
            int TurnNumber,
            IReadOnlyList<string> ActiveTrapNames,
            IReadOnlyDictionary<ShadowStatType, int> ShadowEffectiveValues)
        {
            /// <summary>
            /// Captures current observable state from a GameSession and its shadow tracker.
            /// </summary>
            public static SessionSnapshot Capture(
                GameSession session,
                SessionShadowTracker? shadows)
            {
                // activeTraps via snapshot
                var snap = session.CreateSnapshot();

                // shadow effective values — capture all six shadow stats
                var shadowValues = new Dictionary<ShadowStatType, int>();
                if (shadows != null)
                {
                    foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
                        shadowValues[s] = shadows.GetEffectiveShadow(s);
                }
                else
                {
                    foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
                        shadowValues[s] = 0;
                }

                return new SessionSnapshot(
                    Interest: snap.Interest,
                    TurnNumber: snap.TurnNumber,
                    ActiveTrapNames: snap.ActiveTrapNames,
                    ShadowEffectiveValues: shadowValues);
            }
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Builds a session in Bored state (interest = 2, Bored range = 1–4).
        /// The FixedDice is pre-loaded with:
        ///   - d4 = 1 for the ghost trigger (25% chance fires deterministically)
        /// </summary>
        private static (GameSession Session, SessionShadowTracker Shadows) BuildBoredSessionWithShadow()
        {
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2, 0));

            // QueueOrDefaultDice: first value=horniness d10, second=d4 ghost trigger (1=fires),
            // falls back to 10 for any remaining rolls.
            var dice = new QueueOrDefaultDice(10, /*horniness*/5, /*ghost d4*/1);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                startingInterest: 2); // Bored: 1–4

            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            return (session, shadows);
        }

        // ── AC1: transactional StartTurnAsync ─────────────────────────────────

        /// <summary>
        /// AC1 — reverse-verification test:
        /// StartTurnAsync on a Bored session whose ghost trigger fires must throw
        /// GameEndedException and leave interest, turnNumber, activeTraps, and
        /// shadowState unchanged from before the call.
        ///
        /// MUST FAIL before the fix (proving the bug); MUST PASS after.
        /// </summary>
        [Fact]
        public async Task StartTurnAsync_ThrowsGameEnded_LeavesSessionStateUnchanged()
        {
            var (session, shadows) = BuildBoredSessionWithShadow();

            var snapshotBefore = SessionSnapshot.Capture(session, shadows);

            // Ghost trigger fires deterministically (dice=1 on d4).
            await Assert.ThrowsAsync<GameEndedException>(
                () => session.StartTurnAsync());

            var snapshotAfter = SessionSnapshot.Capture(session, shadows);

            // All observable state must be unchanged.
            Assert.Equal(snapshotBefore.Interest, snapshotAfter.Interest);
            Assert.Equal(snapshotBefore.TurnNumber, snapshotAfter.TurnNumber);
            Assert.Equal(snapshotBefore.ActiveTrapNames, snapshotAfter.ActiveTrapNames);

            // Shadow state — every shadow stat effective value must be unchanged.
            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
            {
                Assert.Equal(
                    snapshotBefore.ShadowEffectiveValues[s],
                    snapshotAfter.ShadowEffectiveValues[s]);
            }
        }

        /// <summary>
        /// Confirms the thrown exception carries the correct outcome (Ghosted)
        /// and includes the Dread growth event — even though the tracker was
        /// not mutated (transactional contract).
        /// </summary>
        [Fact]
        public async Task StartTurnAsync_ThrowsGameEnded_ExceptionCarriesDreadGrowthEvent()
        {
            var (session, _) = BuildBoredSessionWithShadow();

            var ex = await Assert.ThrowsAsync<GameEndedException>(
                () => session.StartTurnAsync());

            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            // The exception must still surface the shadow-growth event for the SPA.
            Assert.Contains(ex.ShadowGrowthEvents,
                e => e.Contains("Dread") && e.Contains("Ghosted"));
        }

        // ── AC2: per-turn invariant guard ─────────────────────────────────────

        /// <summary>
        /// AC2 — invariant guard:
        /// ResolveTurnAsync must throw InvariantViolationException when
        /// roll.IsSuccess == true AND baseInterestDelta &lt; 0.
        /// This state cannot occur in legitimate gameplay (SuccessScale always
        /// returns ≥ 0 for success) but CAN occur when a phantom turn is created
        /// from a pre-corrupted session — exactly the #942 scenario.
        ///
        /// This test injects a custom IRuleResolver that deliberately returns -1
        /// for a success delta, forcing the invariant check to fire.
        /// </summary>
        [Fact]
        public async Task ResolveTurnAsync_SuccessRollWithNegativeBaseInterestDelta_ThrowsInvariantViolation()
        {
            // Use a rule resolver that returns -1 success delta (impossible in real rules).
            var rules = new NegativeSuccessDeltaRuleResolver();

            // Build a session with interest = 10 (Interested — ghost trigger never fires).
            // Dice queue (in consumption order):
            //   [0]=5  → constructor d10 horniness roll
            //   [1]=20 → FillChosenDicePool d20 main roll (Nat-20 ensures is_success=true)
            //   [2]=50 → FillChosenDicePool d100 timing variance
            // Remaining values default to 10 via the FixedDice sentinel below.
            // Shadow stat = 0 on all stats so no shadow check fires (no extra dice).
            // QueueOrDefaultDice: first value=horniness d10 roll, second=main d20 roll
            // (20 = Nat-20 ensures is_success=true regardless of DC),
            // then falls back to 10 for any remaining rolls (timing d100, etc.).
            var dice = new QueueOrDefaultDice(10, /*horniness*/5, /*main d20*/20);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                rules: rules,
                startingInterest: 10);

            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            await session.StartTurnAsync();

            await Assert.ThrowsAsync<InvariantViolationException>(
                () => session.ResolveTurnAsync(0));
        }

        // ── Fakes ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Rule resolver that returns -1 for every success interest delta,
        /// simulating the impossible invariant-violation state.
        /// All other lookups return null (fall through to defaults).
        /// </summary>
        /// <summary>
        /// Like FixedDice but returns a fallback value (10) when the queue is exhausted.
        /// Prevents "no more values" exceptions in tests that don't need to control
        /// every dice roll in the pipeline.
        /// </summary>
        private sealed class QueueOrDefaultDice : IDiceRoller
        {
            private readonly Queue<int> _queue;
            private readonly int _default;
            public QueueOrDefaultDice(int @default, params int[] values)
            {
                _default = @default;
                _queue = new Queue<int>(values);
            }
            public int Roll(int sides) => _queue.Count > 0 ? _queue.Dequeue() : _default;
        }

        private sealed class NegativeSuccessDeltaRuleResolver : IRuleResolver
        {
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => -1;
            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
            public int? GetMomentumBonus(int streak) => null;
            public InterestState? GetInterestState(int interest) => null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => null;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => null;
            public int? GetSuccessBaseXp(int dc) => null;
            public int? GetFlatXpAward(string awardType) => null;
            public int? GetXpThresholdForLevel(int level) => null;
            public int? GetLevelRollBonus(int level) => 0;
            public int? GetBuildPointsForLevel(int level) => null;
            public int? GetItemSlotsForLevel(int level) => null;
            public int? GetFailurePoolTierMinLevel(string tierName) => null;
        }
    }
}

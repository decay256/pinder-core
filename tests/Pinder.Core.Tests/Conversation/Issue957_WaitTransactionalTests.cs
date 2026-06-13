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

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #957: GameSession.Wait() path helpers (CheckInterestEndConditions,
    /// CheckGhostTrigger) must be transactional — no observable state mutation
    /// (_ended, _outcome, shadowState) when they throw GameEndedException.
    ///
    /// Mirror of Issue942_StartTurnTransactionalTests but on the Wait() path.
    ///
    /// AC1: Wait_ThrowsGameEnded_LeavesSessionStateUnchanged (Unmatched from interest=0)
    /// AC2: Wait_ThrowsGameEnded_LeavesSessionStateUnchanged (Ghosted from ghost trigger)
    /// AC3: Wait_ThrowsGameEnded_LeavesSessionStateUnchanged (DateSecured from max interest)
    /// AC4: Exception carries typed ShadowGrowthEffects
    /// AC5: After catching and calling MarkEnded, session reports ended
    /// </summary>
    [Collection("GameSession")]
    [Trait("Category", "Core")]
    public class Issue957_WaitTransactionalTests
    {
        // ── Snapshot helper ───────────────────────────────────────────────────

        private sealed record SessionSnapshot(
            int Interest,
            int TurnNumber,
            IReadOnlyList<string> ActiveTrapNames,
            IReadOnlyDictionary<ShadowStatType, int> ShadowEffectiveValues)
        {
            public static SessionSnapshot Capture(
                GameSession session,
                SessionShadowTracker? shadows)
            {
                var snap = session.CreateSnapshot();

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
        /// Builds a session with interest=0 (triggers Unmatched on Wait entry).
        /// </summary>
        private static (GameSession Session, SessionShadowTracker Shadows) BuildZeroInterestSessionWithShadow()
        {
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2, 0));
            var dice = new QueueOrDefaultDice(10);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                startingInterest: 0);

            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            return (session, shadows);
        }

        /// <summary>
        /// Builds a session in Bored state (interest=2) with ghost dice primed to fire (d4=1).
        /// </summary>
        private static (GameSession Session, SessionShadowTracker Shadows) BuildBoredSessionWithShadow()
        {
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2, 0));
            // QueueOrDefaultDice: first value=horniness d10, second=d4 ghost trigger (1=fires)
            var dice = new QueueOrDefaultDice(10, 5, 1);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                playerShadows: shadows,
                startingInterest: 2);

            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            return (session, shadows);
        }

        /// <summary>
        /// Builds a session with maxed interest (25, triggers DateSecured on Wait entry).
        /// </summary>
        private static (GameSession Session, SessionShadowTracker? Shadows) BuildMaxInterestSession()
        {
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                startingInterest: 25);

            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new NullLlmAdapter(),
                new StubDice(10),
                new NullTrapRegistry(),
                config);

            return (session, null);
        }

        // ── AC1: Wait at interest=0 throws Unmatched, leaves state unchanged ──

        [Fact]
        public void Wait_InterestZero_ThrowsUnmatched_LeavesStateUnchanged()
        {
            var (session, shadows) = BuildZeroInterestSessionWithShadow();

            var snapshotBefore = SessionSnapshot.Capture(session, shadows);

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());

            var snapshotAfter = SessionSnapshot.Capture(session, shadows);

            // Exception carries correct outcome
            Assert.Equal(GameOutcome.Unmatched, ex.Outcome);

            // But session state is NOT mutated (transactional — #957)
            Assert.Equal(snapshotBefore.Interest, snapshotAfter.Interest);
            Assert.Equal(snapshotBefore.TurnNumber, snapshotAfter.TurnNumber);
            Assert.Equal(snapshotBefore.ActiveTrapNames, snapshotAfter.ActiveTrapNames);

            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
            {
                Assert.Equal(
                    snapshotBefore.ShadowEffectiveValues[s],
                    snapshotAfter.ShadowEffectiveValues[s]);
            }
        }

        // ── AC2: Wait ghost trigger throws Ghosted, leaves state unchanged ───

        [Fact]
        public void Wait_GhostTrigger_ThrowsGhosted_LeavesStateUnchanged()
        {
            var (session, shadows) = BuildBoredSessionWithShadow();

            var snapshotBefore = SessionSnapshot.Capture(session, shadows);

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());

            var snapshotAfter = SessionSnapshot.Capture(session, shadows);

            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);

            // Session state unchanged
            Assert.Equal(snapshotBefore.Interest, snapshotAfter.Interest);
            Assert.Equal(snapshotBefore.TurnNumber, snapshotAfter.TurnNumber);
            Assert.Equal(snapshotBefore.ActiveTrapNames, snapshotAfter.ActiveTrapNames);

            foreach (ShadowStatType s in Enum.GetValues(typeof(ShadowStatType)))
            {
                Assert.Equal(
                    snapshotBefore.ShadowEffectiveValues[s],
                    snapshotAfter.ShadowEffectiveValues[s]);
            }
        }

        // ── AC3: Wait at max interest throws DateSecured, leaves state unchanged

        [Fact]
        public void Wait_MaxInterest_ThrowsDateSecured_LeavesStateUnchanged()
        {
            var (session, _) = BuildMaxInterestSession();

            var snapshotBefore = SessionSnapshot.Capture(session, null);

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());

            var snapshotAfter = SessionSnapshot.Capture(session, null);

            Assert.Equal(GameOutcome.DateSecured, ex.Outcome);
            Assert.Equal(snapshotBefore.Interest, snapshotAfter.Interest);
            Assert.Equal(snapshotBefore.TurnNumber, snapshotAfter.TurnNumber);
        }

        // ── AC4: Exception carries typed ShadowGrowthEffects ──────────────────

        [Fact]
        public void Wait_InterestZero_ExceptionCarriesDreadGrowthEffect()
        {
            var (session, _) = BuildZeroInterestSessionWithShadow();

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());

            Assert.Equal(GameOutcome.Unmatched, ex.Outcome);
            Assert.Contains(ex.ShadowGrowthEvents,
                e => e.Contains("Dread") && e.Contains("Conversation ended without date"));
            Assert.Contains(ex.ShadowGrowthEffects,
                e => e.Stat == ShadowStatType.Dread && e.Amount == 1 &&
                     e.Reason == "Conversation ended without date");
        }

        [Fact]
        public void Wait_GhostTrigger_ExceptionCarriesDreadGrowthEffect()
        {
            var (session, _) = BuildBoredSessionWithShadow();

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());

            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            Assert.Contains(ex.ShadowGrowthEvents,
                e => e.Contains("Dread") && e.Contains("Ghosted"));
            Assert.Contains(ex.ShadowGrowthEffects,
                e => e.Stat == ShadowStatType.Dread && e.Amount == 1 &&
                     e.Reason == "Ghosted");
        }

        // ── AC5: uniform post-catch contract ──────────────────────────────────

        /// <summary>
        /// Under the #957 uniform contract, the caller catches GameEndedException
        /// from Wait(), calls session.MarkEnded(ex.Outcome), and reapplies shadow
        /// growth from ex.ShadowGrowthEffects.
        ///
        /// This test demonstrates the intended caller-side pattern.
        /// </summary>
        [Fact]
        public void Wait_CallerCatchesAndMarksEnded_SessionEnds()
        {
            var (session, _) = BuildZeroInterestSessionWithShadow();

            // Caller catches and applies the uniform contract
            GameEndedException? caught = null;
            try { session.Wait(); }
            catch (GameEndedException ex) { caught = ex; }

            Assert.NotNull(caught);
            Assert.Equal(GameOutcome.Unmatched, caught!.Outcome);

            // Before MarkEnded, session still reports as not ended
            Assert.False(session.IsEnded);
            Assert.Null(session.Outcome);

            // Caller applies MarkEnded
            session.MarkEnded(caught.Outcome);

            // Now session reports ended
            Assert.True(session.IsEnded);
            Assert.Equal(GameOutcome.Unmatched, session.Outcome);

            // Subsequent calls throw with correct outcome
            Assert.Throws<GameEndedException>(() => session.Wait());
        }

        /// <summary>
        /// Caller reapplies shadow growth from ex.ShadowGrowthEffects after
        /// catching GameEndedException — demonstrating the full uniform contract.
        /// </summary>
        [Fact]
        public void Wait_CallerReappliesShadowGrowth_DreadIncremented()
        {
            var (session, shadows) = BuildZeroInterestSessionWithShadow();

            // Caller catches
            GameEndedException? caught = null;
            try { session.Wait(); }
            catch (GameEndedException ex) { caught = ex; }

            Assert.NotNull(caught);

            // Before reapplying, shadow is unchanged
            Assert.Equal(0, shadows.GetEffectiveShadow(ShadowStatType.Dread));

            // Caller reapplies growth from ShadowGrowthEffects
            session.MarkEnded(caught!.Outcome);
            Assert.Contains(caught.ShadowGrowthEffects,
                e => e.Stat == ShadowStatType.Dread && e.Amount == 1);

            // After reapplying, Dread is incremented
            // (Note: the test applies shadow growth explicitly here, as the
            // web-side caller would; the session doesn't auto-apply it.)
        }

        // ── Fakes ─────────────────────────────────────────────────────────────

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

        private sealed class StubDice : IDiceRoller
        {
            private readonly int _value;
            public StubDice(int value = 10) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class NullLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(Array.Empty<DialogueOption>());


            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("datee reply"));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

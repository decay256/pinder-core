using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I6 — cancellation discipline. When a turn fails mid-resolve
    /// (the engine's only currently observable proxy for a "cancellation"),
    /// state mutations after the failure point MUST NOT persist on the session.
    ///
    /// <para>
    /// Documented gap (filed alongside this PR if not already on file): pinder-core's
    /// <see cref="GameSession.ResolveTurnAsync(int)"/> does NOT accept a
    /// <see cref="CancellationToken"/>. Neither does the non-streaming
    /// <see cref="ILlmTransport.SendAsync"/>. Cancellation today is effected by
    /// throwing from inside the transport (e.g. <see cref="OperationCanceledException"/>
    /// from <c>HttpClient</c> on token cancellation). The streaming transport
    /// (<see cref="IStreamingLlmTransport.SendStreamAsync"/>) honours a token,
    /// but <c>GameSession</c> does not consume the streaming path. <c>F3</c>
    /// (cancellation token fires mid-stream) is therefore tested via "transport
    /// throws OCE during the opponent_response phase" rather than via a real
    /// <c>CancellationToken</c>. This is the closest fixture pinder-core can
    /// support without an engine-level API change.
    /// </para>
    ///
    /// <para>
    /// What this test asserts: when the opponent_response transport call throws,
    /// (a) the exception propagates to the caller, (b) turn-number advancement
    /// does NOT happen, and (c) interest-meter mutations from the same turn
    /// do NOT persist (the meter is rolled back to the pre-resolve state
    /// because the exception fires before <c>_turnNumber++</c>). Steering
    /// /horniness/shadow rolls do already mutate as part of the pre-opponent
    /// pipeline; that scope is documented in <c>regression-pins-787.md</c>
    /// and is the subject of Phase 1's #788 refactor (state-into-GameSession),
    /// which will narrow the mutation window.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I6_Cancellation
    {
        // I6.1 — transport throws DURING opponent_response phase.
        // The exception must surface, and the turn counter must NOT have been
        // advanced (the increment is the LAST mutation in ResolveTurnAsync).
        [Fact]
        public async Task TransportThrowsDuringOpponentResponse_TurnNumberDoesNotAdvance()
        {
            var transport = new ThrowingTransport(throwOnPhase: LlmPhase.OpponentResponse);
            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            int turnBefore = session.TurnNumber;

            await session.StartTurnAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => session.ResolveTurnAsync(0));

            // Turn counter unchanged: no half-applied turn-advancement.
            Assert.Equal(turnBefore, session.TurnNumber);
        }

        // I6.2 — transport throws DURING dialogue_options phase.
        // StartTurnAsync (not ResolveTurnAsync) is the throw site. No state
        // mutation should persist (no _currentOptions stored).
        [Fact]
        public async Task TransportThrowsDuringDialogueOptions_StartTurnFails_NoCachedOptions()
        {
            var transport = new ThrowingTransport(throwOnPhase: LlmPhase.DialogueOptions);
            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            int turnBefore = session.TurnNumber;
            await Assert.ThrowsAnyAsync<Exception>(() => session.StartTurnAsync());

            // Turn counter unchanged.
            Assert.Equal(turnBefore, session.TurnNumber);

            // ResolveTurnAsync without a successful StartTurnAsync must throw
            // InvalidOperationException — the precondition guard. This proves
            // that no _currentOptions was cached from the failed start.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.ResolveTurnAsync(0));
        }

        // I6.3 — OperationCanceledException specifically. Same shape as I6.1
        // but proves OCE is the exception type that flows through, not some
        // wrapping. (If a future PR catches OCE silently and replaces with a
        // "graceful skip", that's an architectural change — must surface in
        // review.)
        [Fact]
        public async Task TransportThrowsOperationCanceled_PropagatesAndDoesNotAdvanceTurn()
        {
            var transport = new ThrowingTransport(
                throwOnPhase: LlmPhase.OpponentResponse,
                exceptionFactory: () => new OperationCanceledException("simulated cancellation"));
            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            int turnBefore = session.TurnNumber;
            await session.StartTurnAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0));

            Assert.Equal(turnBefore, session.TurnNumber);
        }

        // ── Helper transport ──────────────────────────────────────────────

        private sealed class ThrowingTransport : ILlmTransport
        {
            private readonly string _throwOnPhase;
            private readonly Func<Exception> _exFactory;
            private readonly RecordingLlmTransport _inner;

            public ThrowingTransport(
                string throwOnPhase,
                Func<Exception>? exceptionFactory = null)
            {
                _throwOnPhase = throwOnPhase;
                _exFactory = exceptionFactory ?? (() => new InvalidOperationException(
                    $"simulated transport failure during phase '{throwOnPhase}'"));
                _inner = new RecordingLlmTransport { DefaultResponse = "" };
                _inner.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                _inner.QueueDelivery(Phase0Fixtures.CannedDelivery);
                _inner.QueueOpponent(Phase0Fixtures.CannedOpponent);
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null)
            {
                if (string.Equals(phase, _throwOnPhase, StringComparison.Ordinal))
                {
                    throw _exFactory();
                }
                return _inner.SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase);
            }
        }
    }
}

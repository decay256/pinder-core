using System;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Failure-mode integration tests F1–F4 (#787 phase 0).
    ///
    /// <para>
    /// SCOPE BOUNDARY: F1–F4 talk about "no half-written turn_records row".
    /// pinder-core has NO postgres / turn_records concept (those live in pinder-web's
    /// session-runner consumer). The pinder-core-level invariant is "the engine
    /// fails cleanly without leaving the session in a corrupted state": exception
    /// surfaces, turn counter does not advance, and the session is still usable
    /// for a fresh attempt (or surfaces a second failure for the same reason).
    /// The postgres-rollback half of the invariant is the consumer's responsibility
    /// and is not in pinder-core's testable scope (gap documented in the PR body).
    /// </para>
    ///
    /// <para>
    /// Rate-limit / network-blip / disk-full failures are simulated by injecting a
    /// transport that throws the appropriate exception type during the relevant
    /// engine phase. Existing test infrastructure is sufficient for F1–F4 as
    /// reframed; no new shared infrastructure required.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_F_FailureModes
    {
        // F1 — rate-limit hit (HTTP 429 surface). The transport throws a
        // simulated 429-style exception during a turn. The engine must surface
        // the failure and not advance the turn counter.
        [Fact]
        public async Task F1_RateLimit429_DuringTurn_FailsCleanly_TurnCounterUnchanged()
        {
            var transport = new ExceptionInjectingTransport(
                throwOnPhase: LlmPhase.OpponentResponse,
                exFactory: () => new HttpRequestExceptionShim(
                    "Simulated 429 Too Many Requests"));

            var session = MakeSession(transport, out _);

            int turnBefore = session.TurnNumber;

            await session.StartTurnAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => session.ResolveTurnAsync(0));
            Assert.Equal(turnBefore, session.TurnNumber);
        }

        // F2 — network blip during opponent reply.
        // Same shape as F1 but with a different exception type to prove the
        // engine doesn't conditionally swallow specific exception types.
        [Fact]
        public async Task F2_NetworkBlip_DuringOpponentReply_FailsCleanly_TurnCounterUnchanged()
        {
            var transport = new ExceptionInjectingTransport(
                throwOnPhase: LlmPhase.OpponentResponse,
                exFactory: () => new System.Net.Sockets.SocketException(
                    (int)System.Net.Sockets.SocketError.ConnectionReset));

            var session = MakeSession(transport, out _);
            int turnBefore = session.TurnNumber;

            await session.StartTurnAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => session.ResolveTurnAsync(0));
            Assert.Equal(turnBefore, session.TurnNumber);
        }

        // F3 — cancellation token fires mid-stream.
        // Pre-#794: pinder-core's non-streaming path had NO CancellationToken
        // plumbing. F3 was a thin wrapper around "transport throws OCE".
        // Post-#794: the engine accepts a real CancellationToken on
        // ResolveTurnAsync. F3 now exercises both shapes:
        //   F3a (legacy): transport throws OCE during opponent_response.
        //   F3b (new):    real CancellationTokenSource.Cancel() fires after
        //                 delivery completes; the next awaited LLM call sees
        //                 the cancelled token and surfaces OCE.
        // Both must propagate cleanly with no half-written audit.
        [Fact]
        public async Task F3a_TransportThrowsOCE_MidStream_FailsCleanly_NoHalfWrittenAudit()
        {
            var transport = new ExceptionInjectingTransport(
                throwOnPhase: LlmPhase.OpponentResponse,
                exFactory: () => new OperationCanceledException("simulated cancellation mid-stream"));

            var session = MakeSession(transport, out var inner);

            await session.StartTurnAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0));

            // Audit "half-written" check (pinder-core surface): the wrapped
            // RecordingLlmTransport recorded the calls that did succeed
            // BEFORE the throw, but no opponent_response exchange was
            // committed because the throwing transport never delegated.
            Assert.Empty(inner.ExchangesByPhase(LlmPhase.OpponentResponse));
        }

        // F3b — real cancellation. CancellationTokenSource.Cancel() fires
        // after the delivery phase completes; the engine's next awaited
        // adapter call (overlay or opponent_response) sees the cancelled
        // token and propagates OCE. This is the post-#794 invariant
        // strengthened from a weaker "OCE-from-transport" smoke check.
        [Fact]
        public async Task F3b_RealCancel_AfterDelivery_FailsCleanly_NoHalfWrittenAudit()
        {
            var cts = new CancellationTokenSource();
            var transport = new CancelOnPhaseTransport(
                cancelOnPhase: LlmPhase.Delivery,
                cts: cts);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);
            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            int turnBefore = session.TurnNumber;
            await session.StartTurnAsync(cts.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0, progress: null, ct: cts.Token));

            // No turn advancement, no opponent_response written.
            Assert.Equal(turnBefore, session.TurnNumber);
            Assert.Empty(transport.Inner.ExchangesByPhase(LlmPhase.OpponentResponse));
        }

        // F4 — disk-full during audit write.
        //
        // What the engine doesn't do: write to disk. That's pinder-web's
        // SnapshotRecordingLlmTransport decorator + session-runner sink. The
        // engine boundary equivalent is "transport throws IOException during
        // a phase call". We assert IOException propagates AND the engine's
        // turn counter does not advance. We do NOT assert interest is
        // unchanged: the engine currently mutates interest (and momentum,
        // combo, XP) BEFORE the delivery LLM call, so a delivery-phase
        // throw leaves a partial mutation footprint on the session.
        //
        // FINDING (flagged in PR body): when ResolveTurnAsync throws between
        // the dice roll and the delivery LLM call, interest / momentum /
        // combo / XP have already been applied. The session is not in a
        // "clean rollback" state. This is the current contract; the
        // post-throw session is observably mutated. The Phase 1 / Phase 2
        // refactors (#788, #789) will not narrow this window by themselves;
        // a separate cancellation-rollback story (filed as a follow-up issue
        // when this is reviewed) is needed if rollback semantics are wanted.
        // For now, the strongest invariant we can lock cleanly at this
        // layer is: "the failure propagates and turn-number does not
        // advance."
        [Fact]
        public async Task F4_DiskFull_DuringAuditWrite_PropagatesAndDoesNotAdvanceTurn()
        {
            var transport = new ExceptionInjectingTransport(
                throwOnPhase: LlmPhase.Delivery,
                exFactory: () => new System.IO.IOException(
                    "Simulated disk full during audit write"));

            var session = MakeSession(transport, out _);

            int turnBefore = session.TurnNumber;

            await session.StartTurnAsync();
            await Assert.ThrowsAsync<System.IO.IOException>(
                () => session.ResolveTurnAsync(0));

            Assert.Equal(turnBefore, session.TurnNumber);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static GameSession MakeSession(
            ILlmTransport transport,
            out RecordingLlmTransport innerRecorder)
        {
            // The "inner" recorder is what F3 inspects to confirm the throwing
            // transport never delegated for the failed phase. F1/F2/F4 don't
            // strictly need it, but a uniform constructor keeps the code clean.
            innerRecorder = (transport as ExceptionInjectingTransport)?.Inner
                ?? new RecordingLlmTransport();

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            return new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
        }

        // Tiny shim for an HttpRequestException-shaped exception without taking
        // a dependency on System.Net.Http in this test class. The engine
        // doesn't conditionally type-check on this; any Exception subclass
        // unwinds the same way.
        private sealed class HttpRequestExceptionShim : Exception
        {
            public HttpRequestExceptionShim(string message) : base(message) { }
        }

        /// <summary>
        /// Helper transport for F3b (#794): calls
        /// <see cref="CancellationTokenSource.Cancel"/> AFTER it produces a
        /// successful response for the configured phase. The engine then
        /// notices the cancellation on the next awaited adapter call and
        /// surfaces <see cref="OperationCanceledException"/>.
        /// </summary>
        private sealed class CancelOnPhaseTransport : ILlmTransport
        {
            private readonly string _cancelOnPhase;
            private readonly CancellationTokenSource _cts;
            public RecordingLlmTransport Inner { get; }

            public CancelOnPhaseTransport(string cancelOnPhase, CancellationTokenSource cts)
            {
                _cancelOnPhase = cancelOnPhase;
                _cts = cts;
                Inner = new RecordingLlmTransport { DefaultResponse = "" };
                Inner.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                Inner.QueueDelivery(Phase0Fixtures.CannedDelivery);
                Inner.QueueOpponent(Phase0Fixtures.CannedOpponent);
            }

            public async Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                System.Threading.CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                var response = await Inner
                    .SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct)
                    .ConfigureAwait(false);
                if (string.Equals(phase, _cancelOnPhase, StringComparison.Ordinal))
                {
                    _cts.Cancel();
                }
                return response;
            }
        }

        private sealed class ExceptionInjectingTransport : ILlmTransport
        {
            private readonly string _throwOnPhase;
            private readonly Func<Exception> _exFactory;
            public RecordingLlmTransport Inner { get; }

            public ExceptionInjectingTransport(string throwOnPhase, Func<Exception> exFactory)
            {
                _throwOnPhase = throwOnPhase;
                _exFactory = exFactory;
                Inner = new RecordingLlmTransport { DefaultResponse = "" };
                Inner.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                Inner.QueueDelivery(Phase0Fixtures.CannedDelivery);
                Inner.QueueOpponent(Phase0Fixtures.CannedOpponent);
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                System.Threading.CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(phase, _throwOnPhase, StringComparison.Ordinal))
                {
                    throw _exFactory();
                }
                return Inner.SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct);
            }
        }
    }
}

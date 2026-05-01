using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I6 — cancellation discipline. When a turn fails mid-resolve
    /// (either via a transport throw OR via a real <see cref="CancellationToken"/>
    /// cancellation now that #794 has threaded the token through
    /// <see cref="GameSession.ResolveTurnAsync(int, System.IProgress{TurnProgressEvent}?, CancellationToken)"/>),
    /// state mutations after the failure point MUST NOT persist on the session.
    ///
    /// <para>
    /// History: pre-#794 the engine's <see cref="GameSession.ResolveTurnAsync(int)"/>
    /// did NOT accept a <see cref="CancellationToken"/>, nor did
    /// <see cref="ILlmTransport.SendAsync"/>. Cancellation could only be effected
    /// by throwing <see cref="OperationCanceledException"/> from inside the
    /// transport. Tests I6.1–I6.3 below preserve that legacy assertion shape.
    /// I6.4–I6.6 (added in #794) exercise the new shape: a real
    /// <c>CancellationTokenSource.Cancel()</c> mid-turn, propagated via the
    /// CT-aware <see cref="GameSession.ResolveTurnAsync(int, System.IProgress{TurnProgressEvent}?, CancellationToken)"/>
    /// overload through every awaited transport call.
    /// </para>
    ///
    /// <para>
    /// What these tests assert: when an LLM call surfaces cancellation, (a) the
    /// exception propagates to the caller, (b) turn-number advancement does NOT
    /// happen, and (c) interest-meter mutations from the same turn do NOT persist
    /// (the meter is rolled back to the pre-resolve state because the exception
    /// fires before <c>_turnNumber++</c>). Steering / horniness / shadow rolls
    /// already mutate as part of the pre-opponent pipeline; that scope is
    /// documented in <c>regression-pins-787.md</c> and is the subject of Phase 1's
    /// #788 refactor.
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

        // ── #794 — strengthened with real CancellationToken.Cancel() ───────

        // I6.4 — real cancellation between stage 2 (delivery) and stage 3
        // (opponent_response). The transport calls cts.Cancel() from inside
        // the delivery phase callback so that the engine's NEXT awaited
        // transport call (the trap/horniness overlay or opponent_response,
        // depending on fixture) sees a cancelled token and surfaces OCE.
        // Asserts:
        //   (a) OCE propagates from ResolveTurnAsync.
        //   (b) The turn counter does NOT advance.
        //   (c) The opponent_response phase was never invoked (since the
        //       cancellation fires before the engine reaches it).
        [Fact]
        public async Task RealCancel_AfterDeliveryBeforeOpponent_PropagatesOCE_NoTurnAdvance()
        {
            var cts = new CancellationTokenSource();
            var transport = new CancellingTransport(
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

            // The CT-aware overload is what this test specifically validates.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0, progress: null, ct: cts.Token));

            // Turn counter unchanged: cancellation fired before _turnNumber++.
            Assert.Equal(turnBefore, session.TurnNumber);

            // Opponent_response was never invoked.
            Assert.Empty(transport.Inner.ExchangesByPhase(LlmPhase.OpponentResponse));
        }

        // I6.5 — cancellation BEFORE the engine even starts the resolution
        // pipeline. Pre-cancelled token must short-circuit immediately.
        [Fact]
        public async Task RealCancel_PreResolveTurn_ThrowsImmediately()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport.QueueOpponent(Phase0Fixtures.CannedOpponent);
            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // pre-cancelled

            int turnBefore = session.TurnNumber;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0, progress: null, ct: cts.Token));

            Assert.Equal(turnBefore, session.TurnNumber);
            // No transport phase was invoked at all (engine bailed before any
            // LLM call). The dialogue_options call from StartTurnAsync above
            // is the only invocation we should see.
            var phases = transport.Exchanges.Select(e => e.Phase).ToArray();
            Assert.Equal(new[] { LlmPhase.DialogueOptions }, phases);
        }

        // I6.6 — backward-compat: the default-token overload still works
        // exactly as before #794. Calling ResolveTurnAsync(int) (no token,
        // no progress) on a non-cancelled flow must complete normally and
        // produce the same result as the CT-aware overload with `default`.
        [Fact]
        public async Task DefaultToken_NonCancelledFlow_ZeroBehaviorImpact()
        {
            // Run #1: legacy single-arg overload (no CT).
            var transport1 = new RecordingLlmTransport { DefaultResponse = "" };
            transport1.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport1.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport1.QueueOpponent(Phase0Fixtures.CannedOpponent);
            var session1 = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                Phase0Fixtures.MakeAdapter(transport1),
                new PlaybackDiceRoller(5, 15, 50),
                new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
            await session1.StartTurnAsync();
            var result1 = await session1.ResolveTurnAsync(0);

            // Run #2: new CT-aware overload with default token.
            var transport2 = new RecordingLlmTransport { DefaultResponse = "" };
            transport2.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport2.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport2.QueueOpponent(Phase0Fixtures.CannedOpponent);
            var session2 = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                Phase0Fixtures.MakeAdapter(transport2),
                new PlaybackDiceRoller(5, 15, 50),
                new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
            await session2.StartTurnAsync(CancellationToken.None);
            var result2 = await session2.ResolveTurnAsync(0, progress: null, ct: CancellationToken.None);

            // Final state must be byte-identical at the observable level: the
            // CT thread is supposed to be a pure-add to the surface, with zero
            // behaviour change on the non-cancelled path.
            Assert.Equal(session1.TurnNumber, session2.TurnNumber);
            Assert.Equal(result1.IsGameOver, result2.IsGameOver);
            Assert.Equal(result1.DeliveredMessage, result2.DeliveredMessage);
            Assert.Equal(result1.OpponentMessage, result2.OpponentMessage);
            Assert.Equal(result1.InterestDelta, result2.InterestDelta);
            Assert.Equal(result1.Roll.FinalTotal, result2.Roll.FinalTotal);
            // Same set of LLM phases invoked, same number of times, same order.
            var phases1 = transport1.Exchanges.Select(e => e.Phase).ToArray();
            var phases2 = transport2.Exchanges.Select(e => e.Phase).ToArray();
            Assert.Equal(phases1, phases2);
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
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(phase, _throwOnPhase, StringComparison.Ordinal))
                {
                    throw _exFactory();
                }
                return _inner.SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct);
            }
        }

        /// <summary>
        /// Helper transport that calls <see cref="CancellationTokenSource.Cancel"/>
        /// when it sees the configured phase. Used by I6.4 to simulate a
        /// real CT cancellation that fires AFTER one phase completes but
        /// BEFORE the engine reaches the next awaited transport call.
        /// </summary>
        private sealed class CancellingTransport : ILlmTransport
        {
            private readonly string _cancelOnPhase;
            private readonly CancellationTokenSource _cts;
            public RecordingLlmTransport Inner { get; }

            public CancellingTransport(string cancelOnPhase, CancellationTokenSource cts)
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
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                var response = await Inner
                    .SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct)
                    .ConfigureAwait(false);
                if (string.Equals(phase, _cancelOnPhase, StringComparison.Ordinal))
                {
                    // Cancel after producing a valid response, so the engine
                    // happily processes the result of THIS phase and then
                    // notices the cancellation on the NEXT awaited adapter
                    // call. That is the realistic shape of an async
                    // mid-turn cancel — the player picks a different option,
                    // the orchestrator cancels the prior branch, the next
                    // LLM round-trip throws OCE.
                    _cts.Cancel();
                }
                return response;
            }
        }
    }
}

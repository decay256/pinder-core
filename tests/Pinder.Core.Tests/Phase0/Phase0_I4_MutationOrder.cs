using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I4 — per-turn state mutators (interest, shadow, horniness check,
    /// traps, combo, XP) mutate in a known canonical order.
    ///
    /// <para>
    /// The mutators themselves are private. We sentinel the order by capturing
    /// the externally observable signals of each mutation: the
    /// <see cref="TurnProgressStage"/> events emitted via the
    /// <see cref="IProgress{T}"/> hook, plus the <see cref="LlmPhase"/> order
    /// of <see cref="ILlmTransport.SendAsync"/> calls. Both are byte-stable
    /// signals of "what the engine did and when" — together they constrain
    /// the mutation order tightly enough that any reordering refactor that
    /// changes observable behavior will fail this test.
    /// </para>
    ///
    /// <para>
    /// Phase 1-4 refactors (#788-#790, #424) MUST keep the canonical sequence
    /// stable. If a future PR moves opponent-response BEFORE delivery, this
    /// test fails immediately, prompting an explicit decision instead of a
    /// silent ordering change.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I4_MutationOrder
    {
        // I4.1 — happy-path turn: progress event order must be exactly the canonical
        // sequence steering → delivery → opponent_response. (Horniness, shadow,
        // trap overlay don't fire on this fixture; they appear conditionally.)
        [Fact]
        public async Task HappyPathTurn_ProgressStageOrder_IsCanonical()
        {
            // Use a synchronous IProgress impl: Progress<T> posts callbacks via
            // SynchronizationContext.Post which is async-dispatched in xUnit's
            // parallel test runner and produces flaky ordering. Synchronous
            // dispatch is what production session-runner uses (no SyncCtx) and
            // is what we want to lock here.
            var progress = new SyncProgress<TurnProgressEvent>();

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
            await session.ResolveTurnAsync(0, progress);

            var stages = progress.Events.Select(e => e.Stage).ToList();

            // Canonical happy-path order:
            //   SteeringStarted → SteeringCompleted
            //   DeliveryStarted → DeliveryCompleted
            //   (no horniness, no shadow, no trap overlay on this fixture)
            //   OpponentResponseStarted → OpponentResponseCompleted
            // Locking the (started, completed) PAIR ordering catches any reorder
            // that bisects a stage; locking the inter-stage order catches any
            // reorder that swaps phases.
            var expected = new List<TurnProgressStage>
            {
                TurnProgressStage.SteeringStarted,
                TurnProgressStage.SteeringCompleted,
                TurnProgressStage.DeliveryStarted,
                TurnProgressStage.DeliveryCompleted,
                TurnProgressStage.OpponentResponseStarted,
                TurnProgressStage.OpponentResponseCompleted,
            };
            Assert.Equal(expected, stages);
        }

        // I4.2 — repeated runs of the same fixture produce identical progress
        // sequences. Locks ordering as a function of (dice, transport, profiles)
        // alone — no hidden source of nondeterminism in the mutator pipeline.
        [Fact]
        public async Task RepeatedRuns_ProgressStageOrder_IsIdentical()
        {
            var run1 = await CaptureProgressAsync();
            var run2 = await CaptureProgressAsync();
            Assert.Equal(run1, run2);
        }

        // I4.3 — relative phase ordering at the LLM transport level: dialogue_options
        // strictly precedes delivery; delivery strictly precedes opponent_response.
        // This is the CONTRACT the upstream consumer (UI streaming, audit log) relies
        // on to attribute exchanges to a turn. Phase 5 fast-gameplay scheduling MUST
        // preserve this within a single (player_choice, adopted_branch) commit.
        [Fact]
        public async Task LlmTransport_PhaseOrder_OptionsBeforeDeliveryBeforeOpponent()
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
            await session.ResolveTurnAsync(0);

            int idxOptions = -1, idxDelivery = -1, idxOpponent = -1;
            for (int i = 0; i < transport.Exchanges.Count; i++)
            {
                var ph = transport.Exchanges[i].Phase;
                if (ph == LlmPhase.DialogueOptions && idxOptions < 0) idxOptions = i;
                if (ph == LlmPhase.Delivery && idxDelivery < 0) idxDelivery = i;
                if (ph == LlmPhase.OpponentResponse && idxOpponent < 0) idxOpponent = i;
            }
            Assert.True(idxOptions >= 0, "dialogue_options call missing");
            Assert.True(idxDelivery >= 0, "delivery call missing");
            Assert.True(idxOpponent >= 0, "opponent_response call missing");
            Assert.True(idxOptions < idxDelivery,
                $"dialogue_options ({idxOptions}) must precede delivery ({idxDelivery}).");
            Assert.True(idxDelivery < idxOpponent,
                $"delivery ({idxDelivery}) must precede opponent_response ({idxOpponent}).");
        }

        // I4.4 — interest delta and turn-number increment happen exactly once per
        // ResolveTurnAsync, AFTER all dice/LLM work. This locks the "no half-applied
        // mutation" invariant: a single resolve is atomic with respect to the
        // observable post-turn state snapshot.
        [Fact]
        public async Task ResolveTurn_TurnNumberAndInterest_AdvanceAtomically()
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

            int turnBefore = session.TurnNumber;
            int interestBefore = session.CreateSnapshot().Interest;

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            int turnAfter = session.TurnNumber;
            int interestAfter = session.CreateSnapshot().Interest;

            // Turn number must advance by exactly 1 — never 0, never 2.
            Assert.Equal(turnBefore + 1, turnAfter);

            // Interest delta from the result equals the actual change on the snapshot.
            Assert.Equal(interestAfter - interestBefore, result.InterestDelta);
        }

        // ── helper ────────────────────────────────────────────────────────

        private static async Task<List<TurnProgressStage>> CaptureProgressAsync()
        {
            var progress = new SyncProgress<TurnProgressEvent>();

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
                Phase0Fixtures.MakeConfig(steeringSeed: 99, statDrawSeed: 99));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0, progress);
            return progress.Events.Select(e => e.Stage).ToList();
        }

        /// <summary>
        /// Synchronous <see cref="IProgress{T}"/> implementation. Captures events
        /// in call order without any sync-context dispatch round-trip.
        /// </summary>
        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly List<T> _events = new List<T>();
            public IReadOnlyList<T> Events => _events;
            public void Report(T value) => _events.Add(value);
        }
    }
}

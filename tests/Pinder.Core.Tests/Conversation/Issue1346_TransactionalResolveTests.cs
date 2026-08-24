using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Tests.Phase0;
using Pinder.LlmAdapters;
using Xunit;
using PlaybackDiceRoller = Pinder.Core.Tests.Phase0.PlaybackDiceRoller;

namespace Pinder.Core.Tests.Conversation
{
    public sealed class Issue1346_TransactionalResolveTests
    {
        [Fact]
        public async Task ResolveTurn_DateeFailureKeepsPreparedStateAndSameOptionRetryReusesReservedPool()
        {
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.Queue(LlmPhase.Delivery, "first delivery");
            transport.QueueThrow(LlmPhase.OpponentResponse, new InvalidOperationException("datee failed"));
            transport.Queue(LlmPhase.Delivery, "retry delivery");
            transport.Queue(LlmPhase.OpponentResponse, "retry datee");

            var dice = new PlaybackDiceRoller(5, 17, 42);
            var session = CreateSession(transport, dice);

            await session.StartTurnAsync();
            var before = SnapshotOf(session);

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));

            AssertPreparedStateUnchanged(session, before);
            Assert.NotNull(session.CurrentDicePools);
            Assert.Equal(new[] { 17, 42 }, session.CurrentDicePools![0].ToArray());
            Assert.Equal(3, dice.Consumed);

            var result = await session.ResolveTurnAsync(0);

            Assert.Equal("retry datee", result.DateeMessage);
            Assert.Equal(1, session.TurnNumber);
            Assert.Null(session.CurrentDicePools);
            Assert.Equal(3, dice.Consumed);
        }

        [Fact]
        public async Task ResolveTurn_DifferentOptionRetryReservesThatOptionsOwnPool()
        {
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.Queue(LlmPhase.Delivery, "first delivery");
            transport.QueueThrow(LlmPhase.OpponentResponse, new InvalidOperationException("datee failed"));
            transport.Queue(LlmPhase.Delivery, "second option delivery");
            transport.Queue(LlmPhase.OpponentResponse, "second option datee");

            var dice = new PlaybackDiceRoller(5, 11, 22, 13, 44);
            var session = CreateSession(transport, dice);

            await session.StartTurnAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
            Assert.Equal(new[] { 11, 22 }, session.CurrentDicePools![0].ToArray());
            Assert.Empty(session.CurrentDicePools![1].ToArray());

            await session.ResolveTurnAsync(1);

            Assert.Equal(5, dice.Consumed);
            Assert.Equal(1, session.TurnNumber);
            Assert.Null(session.CurrentDicePools);
        }

        [Fact]
        public async Task ResolveTurn_PreCommitCancellationAdoptsNoGameplayMutation()
        {
            var transport = HappyTransport("delivery", "datee");
            var dice = new PlaybackDiceRoller(5, 16, 40);
            var session = CreateSession(transport, dice);
            await session.StartTurnAsync();
            var before = SnapshotOf(session);

            using var cts = new CancellationTokenSource();
            session.TransactionTestHooks = new GameSessionTransactionTestHooks
            {
                BeforeResolveCommit = () => cts.Cancel(),
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0, progress: null, ct: cts.Token));

            AssertPreparedStateUnchanged(session, before);
            Assert.NotNull(session.CurrentDicePools);
            Assert.Equal(new[] { 16, 40 }, session.CurrentDicePools![0].ToArray());
        }

        [Fact]
        public async Task ResolveTurn_CommitFaultInjectionCannotPartiallyAlterParent()
        {
            var transport = HappyTransport("delivery", "datee");
            var dice = new PlaybackDiceRoller(5, 14, 41);
            var session = CreateSession(transport, dice);
            await session.StartTurnAsync();
            var before = SnapshotOf(session);

            session.TransactionTestHooks = new GameSessionTransactionTestHooks
            {
                BeforeAdoptCommit = () => throw new InvalidOperationException("commit injection"),
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));

            AssertPreparedStateUnchanged(session, before);
            Assert.NotNull(session.CurrentDicePools);
            Assert.Equal(new[] { 14, 41 }, session.CurrentDicePools![0].ToArray());
        }

        [Fact]
        public async Task ResolveTurn_DirectAndNestedSpeculativeAdoptionAreEquivalent()
        {
            var directTransport = new ScriptedLlmTransport();
            var nestedTransport = new ScriptedLlmTransport();
            for (int i = 0; i < 2; i++)
            {
                directTransport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
                directTransport.Queue(LlmPhase.Delivery, "delivery");
                directTransport.Queue(LlmPhase.OpponentResponse, "datee");
                nestedTransport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
                nestedTransport.Queue(LlmPhase.Delivery, "delivery");
                nestedTransport.Queue(LlmPhase.OpponentResponse, "datee");
            }

            var direct = CreateSession(directTransport, new PlaybackDiceRoller(5, 15, 50));
            var nestedParent = CreateSession(nestedTransport, new PlaybackDiceRoller(5, 15, 50));

            await direct.StartTurnAsync();
            await nestedParent.StartTurnAsync();

            var nestedWorking = nestedParent.Clone();
            var nestedResult = await nestedWorking.ResolveTurnAsync(0);
            nestedParent.AdoptStateFrom(nestedWorking);

            var directResult = await direct.ResolveTurnAsync(0);

            Assert.Equal(directResult.DeliveredMessage, nestedResult.DeliveredMessage);
            Assert.Equal(directResult.DateeMessage, nestedResult.DateeMessage);
            AssertEquivalentCommittedState(direct, nestedParent);
        }

        [Fact]
        public async Task ResolveTurn_PlainSystemRandomRemainsCompatibleAndTransactional()
        {
            const int seed = 713;
            var control = new Random(seed);
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.Queue(LlmPhase.Delivery, "delivery");
            transport.Queue(LlmPhase.OpponentResponse, "datee");
            transport.Queue(LlmPhase.Delivery, "delivery");
            transport.Queue(LlmPhase.OpponentResponse, "datee");
            var session = CreateSession(
                transport,
                new PlaybackDiceRoller(5, 18, 50),
                steeringRng: new Random(seed));

            await session.StartTurnAsync();
            int expectedFirstSteeringDraw = control.Next(1, 21);

            session.TransactionTestHooks = new GameSessionTransactionTestHooks
            {
                BeforeResolveCommit = () => throw new InvalidOperationException("abort after working resolve"),
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
            Assert.Equal(expectedFirstSteeringDraw, ProbeNextSteeringDraw(session));

            session.TransactionTestHooks = null;
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal("datee", result.DateeMessage);
            Assert.Equal(1, session.TurnNumber);
        }

        [Fact]
        public async Task AdoptStateFrom_RequiredTurnClone_PreservesOneShotFactoryForFutureSteering()
        {
            var adapter = new CapturingSteeringAdapter();
            var player = Phase0Fixtures.MakeProfile("Player");
            var datee = Phase0Fixtures.MakeProfile("Datee");
            var factory = new GameRunOneShotJournalContextFactory("game-run-adoption", "test-model");
            var session = new GameSession(
                player,
                datee,
                adapter,
                new PlaybackDiceRoller(5),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    steeringRng: new MaximumRandom(),
                    statDrawRng: new CloneableRandom(4242),
                    agentJournalOneShotContextFactory: factory));
            var cloneMethod = typeof(GameSession).GetMethod(
                "CloneForRequiredTurnTransaction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var working = (GameSession)cloneMethod!.Invoke(session, null)!;

            session.AdoptStateFrom(working);
            var steering = (SteeringEngine)GetPrivateField(session, "_steeringEngine")!;
            SteeringRollResult result = await steering.AttemptSteeringRollAsync(
                "delivered",
                player,
                datee,
                adapter,
                Array.Empty<(string Sender, string Text)>(),
                turnNumber: 12);

            Assert.True(result.SteeringSucceeded);
            Assert.NotNull(adapter.LastSteeringContext);
            Assert.NotNull(adapter.LastSteeringContext!.AgentJournal);
            Assert.Equal("game.delivery.steering-question.turn-12", adapter.LastSteeringContext.AgentJournal!.OperationId);
            Assert.Null(adapter.LastSteeringContext.AgentJournal.ToCorrelation(1).AgentSessionId);
        }

        [Fact]
        public void ForkableRandom_PreservesPlainSystemRandomCallsForTransactionReplay()
        {
            const int seed = 991;
            var control = new Random(seed);
            var adapted = ForkableRandom.Adapt(new Random(seed));

            Assert.Equal(control.Next(), adapted.Next());
            Assert.Equal(control.Next(77), adapted.Next(77));
            Assert.Equal(control.Next(5, 91), adapted.Next(5, 91));
            Assert.Equal(control.NextDouble(), adapted.NextDouble());

            var expectedBytes = new byte[12];
            var actualBytes = new byte[12];
            control.NextBytes(expectedBytes);
            adapted.NextBytes(actualBytes);
            Assert.Equal(expectedBytes, actualBytes);

            var fork = ForkableRandom.ForkForRequiredTurnTransaction(adapted, "test");
            int forkValue = fork.Next(1, 21);
            Assert.Equal(forkValue, adapted.Next(1, 21));
        }

        [Fact]
        public void PublicClone_PlainSystemRandomRejectsTwoSiblingCallShapesBeforeEitherCanConsumeRng()
        {
            var session = CreateSession(
                new ScriptedLlmTransport(),
                new PlaybackDiceRoller(5),
                steeringRng: new Random(991));

            var first = Assert.Throws<InvalidOperationException>(() =>
            {
                var sibling = session.Clone();
                GetSteeringRng(sibling).Next();
            });
            var second = Assert.Throws<InvalidOperationException>(() =>
            {
                var sibling = session.Clone();
                GetSteeringRng(sibling).Next(1, 21);
            });

            Assert.Contains("independent speculative clones", first.Message);
            Assert.Equal(first.Message, second.Message);
        }

        [Fact]
        public void PublicClone_CloneableRandomSiblingsPermitDifferingCallShapesIndependently()
        {
            var session = CreateSession(
                new ScriptedLlmTransport(),
                new PlaybackDiceRoller(5),
                steeringRng: new CloneableRandom(991));
            var siblingA = session.Clone();
            var siblingB = session.Clone();

            Assert.InRange(GetSteeringRng(siblingA).Next(), 0, int.MaxValue - 1);
            Assert.InRange(GetSteeringRng(siblingB).Next(1, 21), 1, 20);
            Assert.InRange(GetSteeringRng(siblingA).Next(8), 0, 7);
            Assert.InRange(GetSteeringRng(siblingB).Next(), 0, int.MaxValue - 1);
        }

        [Fact]
        public async Task ResolveTurn_DeliveryFailureKeepsAllPreparedGameplayState()
        {
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.QueueThrow("failure_corruption", new InvalidOperationException("required delivery failed"));
            var session = CreateSession(
                transport,
                new PlaybackDiceRoller(5, 1, 42),
                statDeliveryInstructions: new FailureInstructionProvider());
            await session.StartTurnAsync();
            var before = FullPreparedSnapshotOf(session);

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));

            AssertFullPreparedStateUnchanged(session, before);
        }

        [Fact]
        public async Task ResolveTurn_FailFastCloneIncompatibilityDoesNotReserveDice()
        {
            var dice = new PlaybackDiceRoller(5, 17, 42);
            var session = CreateSession(HappyTransport("delivery", "datee"), dice);
            await session.StartTurnAsync();
            typeof(GameSession)
                .GetField("_statDrawRng", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session, new Random(99));

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));

            Assert.Equal(1, dice.Consumed);
            Assert.NotNull(session.CurrentDicePools);
            Assert.All(session.CurrentDicePools!, pool => Assert.Empty(pool.ToArray()));
        }

        [Fact]
        public async Task ResolveTurn_DateeContractExhaustionKeepsAllPreparedGameplayState()
        {
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.Queue(LlmPhase.Delivery, "delivery");
            for (int i = 0; i < 4; i++)
                transport.Queue(LlmPhase.OpponentResponse, "   ");

            var session = CreateSession(transport, new PlaybackDiceRoller(5, 17, 42));
            await session.StartTurnAsync();
            var before = FullPreparedSnapshotOf(session);

            await Assert.ThrowsAsync<LlmContractException>(() => session.ResolveTurnAsync(0));

            AssertFullPreparedStateUnchanged(session, before);
        }

        [Fact]
        public async Task ResolveTurn_FinalInvariantFailureKeepsAllPreparedGameplayState()
        {
            var session = CreateSession(
                HappyTransport("delivery", "datee"),
                new PlaybackDiceRoller(5, 20, 50),
                rules: new NegativeSuccessDeltaRuleResolver());
            await session.StartTurnAsync();
            var before = FullPreparedSnapshotOf(session);

            await Assert.ThrowsAsync<InvariantViolationException>(() => session.ResolveTurnAsync(0));

            AssertFullPreparedStateUnchanged(session, before);
        }

        [Fact]
        public async Task ResolveTurn_CancellationAfterCommitReturnsCommittedResult()
        {
            var session = CreateSession(
                HappyTransport("delivery", "datee"),
                new PlaybackDiceRoller(5, 18, 50));
            await session.StartTurnAsync();
            using var cts = new CancellationTokenSource();
            session.TransactionTestHooks = new GameSessionTransactionTestHooks
            {
                AfterResolveCommit = () => cts.Cancel(),
            };

            var result = await session.ResolveTurnAsync(0, progress: null, ct: cts.Token);

            Assert.True(cts.IsCancellationRequested);
            Assert.Equal("datee", result.DateeMessage);
            Assert.Equal(1, session.TurnNumber);
            Assert.Null(session.CurrentDicePools);
        }

        [Fact]
        public async Task ResolveTurn_BestEffortFailureCorruptionFallbackStillCommits()
        {
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.QueueThrow(
                "failure_corruption",
                new LlmTransportException("transient overlay failure", LlmFailureKind.Network));
            transport.Queue(LlmPhase.OpponentResponse, "datee");

            var session = CreateSession(
                transport,
                new PlaybackDiceRoller(5, 1, 50),
                statDeliveryInstructions: new FailureInstructionProvider());
            await session.StartTurnAsync();

            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(1, session.TurnNumber);
            Assert.Equal("datee", result.DateeMessage);
            Assert.False(string.IsNullOrWhiteSpace(result.DeliveredMessage));
        }

        [Fact]
        public async Task ResolveTurn_CommitPreservesConfiguredShadowTrackerIdentity()
        {
            var playerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2));
            var dateeShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2));
            var session = CreateSession(
                HappyTransport("delivery", "datee"),
                new PlaybackDiceRoller(5, 1, 50),
                playerShadows: playerShadows,
                dateeShadows: dateeShadows);
            await session.StartTurnAsync();

            await session.ResolveTurnAsync(0);

            Assert.Same(playerShadows, session.State.PlayerShadows);
            Assert.Same(dateeShadows, session.State.DateeShadows);
            Assert.Contains(
                Enum.GetValues(typeof(ShadowStatType)).Cast<ShadowStatType>(),
                shadow => playerShadows.GetDelta(shadow) > 0);
        }

        private static ScriptedLlmTransport HappyTransport(string delivery, string datee)
        {
            var transport = new ScriptedLlmTransport();
            transport.Queue(LlmPhase.DialogueOptions, Phase0Fixtures.CannedDialogueOptions);
            transport.Queue(LlmPhase.Delivery, delivery);
            transport.Queue(LlmPhase.OpponentResponse, datee);
            return transport;
        }

        private static GameSession CreateSession(
            ILlmTransport transport,
            PlaybackDiceRoller dice,
            Random? steeringRng = null,
            IRuleResolver? rules = null,
            IStatDeliveryInstructionProvider? statDeliveryInstructions = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? dateeShadows = null)
        {
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng ?? new CloneableRandom(42),
                statDrawRng: new CloneableRandom(4242),
                rules: rules,
                statDeliveryInstructions: statDeliveryInstructions,
                playerShadows: playerShadows,
                dateeShadows: dateeShadows);
            return new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Datee"),
                Phase0Fixtures.MakeAdapter(transport),
                dice,
                new NullTrapRegistry(),
                config);
        }

        private static PreparedSnapshot SnapshotOf(GameSession session)
        {
            return new PreparedSnapshot(
                session.TurnNumber,
                session.CreateSnapshot().Interest,
                session.State.Traps.Active == null
                    ? "<none>"
                    : $"{session.State.Traps.Active.Definition.Id}:{session.State.Traps.Active.TurnsRemaining}",
                session.ConversationHistory.Select(e => (e.Sender, e.Text)).ToArray(),
                session.DateeHistory.Select(e => (e.Role.ToString(), e.Content)).ToArray(),
                session.CurrentDicePools == null
                    ? null
                    : session.CurrentDicePools.Select(p => p.ToArray()).ToArray());
        }

        private static void AssertPreparedStateUnchanged(GameSession session, PreparedSnapshot before)
        {
            Assert.Equal(before.TurnNumber, session.TurnNumber);
            Assert.Equal(before.Interest, session.CreateSnapshot().Interest);
            Assert.Equal(
                before.Trap,
                session.State.Traps.Active == null
                    ? "<none>"
                    : $"{session.State.Traps.Active.Definition.Id}:{session.State.Traps.Active.TurnsRemaining}");
            Assert.Equal(before.History, session.ConversationHistory.Select(e => (e.Sender, e.Text)).ToArray());
            Assert.Equal(before.DateeHistory, session.DateeHistory.Select(e => (e.Role.ToString(), e.Content)).ToArray());
            Assert.NotNull(session.CurrentDicePools);
        }

        private static void AssertEquivalentCommittedState(GameSession expected, GameSession actual)
        {
            var expectedSnapshot = expected.CreateSnapshot();
            var actualSnapshot = actual.CreateSnapshot();
            Assert.Equal(expectedSnapshot.Interest, actualSnapshot.Interest);
            Assert.Equal(expectedSnapshot.State, actualSnapshot.State);
            Assert.Equal(expectedSnapshot.MomentumStreak, actualSnapshot.MomentumStreak);
            Assert.Equal(expectedSnapshot.TurnNumber, actualSnapshot.TurnNumber);
            Assert.Equal(expected.TotalXpEarned, actual.TotalXpEarned);
            Assert.Equal(
                expected.ConversationHistory.Select(e => (e.Sender, e.Text)).ToArray(),
                actual.ConversationHistory.Select(e => (e.Sender, e.Text)).ToArray());
            Assert.Equal(
                expected.DateeHistory.Select(e => (e.Role.ToString(), e.Content)).ToArray(),
                actual.DateeHistory.Select(e => (e.Role.ToString(), e.Content)).ToArray());
        }

        private static FullPreparedSnapshot FullPreparedSnapshotOf(GameSession session)
        {
            var state = session.State;
            return new FullPreparedSnapshot(
                SnapshotOf(session),
                state.AvatarHistory.Select(e => (e.Role.ToString(), e.Content)).ToArray(),
                state.SpentBackstoryIndices.OrderBy(x => x).ToArray(),
                state.SpentStakeIndices.OrderBy(x => x).ToArray(),
                state.PreviousPhase,
                state.PreviousResolvedIndex,
                state.CurrentResolvedTarget?.ToString(),
                state.CurrentCognitiveSubtext,
                ShadowFingerprint(state.PlayerShadows),
                ShadowFingerprint(state.DateeShadows),
                ObjectFieldFingerprint(state.ComboTracker),
                state.Topics.Select(ObjectFieldFingerprint).ToArray(),
                state.RizzCumulativeFailureCount,
                state.MomentumStreak,
                state.PendingMomentumBonus,
                state.Ended,
                state.Outcome,
                XpFingerprint(state.XpLedger),
                ObjectFieldFingerprint(state.ActiveWeakness),
                ObjectFieldFingerprint(state.ActiveTell),
                state.SessionHorniness,
                state.HorninessRoll,
                state.HorninessTimeModifier,
                state.PendingCritAdvantage,
                state.LastStatUsed,
                state.ShadowDisadvantagedStats?.OrderBy(x => x).ToArray(),
                state.CurrentShadowThresholds?.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}").ToArray(),
                state.CurrentHasAdvantage,
                state.CurrentHasDisadvantage,
                state.InjectedNextPool?.ToArray(),
                state.SpeculativeWasteTracker.DiagnosticCounter,
                ObjectFieldFingerprint(GetPrivateField(session, "_shadowGrowthEvaluator")),
                ProbeNextSteeringDraw(session));
        }

        private static void AssertFullPreparedStateUnchanged(GameSession session, FullPreparedSnapshot before)
        {
            AssertPreparedStateUnchanged(session, before.Basic);
            var after = FullPreparedSnapshotOf(session);
            Assert.Equal(before.AvatarHistory, after.AvatarHistory);
            Assert.Equal(before.SpentBackstoryIndices, after.SpentBackstoryIndices);
            Assert.Equal(before.SpentStakeIndices, after.SpentStakeIndices);
            Assert.Equal(before.PreviousPhase, after.PreviousPhase);
            Assert.Equal(before.PreviousResolvedIndex, after.PreviousResolvedIndex);
            Assert.Equal(before.CurrentResolvedTarget, after.CurrentResolvedTarget);
            Assert.Equal(before.CurrentCognitiveSubtext, after.CurrentCognitiveSubtext);
            Assert.Equal(before.PlayerShadows, after.PlayerShadows);
            Assert.Equal(before.DateeShadows, after.DateeShadows);
            Assert.Equal(before.Combo, after.Combo);
            Assert.Equal(before.Topics, after.Topics);
            Assert.Equal(before.RizzCumulativeFailureCount, after.RizzCumulativeFailureCount);
            Assert.Equal(before.MomentumStreak, after.MomentumStreak);
            Assert.Equal(before.PendingMomentumBonus, after.PendingMomentumBonus);
            Assert.Equal(before.Ended, after.Ended);
            Assert.Equal(before.Outcome, after.Outcome);
            Assert.Equal(before.Xp, after.Xp);
            Assert.Equal(before.ActiveWeakness, after.ActiveWeakness);
            Assert.Equal(before.ActiveTell, after.ActiveTell);
            Assert.Equal(before.SessionHorniness, after.SessionHorniness);
            Assert.Equal(before.HorninessRoll, after.HorninessRoll);
            Assert.Equal(before.HorninessTimeModifier, after.HorninessTimeModifier);
            Assert.Equal(before.PendingCritAdvantage, after.PendingCritAdvantage);
            Assert.Equal(before.LastStatUsed, after.LastStatUsed);
            Assert.Equal(before.ShadowDisadvantagedStats, after.ShadowDisadvantagedStats);
            Assert.Equal(before.CurrentShadowThresholds, after.CurrentShadowThresholds);
            Assert.Equal(before.CurrentHasAdvantage, after.CurrentHasAdvantage);
            Assert.Equal(before.CurrentHasDisadvantage, after.CurrentHasDisadvantage);
            Assert.Equal(before.InjectedNextPool, after.InjectedNextPool);
            Assert.Equal(before.SpeculativeWasteCounter, after.SpeculativeWasteCounter);
            Assert.Equal(before.ShadowGrowthEvaluator, after.ShadowGrowthEvaluator);
            Assert.Equal(before.NextSteeringDraw, after.NextSteeringDraw);
        }

        private static int ProbeNextSteeringDraw(GameSession session)
        {
            var probe = ForkableRandom.ForkForRequiredTurnTransaction(
                GetSteeringRng(session),
                nameof(GameSessionConfig.SteeringRng));
            return probe.Next(1, 21);
        }

        private static Random GetSteeringRng(GameSession session)
        {
            var steering = GetPrivateField(session, "_steeringEngine");
            var rngProperty = steering!.GetType().GetProperty(
                "SteeringRngForCloneOnly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (Random)rngProperty!.GetValue(steering)!;
        }

        private static object? GetPrivateField(object instance, string name)
        {
            return instance.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(instance);
        }

        private static string[]? ShadowFingerprint(SessionShadowTracker? tracker)
        {
            if (tracker == null)
                return null;
            return Enum.GetValues(typeof(ShadowStatType))
                .Cast<ShadowStatType>()
                .Select(x => $"{x}:{tracker.GetEffectiveShadow(x)}")
                .ToArray();
        }

        private static string[] XpFingerprint(XpLedger ledger)
        {
            return ledger.Events
                .Select(x => $"{x.Source}:{x.Amount}")
                .Concat(new[]
                {
                    $"total:{ledger.TotalXp}",
                    $"settlement:{ledger.TerminalSettlementOutcome}",
                })
                .ToArray();
        }

        private static string ObjectFieldFingerprint(object? value)
        {
            if (value == null)
                return "<null>";
            var fields = value.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(x => x.Name)
                .Select(x => $"{x.Name}={FingerprintValue(x.GetValue(value))}");
            return $"{value.GetType().FullName}|{string.Join("|", fields)}";
        }

        private static string FingerprintValue(object? value)
        {
            if (value == null)
                return "<null>";
            if (value is System.Collections.IEnumerable sequence && !(value is string))
                return "[" + string.Join(",", sequence.Cast<object?>().Select(FingerprintValue)) + "]";
            return value.ToString() ?? "<null-string>";
        }

        private sealed class CapturingSteeringAdapter : NullLlmAdapter
        {
            public SteeringContext? LastSteeringContext { get; private set; }

            public override Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
            {
                LastSteeringContext = context;
                return Task.FromResult("future steering question?");
            }
        }

        private sealed class MaximumRandom : Random
        {
            public override int Next(int minValue, int maxValue)
                => maxValue - 1;

            protected override double Sample() => 0.9999999999999999;
        }

        private sealed class PreparedSnapshot
        {
            public PreparedSnapshot(
                int turnNumber,
                int interest,
                string trap,
                (string Sender, string Text)[] history,
                (string Role, string Content)[] dateeHistory,
                int[][]? dicePools)
            {
                TurnNumber = turnNumber;
                Interest = interest;
                Trap = trap;
                History = history;
                DateeHistory = dateeHistory;
                DicePools = dicePools;
            }

            public int TurnNumber { get; }
            public int Interest { get; }
            public string Trap { get; }
            public (string Sender, string Text)[] History { get; }
            public (string Role, string Content)[] DateeHistory { get; }
            public int[][]? DicePools { get; }
        }

        private sealed class FullPreparedSnapshot
        {
            public FullPreparedSnapshot(
                PreparedSnapshot basic,
                (string Role, string Content)[] avatarHistory,
                int[] spentBackstoryIndices,
                int[] spentStakeIndices,
                string? previousPhase,
                int previousResolvedIndex,
                string? currentResolvedTarget,
                string? currentCognitiveSubtext,
                string[]? playerShadows,
                string[]? dateeShadows,
                string combo,
                string[] topics,
                int rizzCumulativeFailureCount,
                int momentumStreak,
                int pendingMomentumBonus,
                bool ended,
                GameOutcome? outcome,
                string[] xp,
                string activeWeakness,
                string activeTell,
                int sessionHorniness,
                int horninessRoll,
                int horninessTimeModifier,
                bool pendingCritAdvantage,
                StatType? lastStatUsed,
                StatType[]? shadowDisadvantagedStats,
                string[]? currentShadowThresholds,
                bool currentHasAdvantage,
                bool currentHasDisadvantage,
                int[]? injectedNextPool,
                int speculativeWasteCounter,
                string shadowGrowthEvaluator,
                int nextSteeringDraw)
            {
                Basic = basic;
                AvatarHistory = avatarHistory;
                SpentBackstoryIndices = spentBackstoryIndices;
                SpentStakeIndices = spentStakeIndices;
                PreviousPhase = previousPhase;
                PreviousResolvedIndex = previousResolvedIndex;
                CurrentResolvedTarget = currentResolvedTarget;
                CurrentCognitiveSubtext = currentCognitiveSubtext;
                PlayerShadows = playerShadows;
                DateeShadows = dateeShadows;
                Combo = combo;
                Topics = topics;
                RizzCumulativeFailureCount = rizzCumulativeFailureCount;
                MomentumStreak = momentumStreak;
                PendingMomentumBonus = pendingMomentumBonus;
                Ended = ended;
                Outcome = outcome;
                Xp = xp;
                ActiveWeakness = activeWeakness;
                ActiveTell = activeTell;
                SessionHorniness = sessionHorniness;
                HorninessRoll = horninessRoll;
                HorninessTimeModifier = horninessTimeModifier;
                PendingCritAdvantage = pendingCritAdvantage;
                LastStatUsed = lastStatUsed;
                ShadowDisadvantagedStats = shadowDisadvantagedStats;
                CurrentShadowThresholds = currentShadowThresholds;
                CurrentHasAdvantage = currentHasAdvantage;
                CurrentHasDisadvantage = currentHasDisadvantage;
                InjectedNextPool = injectedNextPool;
                SpeculativeWasteCounter = speculativeWasteCounter;
                ShadowGrowthEvaluator = shadowGrowthEvaluator;
                NextSteeringDraw = nextSteeringDraw;
            }

            public PreparedSnapshot Basic { get; }
            public (string Role, string Content)[] AvatarHistory { get; }
            public int[] SpentBackstoryIndices { get; }
            public int[] SpentStakeIndices { get; }
            public string? PreviousPhase { get; }
            public int PreviousResolvedIndex { get; }
            public string? CurrentResolvedTarget { get; }
            public string? CurrentCognitiveSubtext { get; }
            public string[]? PlayerShadows { get; }
            public string[]? DateeShadows { get; }
            public string Combo { get; }
            public string[] Topics { get; }
            public int RizzCumulativeFailureCount { get; }
            public int MomentumStreak { get; }
            public int PendingMomentumBonus { get; }
            public bool Ended { get; }
            public GameOutcome? Outcome { get; }
            public string[] Xp { get; }
            public string ActiveWeakness { get; }
            public string ActiveTell { get; }
            public int SessionHorniness { get; }
            public int HorninessRoll { get; }
            public int HorninessTimeModifier { get; }
            public bool PendingCritAdvantage { get; }
            public StatType? LastStatUsed { get; }
            public StatType[]? ShadowDisadvantagedStats { get; }
            public string[]? CurrentShadowThresholds { get; }
            public bool CurrentHasAdvantage { get; }
            public bool CurrentHasDisadvantage { get; }
            public int[]? InjectedNextPool { get; }
            public int SpeculativeWasteCounter { get; }
            public string ShadowGrowthEvaluator { get; }
            public int NextSteeringDraw { get; }
        }

        private sealed class FailureInstructionProvider : IStatDeliveryInstructionProvider
        {
            public string? GetHorninessOverlayInstruction(FailureTier tier) => null;
            public string? GetStatFailureInstruction(StatType stat, FailureTier tier) => "Make the failure obvious.";
            public string? GetShadowCorruptionInstruction(ShadowStatType shadow, FailureTier tier) => null;
        }

        private sealed class NegativeSuccessDeltaRuleResolver : IRuleResolver
        {
            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => -1;
            public InterestState? GetInterestState(int interest) => null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public int? GetMomentumBonus(int streak) => null;
            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => null;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => null;
            public int? GetSuccessBaseXp(int dc) => null;
            public SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => null;
            public int? GetFlatXpAward(string awardType) => null;
            public int? GetXpThresholdForLevel(int level) => null;
            public int? GetLevelRollBonus(int level) => 0;
            public int? GetBuildPointsForLevel(int level) => null;
            public int? GetItemSlotsForLevel(int level) => null;
            public int? GetFailurePoolTierMinLevel(string tierName) => null;
            public int? GetProgressionCurrencyPerXp() => 10;
            public bool AllowDefaultFallback => true;
        }

        private sealed class ScriptedLlmTransport : ILlmTransport
        {
            private readonly Dictionary<string, Queue<Func<string>>> _responses =
                new Dictionary<string, Queue<Func<string>>>(StringComparer.Ordinal);

            private const string ValidDirectorJson =
                "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"relief\",\"intensity\":\"moderate and steadily rising\",\"underlying_feeling\":\"fear of being dismissed\",\"interpretation\":\"reads the message as specific warmth that is probably meant for them\",\"impulse\":\"leans in with a careful question\",\"restraint\":\"keeps the reply tentative but available\",\"response_posture\":\"Writing from relief, turns warmer while still checking sincerity\"}";

            public void Queue(string phase, string response)
            {
                Enqueue(phase, () => response);
            }

            public void QueueThrow(string phase, Exception exception)
            {
                Enqueue(phase, () => throw exception);
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                string key = phase ?? LlmPhase.Unknown;
                if (!_responses.TryGetValue(key, out var queue) || queue.Count == 0)
                {
                    if (string.Equals(key, LlmPhase.EmotionalDirector, StringComparison.Ordinal))
                    {
                        return Task.FromResult(ValidDirectorJson);
                    }

                    throw new InvalidOperationException($"No scripted LLM response for phase '{key}'.");
                }

                string response = queue.Dequeue().Invoke();
                if (string.Equals(key, LlmPhase.DialogueOptions, StringComparison.Ordinal) &&
                    response.IndexOf("OPTION_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    response = RewriteDialogueOptionStats(response, systemPrompt, userMessage);
                }

                return Task.FromResult(response);
            }

            private void Enqueue(string phase, Func<string> response)
            {
                if (!_responses.TryGetValue(phase, out var queue))
                {
                    queue = new Queue<Func<string>>();
                    _responses.Add(phase, queue);
                }

                queue.Enqueue(response);
            }

            private static string RewriteDialogueOptionStats(string response, string systemPrompt, string userMessage)
            {
                var statsInPrompt = new List<string>();
                string combinedPrompt = (userMessage ?? string.Empty) + "\n" + (systemPrompt ?? string.Empty);
                const string marker = "Be tagged with one of the available stats for this turn:";
                int markerIdx = combinedPrompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIdx < 0)
                    return response;

                string statsLine = combinedPrompt.Substring(markerIdx + marker.Length);
                int endLineIdx = statsLine.IndexOf('\n');
                if (endLineIdx >= 0)
                    statsLine = statsLine.Substring(0, endLineIdx);

                foreach (Pinder.Core.Stats.StatType stat in Enum.GetValues(typeof(Pinder.Core.Stats.StatType)))
                {
                    string statName = stat == Pinder.Core.Stats.StatType.SelfAwareness
                        ? "SELF_AWARENESS"
                        : stat.ToString();
                    if (statsLine.IndexOf(statName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (stat == Pinder.Core.Stats.StatType.SelfAwareness &&
                         statsLine.IndexOf("SELFAWARENESS", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        statsInPrompt.Add(statName);
                    }
                }

                if (statsInPrompt.Count < 3)
                    return response;

                int idx = 0;
                return System.Text.RegularExpressions.Regex.Replace(
                    response,
                    @"\[STAT:\s*\w+\]",
                    match => idx < statsInPrompt.Count
                        ? $"[STAT: {statsInPrompt[idx++].ToUpperInvariant()}]"
                        : match.Value,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
        }
    }
}

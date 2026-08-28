using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Pinder.SessionRunner.Snapshot;
using Xunit;

namespace Pinder.Core.Tests.Conversation;

public sealed class Issue1425_DateeResponsePlanTests
{
    private static readonly Guid PlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DateeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Contract_RoundTripsCanonicalJsonAndRejectsUnknownOrMissingFields()
    {
        DateeResponsePlan plan = Compile("INTIMATE_BREAKTHROUGH", "guarded").Plan!;

        string json = DateeResponsePlanJson.Serialize(plan);
        DateeResponsePlan parsed = DateeResponsePlanJson.ParseStrict(json);

        Assert.Equal(DateeResponsePlan.CurrentSchemaVersion, parsed.SchemaVersion);
        Assert.Equal(json, DateeResponsePlanJson.Serialize(parsed));
        Assert.Throws<DateeResponsePlanContractException>(() =>
            DateeResponsePlanJson.ParseStrict(json.Insert(1, "\"unknown\":true,")));
        Assert.Throws<DateeResponsePlanContractException>(() =>
            DateeResponsePlanJson.ParseStrict(json.Replace("\"activation\":4,", string.Empty)));
    }

    [Theory]
    [InlineData("CURATED_BUFFER", 0, DateeResponseDisclosure.Voluntary, DateeResponseMovement.Hold)]
    [InlineData("DEFENSIVE_EVASION", -7, DateeResponseDisclosure.Voluntary, DateeResponseMovement.Withdraw)]
    [InlineData("INTIMATE_BREAKTHROUGH", 7, DateeResponseDisclosure.Voluntary, DateeResponseMovement.Open)]
    [InlineData("TRAUMATIC_LEAKAGE", -7, DateeResponseDisclosure.Involuntary, DateeResponseMovement.Withdraw)]
    public void Compiler_MapsEveryAuthoredTransitionManner(
        string manner,
        int interestDelta,
        DateeResponseDisclosure disclosure,
        DateeResponseMovement movement)
    {
        DateeResponsePlanCompilationResult result = Compile(manner, "guarded", interestDelta: interestDelta);

        Assert.Equal(DateeResponsePlanCompilationOutcome.Accepted, result.Outcome);
        Assert.Equal(disclosure, result.Plan!.Disclosure);
        Assert.Equal(movement, result.Plan.Movement);
        Assert.Contains(result.Plan.Sources, source => source.Kind == DateePlanSourceKind.RevelationTarget);
    }

    [Theory]
    [InlineData(7, "open", DateeResponseMovement.Open)]
    [InlineData(7, "controlled", DateeResponseMovement.Open)]
    [InlineData(7, "guarded", DateeResponseMovement.Open)]
    [InlineData(7, "numb", DateeResponseMovement.Open)]
    [InlineData(7, "dissociated", DateeResponseMovement.Open)]
    [InlineData(7, "anxious", DateeResponseMovement.Open)]
    [InlineData(7, "overwhelmed", DateeResponseMovement.Open)]
    [InlineData(7, "conflicted", DateeResponseMovement.Open)]
    [InlineData(-7, "open", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "controlled", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "guarded", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "numb", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "dissociated", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "anxious", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "overwhelmed", DateeResponseMovement.Withdraw)]
    [InlineData(-7, "conflicted", DateeResponseMovement.Withdraw)]
    [InlineData(0, "open", DateeResponseMovement.Hold)]
    [InlineData(0, "controlled", DateeResponseMovement.Hold)]
    [InlineData(0, "guarded", DateeResponseMovement.Hold)]
    [InlineData(0, "numb", DateeResponseMovement.Hold)]
    [InlineData(0, "dissociated", DateeResponseMovement.Hold)]
    [InlineData(0, "anxious", DateeResponseMovement.Hold)]
    [InlineData(0, "overwhelmed", DateeResponseMovement.Hold)]
    [InlineData(0, "conflicted", DateeResponseMovement.Hold)]
    public void Compiler_ResolvedInterestMovementOverridesEveryEmotionalPosture(
        int interestDelta,
        string regulatoryState,
        DateeResponseMovement expected)
    {
        DateeResponsePlanCompilationResult result = Compile(null, regulatoryState, interestDelta: interestDelta);

        Assert.NotEqual(DateeResponsePlanCompilationOutcome.Rejected, result.Outcome);
        Assert.Equal(expected, result.Plan!.Movement);
    }

    [Theory]
    [InlineData("CURATED_BUFFER", 7, false)]
    [InlineData("CURATED_BUFFER", 0, true)]
    [InlineData("CURATED_BUFFER", -7, false)]
    [InlineData("DEFENSIVE_EVASION", 7, false)]
    [InlineData("DEFENSIVE_EVASION", 0, false)]
    [InlineData("DEFENSIVE_EVASION", -7, true)]
    [InlineData("INTIMATE_BREAKTHROUGH", 7, true)]
    [InlineData("INTIMATE_BREAKTHROUGH", 0, false)]
    [InlineData("INTIMATE_BREAKTHROUGH", -7, false)]
    [InlineData("TRAUMATIC_LEAKAGE", 7, true)]
    [InlineData("TRAUMATIC_LEAKAGE", 0, true)]
    [InlineData("TRAUMATIC_LEAKAGE", -7, true)]
    public void Compiler_ProductionInterestAndMannerConflictMatrix(
        string manner,
        int interestDelta,
        bool accepted)
    {
        DateeResponsePlanCompilationResult result = Compile(manner, "guarded", interestDelta: interestDelta);

        Assert.Equal(accepted, result.Outcome == DateeResponsePlanCompilationOutcome.Accepted);
        if (accepted)
        {
            DateeResponseMovement expected = interestDelta > 0
                ? DateeResponseMovement.Open
                : interestDelta < 0
                    ? DateeResponseMovement.Withdraw
                    : DateeResponseMovement.Hold;
            Assert.Equal(expected, result.Plan!.Movement);
        }
        else
        {
            Assert.Equal(
                "datee_response_plan_incompatible.transition_manner.conflicts_with_interest_movement",
                result.Rejection!.Code);
        }
    }

    [Fact]
    public void Compiler_ReturnsCreativeAmbiguityOnlyForCompatibleChoices()
    {
        DateeResponsePlanCompilationResult result = Compile(manner: null, regulatoryState: "conflicted");

        Assert.Equal(DateeResponsePlanCompilationOutcome.CreativeAmbiguity, result.Outcome);
        Assert.Equal(new[] { DateeResponseMovement.Open }, result.AllowedMovements);
        Assert.Equal(
            new[] { DateeConversationalMove.Escalate, DateeConversationalMove.Tease },
            result.AllowedConversationalMoves);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public void Compiler_RejectsUnknownMannerWithSourceBeforeAnyReconciliation()
    {
        DateeResponsePlanCompilationResult result = Compile("HISTORIC_GENERIC", "controlled");

        Assert.Equal(DateeResponsePlanCompilationOutcome.Rejected, result.Outcome);
        Assert.Equal("datee_response_plan_incompatible.transition_manner.unknown", result.Rejection!.Code);
        Assert.Equal(Target("HISTORIC_GENERIC").SourceId, result.Rejection.SourceId);
    }

    [Fact]
    public void Compiler_RejectsTerminalOpenMovement()
    {
        DateeResponsePlanCompilationResult result = Compile(
            "INTIMATE_BREAKTHROUGH",
            "open",
            InterestState.Unmatched);

        Assert.Equal(DateeResponsePlanCompilationOutcome.Rejected, result.Outcome);
        Assert.Equal("datee_response_plan_incompatible.terminal_state_open_forbidden", result.Rejection!.Code);
        Assert.StartsWith("relationship-state:turn:", result.Rejection.SourceId, StringComparison.Ordinal);
    }

    [Fact]
    public void Compiler_RejectsTargetWhoseRolePolicyDecisionDidNotAdmitIt()
    {
        DateeReactionTarget target = Target("CURATED_BUFFER");
        RoleFactAccessDecision denied = RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            PlayerId,
            ConversationParticipantRole.PlayerAvatar,
            target.Fact));
        DateeResponsePlanInput input = Input(target, "controlled", InterestState.Interested, new[] { denied });

        DateeResponsePlanCompilationResult result = new DateeResponsePlanCompiler().Compile(input);

        Assert.Equal(DateeResponsePlanCompilationOutcome.Rejected, result.Outcome);
        Assert.Equal("datee_response_plan_incompatible.role_fact.not_admitted", result.Rejection!.Code);
        Assert.Equal(target.SourceId, result.Rejection.SourceId);
    }

    [Fact]
    public void Reconciler_CannotExpandCompilerOwnedValuesOrSources()
    {
        DateeResponsePlanCompilationResult ambiguity = Compile(null, "conflicted");
        DateeResponsePlan baseline = ambiguity.Plan!;
        var extraSources = baseline.Sources.Concat(new[]
        {
            new DateeResponsePlanSource("reconciler-invented", DateePlanSourceKind.Reconciliation),
        }).ToArray();
        var expanded = new DateeResponsePlan(
            baseline.VisibleEvidence,
            baseline.DateeInterpretation,
            baseline.Movement,
            baseline.MovementStages,
            baseline.PrimaryEmotion,
            baseline.SecondaryEmotion,
            baseline.RegulatoryState,
            baseline.Activation,
            baseline.Trajectory,
            baseline.Disclosure,
            baseline.ConversationalMove,
            baseline.DramaticArcSourceId,
            baseline.Constraints,
            extraSources);

        DateeResponsePlanContractException error = Assert.Throws<DateeResponsePlanContractException>(() =>
            new DateeResponsePlanCompiler().AcceptReconciled(ambiguity, expanded));
        Assert.Equal("datee_response_plan_incompatible.reconciliation.fixed_fields_changed", error.Code);
    }

    [Fact]
    public void MixedMovement_RequiresTwoDifferentStagesAndOneDisclosureOwner()
    {
        DateeResponsePlan valid = Plan(
            DateeResponseMovement.Mixed,
            DateeResponseDisclosure.Involuntary,
            new[]
            {
                new DateeResponseMovementStage(DateeResponseMovement.Hold, true),
                new DateeResponseMovementStage(DateeResponseMovement.Withdraw, false),
            });
        Assert.Equal(2, valid.MovementStages.Count);

        Assert.Throws<DateeResponsePlanContractException>(() => Plan(
            DateeResponseMovement.Mixed,
            DateeResponseDisclosure.Involuntary,
            new[]
            {
                new DateeResponseMovementStage(DateeResponseMovement.Hold, false),
                new DateeResponseMovementStage(DateeResponseMovement.Withdraw, false),
            }));
        Assert.Throws<DateeResponsePlanContractException>(() => Plan(
            DateeResponseMovement.Mixed,
            DateeResponseDisclosure.Involuntary,
            new[]
            {
                new DateeResponseMovementStage(DateeResponseMovement.Hold, true),
                new DateeResponseMovementStage(DateeResponseMovement.Hold, false),
            }));
    }

    [Fact]
    public void AcceptedPlan_SurvivesCloneSnapshotAndResimulationByteForByte()
    {
        DateeResponsePlan accepted = Compile("CURATED_BUFFER", "controlled", interestDelta: 0).Plan!;
        string expected = DateeResponsePlanJson.Serialize(accepted);
        AcceptedDateeResponsePlanState acceptedState = PlanState(accepted);
        var state = new GameSessionState
        {
            LastAcceptedDateeResponsePlan = accepted,
            LastAcceptedDateeResponsePlanState = acceptedState,
        };

        GameSessionState clone = state.Clone();
        var snapshot = new GameStateSnapshot(
            interest: 68,
            state: InterestState.Interested,
            momentumStreak: 1,
            activeTrapNames: Array.Empty<string>(),
            turnNumber: 8,
            lastAcceptedDateeResponsePlan: accepted,
            lastAcceptedDateeResponsePlanState: acceptedState);
        var resimulation = new ResimulateData(PlayerId, DateeId)
        {
            TargetInterest = 68,
            TurnNumber = 8,
            LastAcceptedDateeResponsePlan = snapshot.LastAcceptedDateeResponsePlan,
            LastAcceptedDateeResponsePlanState = snapshot.LastAcceptedDateeResponsePlanState,
        };
        var restored = new GameSessionState();

        restored.RestoreFromSnapshot(resimulation, new EmptyTrapRegistry());

        Assert.Equal(expected, DateeResponsePlanJson.Serialize(clone.LastAcceptedDateeResponsePlan!));
        Assert.Equal(expected, DateeResponsePlanJson.Serialize(snapshot.LastAcceptedDateeResponsePlan!));
        Assert.Equal(expected, DateeResponsePlanJson.Serialize(restored.LastAcceptedDateeResponsePlan!));
        Assert.Equal(expected, clone.LastAcceptedDateeResponsePlanState!.CanonicalPlanJson);
        Assert.Equal(expected, snapshot.LastAcceptedDateeResponsePlanState!.CanonicalPlanJson);
        Assert.Equal(expected, restored.LastAcceptedDateeResponsePlanState!.CanonicalPlanJson);
        Assert.Equal("accepted-artifact", restored.LastAcceptedDateeResponsePlanState.Provenance.AcceptedArtifactId);
        Assert.Equal(
            "agent-journal/game-run/datee/invocation/attempt/invocation",
            restored.LastAcceptedDateeResponsePlanState.Provenance.ReconciliationInvocationId);
        Assert.Equal(
            "agent-journal/game-run/datee/invocation/attempt/result",
            restored.LastAcceptedDateeResponsePlanState.Provenance.ReconciliationResultId);
    }

    [Fact]
    public void SessionRunnerSnapshot_RoundTripsPlanIdentityAndCompleteProvenance()
    {
        AcceptedDateeResponsePlanState acceptedState = PlanState(
            Compile("INTIMATE_BREAKTHROUGH", "guarded").Plan!);
        var replayState = new DateeResponseReplayState(
            responseTurn: acceptedState.OriginatingTurn,
            postTurnNumber: acceptedState.OriginatingTurn + 1,
            deliveredMessage: acceptedState.VisibleMessageText,
            acceptedDateeMessage: "Then ask me something real.",
            responseDelayMinutes: 5,
            interestBefore: 12,
            interestBeforeState: InterestState.Interested,
            interestAfterState: InterestState.Interested,
            deliveryTier: FailureTier.Success,
            rollStat: StatType.Honesty,
            outcomeIntensity: RollOutcomeIntensity.Strong,
            horninessOverlayApplied: false,
            horninessTier: FailureTier.Success,
            acceptedEmotionalDirection: new CharacterEmotionalDirectionSummary(
                acceptedState.OriginatingTurn,
                "desire",
                "fear",
                "guarded",
                4,
                "escalating",
                "move closer"),
            activeTrapIds: Array.Empty<string>(),
            activeTrapInstructions: Array.Empty<string>());
        var stateAfter = new GameStateSnapshot(
            interest: 19,
            state: InterestState.Interested,
            momentumStreak: 0,
            activeTrapNames: Array.Empty<string>(),
            turnNumber: 9,
            lastAcceptedDateeResponsePlan: acceptedState.Plan,
            lastAcceptedDateeResponsePlanState: acceptedState,
            lastDateeResponseReplayState: replayState);
        var result = new TurnResult(
            new RollResult(18, null, 18, StatType.Honesty, 2, 0, 12, FailureTier.Success),
            acceptedState.VisibleMessageText,
            "Then ask me something real.",
            narrativeBeat: null,
            interestDelta: 7,
            stateAfter,
            isGameOver: false,
            outcome: null);
        var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(2));

        TurnSnapshot persisted = Program.BuildTurnSnapshot(
            turnNumber: 9,
            result: result,
            shadows: shadows,
            statsUsedHistory: new List<StatType>(),
            highestPctHistory: new List<bool>(),
            charmUsageCount: 0,
            charmMadnessTriggered: false,
            saUsageCount: 0,
            saOverthinkingTriggered: false,
            rizzCumulativeFailureCount: 0,
            conversationHistory: new List<(string Sender, string Text)>(),
            comboHistory: new List<(StatType Stat, bool Succeeded)>(),
            activeTell: null);
        string json = JsonSerializer.Serialize(persisted);
        TurnSnapshot diskRoundTrip = JsonSerializer.Deserialize<TurnSnapshot>(json)!;
        ResimulateData restored = Program.BuildResimulateData(diskRoundTrip, PlayerId, DateeId);
        AcceptedDateeResponsePlanState restoredState = Assert.IsType<AcceptedDateeResponsePlanState>(
            restored.LastAcceptedDateeResponsePlanState);

        Assert.Equal(acceptedState.CanonicalPlanJson, restoredState.CanonicalPlanJson);
        Assert.Equal(acceptedState.OriginatingTurn, restoredState.OriginatingTurn);
        Assert.Equal(acceptedState.MessageReference, restoredState.MessageReference);
        Assert.Equal(acceptedState.VisibleMessageText, restoredState.VisibleMessageText);
        Assert.Equal(
            acceptedState.Provenance.SourceArtifactId,
            restoredState.Provenance.SourceArtifactId);
        Assert.Equal(
            acceptedState.Provenance.CompilerArtifactId,
            restoredState.Provenance.CompilerArtifactId);
        Assert.Equal(
            acceptedState.Provenance.AcceptedArtifactId,
            restoredState.Provenance.AcceptedArtifactId);
        Assert.Equal(
            acceptedState.Provenance.ReconciliationInvocationId,
            restoredState.Provenance.ReconciliationInvocationId);
        Assert.Equal(
            acceptedState.Provenance.ReconciliationResultId,
            restoredState.Provenance.ReconciliationResultId);
        Assert.Equal(
            Json(replayState),
            Json(restored.LastDateeResponseReplayState));
        Assert.Null(restored.DateeResponsePlanReplaySelection);
    }

    [Fact]
    public async Task ProductionTurnSnapshot_ResponseReplayReusesOriginatingPlanAndOrdinaryContinuationDoesNot()
    {
        CharacterProfile player = Profile("Gerald");
        CharacterProfile datee = Profile("Velvet");
        var productionTransport = new StageTransport(
            directorResponse: DirectorJson("controlled"),
            performanceResponses: new[] { ValidPerformanceJson() },
            supportsConversationSessions: true);
        var session = new GameSession(
            player,
            datee,
            Adapter(productionTransport),
            new FixedDice(),
            new EmptyTrapRegistry(),
            new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: new CloneableRandom(42),
                statDrawRng: new CloneableRandom(4242),
                agentJournalContext: JournalContext("production")));

        await session.StartTurnAsync();
        TurnResult productionResult = await session.ResolveTurnAsync(0);
        GameSessionState originalPostState = session.State.Clone();
        ResimulateData replaySnapshot = session.CreateDateeResponseResimulateData();
        ResimulateData continuationSnapshot = session.CreateResimulateData();
        AcceptedDateeResponsePlanState persisted = replaySnapshot.LastAcceptedDateeResponsePlanState!;
        Assert.Equal(0, persisted.OriginatingTurn);
        Assert.Equal(1, replaySnapshot.TurnNumber);
        Assert.Equal(productionResult.DeliveredMessage, persisted.VisibleMessageText);

        var replayTransport = new StageTransport(
            directorResponse: DirectorJson("SHOULD_NOT_RUN"),
            performanceResponses: new[] { ValidPerformanceJson() },
            supportsConversationSessions: true);
        var replaySession = new GameSession(
            player,
            datee,
            Adapter(replayTransport),
            new FixedDice(),
            new EmptyTrapRegistry(),
            new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: new CloneableRandom(42),
                statDrawRng: new CloneableRandom(4242),
                agentJournalContext: JournalContext("replay")));
        replaySession.RestoreState(replaySnapshot, new EmptyTrapRegistry());
        DateeResponseReplayResult replayResult = await replaySession.ReplayLastDateeResponseAsync();

        Assert.Equal(2, productionTransport.Count("emotional_director"));
        Assert.Equal(0, productionTransport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(1, productionTransport.Count(DateePerformanceStructuredContract.SchemaName));
        Assert.Equal(0, replayTransport.Count("emotional_director"));
        Assert.Equal(0, replayTransport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(1, replayTransport.Count(DateePerformanceStructuredContract.SchemaName));
        Assert.Equal(persisted.CanonicalPlanJson, ExtractPlanJson(
            replayTransport.Single(DateePerformanceStructuredContract.SchemaName).UserMessage));
        Assert.Equal(productionResult.DateeMessage, replayResult.DateeMessage);
        Assert.Equal(persisted.OriginatingTurn, replaySession.State.DateeEmotionalDirectionHistory.Single().Turn);
        Assert.Equal(2, originalPostState.DateeHistory.Count);
        Assert.Equal(2, originalPostState.AvatarHistory.Count);
        Assert.Equal(Json(originalPostState.DateeHistory), Json(replaySession.State.DateeHistory));
        Assert.Equal(Json(originalPostState.AvatarHistory), Json(replaySession.State.AvatarHistory));
        AssertEquivalent(originalPostState, replaySession.State);

        var continuationTransport = new StageTransport(
            directorResponse: DirectorJson("controlled"),
            performanceResponses: new[]
            {
                "{\"schema_version\":\"datee_performance.v1\",\"message\":\"Now tell me why that matters to you.\",\"signals\":{\"tell\":null,\"weakness\":null}}",
            },
            supportsConversationSessions: true);
        var continuationSession = new GameSession(
            player,
            datee,
            Adapter(continuationTransport),
            new FixedDice(),
            new EmptyTrapRegistry(),
            new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: new CloneableRandom(42),
                statDrawRng: new CloneableRandom(4242),
                agentJournalContext: JournalContext("continuation")));
        continuationSession.RestoreState(continuationSnapshot, new EmptyTrapRegistry());
        await continuationSession.StartTurnAsync();
        // The assertion here is plan-lifecycle isolation, not revelation-target
        // selection. Remove the independently selected target so a random
        // pre-roll manner cannot fail the otherwise ordinary follow-up turn.
        continuationSession.State.CurrentDateeReactionTarget = null;
        await continuationSession.ResolveTurnAsync(0);
        Assert.Equal(2, continuationTransport.Count("emotional_director"));
        Assert.Equal(1, continuationTransport.Count(DateePerformanceStructuredContract.SchemaName));
    }

    [Fact]
    public async Task RejectedCompile_IsTransactionallyInertAtProductionStageBoundary()
    {
        CharacterProfile player = Profile("Gerald");
        CharacterProfile datee = Profile("Velvet");
        GameSessionState state = State(19, 8);
        state.CurrentDateeReactionTarget = TargetFor(datee.CharacterId, "HISTORIC_GENERIC", 7);
        GameSessionState before = state.Clone();
        var transport = new StageTransport(
            directorResponse: DirectorJson("controlled"),
            performanceResponses: Array.Empty<string>());
        var stage = new DateeResponseStage(Adapter(transport));

        DateeResponsePlanContractException error = await Assert.ThrowsAsync<DateeResponsePlanContractException>(() =>
            stage.ExecuteAsync(
                state,
                Roll(12),
                Delivery("Tell me what you actually want."),
                player,
                datee,
                null,
                CancellationToken.None));

        Assert.Equal("datee_response_plan_incompatible.transition_manner.unknown", error.Code);
        AssertEquivalent(before, state);
        Assert.Equal(1, transport.Count("emotional_director"));
        Assert.Equal(0, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(0, transport.Count(DateePerformanceStructuredContract.SchemaName));
    }

    [Fact]
    public async Task RejectedReconciliation_IsTransactionallyInertAtProductionStageBoundary()
    {
        CharacterProfile player = Profile("Gerald");
        CharacterProfile datee = Profile("Velvet");
        GameSessionState state = State(19, 8);
        GameSessionState before = state.Clone();
        var transport = new StageTransport(
            directorResponse: DirectorJson("conflicted"),
            performanceResponses: Array.Empty<string>(),
            reconciliationResponses: new[] { "{}" });
        var stage = new DateeResponseStage(Adapter(transport));

        await Assert.ThrowsAsync<LlmContractException>(() => stage.ExecuteAsync(
            state,
            Roll(12),
            Delivery("Tell me what you actually want."),
            player,
            datee,
            null,
            CancellationToken.None));

        AssertEquivalent(before, state);
        Assert.Equal(1, transport.Count("emotional_director"));
        Assert.Equal(1, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(0, transport.Count(DateePerformanceStructuredContract.SchemaName));
    }

    [Fact]
    public async Task SuccessfulPerformance_SpendsTargetOnlyAfterPerformanceAcceptance()
    {
        CharacterProfile player = Profile("Gerald");
        CharacterProfile datee = Profile("Velvet");
        GameSessionState state = State(19, 8);
        state.CurrentDateeReactionTarget = TargetFor(datee.CharacterId, "INTIMATE_BREAKTHROUGH", 7);
        var transport = new StageTransport(
            directorResponse: DirectorJson("guarded"),
            performanceResponses: new[] { ValidPerformanceJson() });
        transport.OnRequest = request =>
        {
            if (request.SchemaName == DateePerformanceStructuredContract.SchemaName)
            {
                Assert.Empty(state.DateeSpentStakeIndices);
                Assert.Empty(state.DateeHistory);
            }
        };
        var stage = new DateeResponseStage(Adapter(transport));

        await stage.ExecuteAsync(
            state,
            Roll(12),
            Delivery("Tell me what you actually want."),
            player,
            datee,
            null,
            CancellationToken.None);

        Assert.Contains(7, state.DateeSpentStakeIndices);
        Assert.Equal(2, state.DateeHistory.Count);
        Assert.NotNull(state.LastAcceptedDateeResponsePlan);
        Assert.Equal(1, transport.Count(DateePerformanceStructuredContract.SchemaName));
    }

    private static DateeResponsePlanCompilationResult Compile(
        string? manner,
        string regulatoryState,
        InterestState afterState = InterestState.Interested,
        int interestDelta = 7)
    {
        DateeReactionTarget? target = manner == null ? null : Target(manner);
        RoleFactAccessDecision[] decisions = target == null
            ? Array.Empty<RoleFactAccessDecision>()
            : new[]
            {
                RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
                    DateeId,
                    ConversationParticipantRole.Datee,
                    target.Fact)),
            };
        return new DateeResponsePlanCompiler().Compile(Input(target, regulatoryState, afterState, decisions, interestDelta));
    }

    private static DateeResponsePlanInput Input(
        DateeReactionTarget? target,
        string regulatoryState,
        InterestState afterState,
        IReadOnlyList<RoleFactAccessDecision> decisions,
        int interestDelta = 7)
        => new DateeResponsePlanInput(
            new DateeVisibleEvidence(
                "Tell me what you actually want.",
                ConversationMessageReference.Create(8, ConversationParticipantRole.PlayerAvatar)),
            12,
            afterState == InterestState.Unmatched ? 0 : 12 + interestDelta,
            InterestState.Interested,
            afterState,
            Direction(regulatoryState),
            target,
            cognitivePressure: null,
            activeTrapIds: new[] { "fear-of-neediness" },
            archetypeId: "archetype:velvet",
            dramaticArcSourceId: "dramatic-arc:session-1",
            accessDecisions: decisions);

    private static CharacterEmotionalDirection Direction(string regulatoryState)
        => new CharacterEmotionalDirection(
            "desire",
            "fear",
            regulatoryState,
            4,
            "escalating",
            "being seen without losing control",
            "the invitation may be sincere",
            "move closer",
            "protect one vulnerable edge",
            "answer with active emotional movement");

    private static DateeReactionTarget Target(string manner)
    {
        var resolved = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.StakeRegistry,
            Field = "STAKE_LINE",
            Index = 2,
            Manner = manner,
            StemText = "Velvet hides tenderness behind theatrical confidence.",
            TransitionStyle = "authored-style-that-must-not-reach-performance",
        };
        return DateeReactionTarget.FromLegacyResolvedTarget(
            resolved,
            DateeId,
            DateeId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceIds.PsychologicalStake(DateeId, 2));
    }

    private static DateeResponsePlan Plan(
        DateeResponseMovement movement,
        DateeResponseDisclosure disclosure,
        IReadOnlyList<DateeResponseMovementStage> stages)
    {
        ConversationMessageReference message = ConversationMessageReference.Create(8, ConversationParticipantRole.PlayerAvatar);
        var sources = new[]
        {
            new DateeResponsePlanSource(message.Value, DateePlanSourceKind.VisibleMessage),
            new DateeResponsePlanSource("emotional:8", DateePlanSourceKind.EmotionalDirection),
            new DateeResponsePlanSource("target:traumatic-leakage", DateePlanSourceKind.RevelationTarget),
        };
        return new DateeResponsePlan(
            new DateeVisibleEvidence("Tell me what you actually want.", message),
            "Possibly, the invitation is sincere",
            movement,
            stages,
            "desire",
            "fear",
            "conflicted",
            4,
            "volatile",
            disclosure,
            DateeConversationalMove.Reveal,
            dramaticArcSourceId: null,
            new[]
            {
                new DateeResponsePlanConstraint("visible_evidence.canonical", DateePlanConstraintSeverity.Hard, message.Value),
                new DateeResponsePlanConstraint("disclosure.traumatic_leakage", DateePlanConstraintSeverity.Hard, "target:traumatic-leakage"),
            },
            sources);
    }

    private static CharacterProfile Profile(string name)
        => TestHelpers.MakeCharacterProfile(
            TestHelpers.MakeStatBlock(2),
            "You are " + name + ".",
            name,
            new TimingProfile(5, 0, 0, "neutral"),
            level: 1);

    private static GameSessionState State(int interest, int turn)
        => new GameSessionState
        {
            Interest = new InterestMeter(interest),
            TurnNumber = turn,
        };

    private static RollStageResult Roll(int interestBefore)
        => new RollStageResult
        {
            ResolveDice = new FixedDice(),
            InterestBefore = interestBefore,
            InterestAfter = 19,
            StateBefore = InterestState.Interested,
            RollResult = new RollResult(
                18,
                null,
                18,
                StatType.Honesty,
                2,
                0,
                12,
                FailureTier.Success),
        };

    private static DeliveryStageResult Delivery(string message)
        => new DeliveryStageResult
        {
            DeliveredMessage = message,
            HorninessCheckResult = HorninessCheckResult.NotPerformed,
        };

    private static DateeReactionTarget TargetFor(Guid ownerId, string manner, int index)
    {
        var resolved = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.StakeRegistry,
            Field = "STAKE_LINE",
            Index = index,
            Manner = manner,
            StemText = "Velvet keeps one private truth carefully contained.",
            TransitionStyle = "raw style",
        };
        return DateeReactionTarget.FromLegacyResolvedTarget(
            resolved,
            ownerId,
            ownerId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceIds.PsychologicalStake(ownerId, index));
    }

    private static PinderLlmAdapter Adapter(StageTransport transport)
        => new PinderLlmAdapter(
            transport,
            new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 0,
                ContractViolationBackoffMs = 1,
            });

    private static AcceptedDateeResponsePlanState PlanState(DateeResponsePlan plan)
        => AcceptedDateeResponsePlanState.Create(
            plan,
            new DateeResponsePlanProvenance(
                "source-artifact",
                "compiler-artifact",
                "accepted-artifact",
                "agent-journal/game-run/datee/invocation/attempt/invocation",
                "agent-journal/game-run/datee/invocation/attempt/result"));

    private static GameRunAgentJournalContext JournalContext(string suffix)
        => new GameRunAgentJournalContext(
            "run-1425-" + suffix,
            "datee-session-1425-" + suffix,
            requestId: "request-1425-" + suffix,
            branchId: "main");

    private static void AssertEquivalent(GameSessionState expected, GameSessionState actual)
    {
        IReadOnlyDictionary<string, string> expectedProjection = SemanticStateProjection(expected);
        IReadOnlyDictionary<string, string> actualProjection = SemanticStateProjection(actual);
        Assert.Equal(expectedProjection.Keys, actualProjection.Keys);
        string[] mismatches = expectedProjection.Keys
            .Where(key => !string.Equals(expectedProjection[key], actualProjection[key], StringComparison.Ordinal))
            .Select(key => $"'{key}'\nExpected: {expectedProjection[key]}\nActual:   {actualProjection[key]}")
            .ToArray();
        Assert.True(mismatches.Length == 0, "Semantic state mismatches:\n" + string.Join("\n", mismatches));
    }

    private static IReadOnlyDictionary<string, string> SemanticStateProjection(GameSessionState state)
    {
        // This is the complete mutable GameSessionState resolution surface. The
        // alias properties (Spent*, Previous*, CurrentResolvedTarget and
        // CurrentCognitiveSubtext) are represented by their authoritative role-
        // specific fields. Callbacks, RNG instances, transport diagnostics and
        // logs are deliberately excluded because they live outside
        // GameSessionState and are not semantic session state. Provider session
        // snapshots are also excluded: they contain fresh opaque IDs,
        // timestamps, and private journal diagnostics. Their semantic message
        // content is compared through DateeHistory and AvatarHistory above.
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["interest"] = state.Interest.Current + ":" + state.Interest.GetState(),
            ["traps"] = Json(state.Traps.AllActive.Select(trap => new
            {
                trap.Definition.Id,
                Stat = trap.Definition.Stat.ToString(),
                trap.TurnsRemaining,
            }).ToArray()),
            ["history"] = Json(state.History.Select(entry => new { entry.Sender, entry.Text }).ToArray()),
            ["datee_outfit"] = state.DateeOutfitDescription,
            ["datee_history"] = Json(state.DateeHistory),
            ["datee_emotional_history"] = Json(state.DateeEmotionalDirectionHistory),
            ["last_plan"] = state.LastAcceptedDateeResponsePlan == null
                ? "<null>"
                : DateeResponsePlanJson.Serialize(state.LastAcceptedDateeResponsePlan),
            ["last_plan_state"] = PlanStateFingerprint(state.LastAcceptedDateeResponsePlanState),
            ["last_response_replay_state"] = Json(state.LastDateeResponseReplayState),
            ["pending_plan_replay"] = Json(state.PendingDateeResponsePlanReplay),
            ["avatar_history"] = Json(state.AvatarHistory),
            ["avatar_spent_backstory"] = Json(state.AvatarSpentBackstoryIndices.OrderBy(value => value).ToArray()),
            ["avatar_spent_stake"] = Json(state.AvatarSpentStakeIndices.OrderBy(value => value).ToArray()),
            ["avatar_previous_phase"] = state.AvatarPreviousPhase ?? "<null>",
            ["avatar_previous_index"] = state.AvatarPreviousResolvedIndex.ToString(),
            ["avatar_target"] = Json(state.CurrentAvatarRevelationTarget),
            ["avatar_cognitive_text"] = state.CurrentAvatarCognitiveSubtext ?? "<null>",
            ["avatar_cognitive_fact"] = Json(state.CurrentAvatarCognitiveSubtextFact),
            ["datee_spent_backstory"] = Json(state.DateeSpentBackstoryIndices.OrderBy(value => value).ToArray()),
            ["datee_spent_stake"] = Json(state.DateeSpentStakeIndices.OrderBy(value => value).ToArray()),
            ["datee_previous_phase"] = state.DateePreviousPhase ?? "<null>",
            ["datee_previous_index"] = state.DateePreviousResolvedIndex.ToString(),
            ["datee_target"] = Json(state.CurrentDateeReactionTarget),
            ["legacy_target"] = Json(state.LegacyCurrentResolvedTarget),
            ["datee_cognitive_text"] = state.CurrentDateeCognitiveSubtext ?? "<null>",
            ["datee_cognitive_fact"] = Json(state.CurrentDateeCognitiveSubtextFact),
            ["player_shadows"] = ShadowFingerprint(state.PlayerShadows),
            ["datee_shadows"] = ShadowFingerprint(state.DateeShadows),
            ["combo"] = Json(state.ComboTracker.CreateSnapshot()),
            ["combo_triple"] = state.ComboTracker.HasTripleBonus.ToString(),
            ["topics"] = Json(state.Topics),
            ["rizz_failures"] = state.RizzCumulativeFailureCount.ToString(),
            ["momentum"] = state.MomentumStreak.ToString(),
            ["pending_momentum"] = state.PendingMomentumBonus.ToString(),
            ["turn"] = state.TurnNumber.ToString(),
            ["ended"] = state.Ended.ToString(),
            ["outcome"] = state.Outcome?.ToString() ?? "<null>",
            ["xp"] = XpLedgerFingerprint(state.XpLedger),
            ["weakness"] = Json(state.ActiveWeakness),
            ["tell"] = Json(state.ActiveTell),
            ["session_horniness"] = state.SessionHorniness.ToString(),
            ["horniness_roll"] = state.HorninessRoll.ToString(),
            ["horniness_modifier"] = state.HorninessTimeModifier.ToString(),
            ["pending_crit"] = state.PendingCritAdvantage.ToString(),
            ["last_stat"] = state.LastStatUsed?.ToString() ?? "<null>",
            ["shadow_disadvantaged"] = Json(state.ShadowDisadvantagedStats?.OrderBy(value => value).ToArray()),
            ["shadow_thresholds"] = Json(state.CurrentShadowThresholds?.OrderBy(entry => entry.Key).ToArray()),
            ["options"] = Json(state.CurrentOptions),
            ["has_advantage"] = state.CurrentHasAdvantage.ToString(),
            ["has_disadvantage"] = state.CurrentHasDisadvantage.ToString(),
            ["dice_pools"] = Json(state.CurrentDicePools?.Select(pool => pool.ToArray()).ToArray()),
            ["injected_pool"] = Json(state.InjectedNextPool?.ToArray()),
            ["speculative_waste"] = Json(new
            {
                state.SpeculativeWasteTracker.WasteThreshold,
                state.SpeculativeWasteTracker.RecoveryThreshold,
                state.SpeculativeWasteTracker.DiagnosticCounter,
                state.SpeculativeWasteTracker.ShouldRunParallel,
            }),
        };
    }

    private static string Json(object? value)
        => value == null ? "<null>" : AgentJournalJson.Serialize(value);

    private static string PlanStateFingerprint(AcceptedDateeResponsePlanState? state)
        => state == null
            ? "<null>"
            : Json(new
            {
                PlanStateSchemaVersion = state.SchemaVersion,
                state.CanonicalPlanJson,
                state.OriginatingTurn,
                state.MessageReference,
                state.VisibleMessageText,
                ProvenanceSchemaVersion = state.Provenance.SchemaVersion,
                state.Provenance.SourceArtifactId,
                state.Provenance.CompilerArtifactId,
                state.Provenance.AcceptedArtifactId,
                state.Provenance.ReconciliationInvocationId,
                state.Provenance.ReconciliationResultId,
            });

    private static string ShadowFingerprint(SessionShadowTracker? tracker)
        => tracker == null
            ? "<null>"
            : Json(Enum.GetValues(typeof(ShadowStatType))
                .Cast<ShadowStatType>()
                .Select(shadow => new
                {
                    Shadow = shadow.ToString(),
                    Effective = tracker.GetEffectiveShadow(shadow),
                    Delta = tracker.GetDelta(shadow),
                })
                .ToArray());

    private static string XpLedgerFingerprint(Pinder.Core.Progression.XpLedger ledger)
        => Json(new
        {
            ledger.TotalXp,
            Events = ledger.Events.Select(entry => new { entry.Source, entry.Amount }).ToArray(),
            DrainCursor = PrivateField(ledger, "_drainCursor"),
            ledger.TerminalSettlementOutcome,
            ledger.TerminalSettlementBaseXp,
            TerminalSettlementMultiplier = PrivateField(ledger, "_terminalSettlementMultiplier"),
            TerminalSettlementBonusXp = PrivateField(ledger, "_terminalSettlementBonusXp"),
        });

    private static object? PrivateField(object instance, string fieldName)
        => instance.GetType()
            .GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(instance);

    private static string DirectorJson(string regulatoryState)
        => "{\"schema_version\":\"emotional_director.v2\",\"primary_emotion\":\"desire\",\"secondary_emotion\":\"fear\",\"regulatory_state\":\""
            + regulatoryState
            + "\",\"activation\":4,\"trajectory\":\"escalating\",\"core_threat_or_desire\":\"being seen\",\"interpretation\":\"the invitation may be sincere\",\"impulse\":\"move closer\",\"restraint\":\"keep one edge\",\"response_posture\":\"respond actively\"}";

    private static string ValidPerformanceJson()
        => "{\"schema_version\":\"datee_performance.v1\",\"message\":\"Then ask me something real.\",\"signals\":{\"tell\":null,\"weakness\":null}}";

    private static string ExtractPlanJson(string prompt)
    {
        int marker = prompt.IndexOf("{\"schema_version\":\"datee_response_plan.v1\"", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        int end = prompt.IndexOf("\n</ENGINE_STATE>", marker, StringComparison.Ordinal);
        Assert.True(end > marker);
        return prompt.Substring(marker, end - marker);
    }

    private sealed class FixedDice : IDiceRoller
    {
        public int Roll(int sides) => Math.Min(10, sides);
    }

    private sealed class StageTransport : ILlmTransport, IConversationLlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport
    {
        private readonly string _directorResponse;
        private readonly Queue<string> _performanceResponses;
        private readonly Queue<string> _reconciliationResponses;
        private readonly bool _supportsConversationSessions;

        public StageTransport(
            string directorResponse,
            IReadOnlyList<string> performanceResponses,
            IReadOnlyList<string>? reconciliationResponses = null,
            bool supportsConversationSessions = false)
        {
            _directorResponse = directorResponse;
            _performanceResponses = new Queue<string>(performanceResponses);
            _reconciliationResponses = new Queue<string>(reconciliationResponses ?? Array.Empty<string>());
            _supportsConversationSessions = supportsConversationSessions;
        }

        public List<StructuredLlmRequest> Requests { get; } = new List<StructuredLlmRequest>();
        public Action<StructuredLlmRequest>? OnRequest { get; set; }
        public bool SupportsStructuredConversationMessages => true;
        public bool SupportsConversationMessages => _supportsConversationSessions;
        public int Count(string schemaName) => Requests.Count(request => request.SchemaName == schemaName);
        public StructuredLlmRequest Single(string schemaName) => Requests.Single(request => request.SchemaName == schemaName);

        public Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Structured transport required.");

        public Task<StructuredLlmResponse> SendStructuredAsync(
            StructuredLlmRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            OnRequest?.Invoke(request);
            string response = request.SchemaName == "emotional_director"
                ? _directorResponse
                : request.SchemaName == DialogueOptionsStructuredContract.SchemaName
                    ? ValidOptionsJson(request.UserMessage)
                : request.SchemaName == DateeResponsePlanStructuredContract.SchemaName
                    ? _reconciliationResponses.Dequeue()
                    : request.SchemaName == DateePerformanceStructuredContract.SchemaName
                        ? _performanceResponses.Dequeue()
                        : throw new InvalidOperationException("Unexpected schema " + request.SchemaName);
            return Task.FromResult(new StructuredLlmResponse(
                response,
                provider: "test",
                model: "issue-1425-stage",
                usedNativeStructuredOutput: true,
                validationMode: "native_schema"));
        }

        private static string ValidOptionsJson(string prompt)
        {
            const string marker = "Each stat must be one of: ";
            int start = prompt.LastIndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0);
            start += marker.Length;
            int end = prompt.IndexOf('.', start);
            string[] stats = prompt.Substring(start, end - start)
                .Split(',')
                .Select(value => value.Trim())
                .ToArray();
            Assert.Equal(3, stats.Length);
            return "{\"schema_version\":\"dialogue_options.v1\",\"options\":["
                + "{\"stat\":\"" + stats[0] + "\",\"text\":\"Tell me what you actually want.\",\"callback\":null,\"combo\":null},"
                + "{\"stat\":\"" + stats[1] + "\",\"text\":\"What surprised you today?\",\"callback\":null,\"combo\":null},"
                + "{\"stat\":\"" + stats[2] + "\",\"text\":\"Make one reckless confession.\",\"callback\":null,\"combo\":null}]}";
        }

        public Task<StructuredLlmResponse> SendStructuredConversationAsync(
            StructuredLlmRequest request,
            IReadOnlyList<ConversationMessage> priorMessages,
            CancellationToken cancellationToken = default)
            => SendStructuredAsync(request, cancellationToken);

        public Task<string> SendConversationAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> priorMessages,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Structured conversation transport required.");
    }

    private sealed class EmptyTrapRegistry : ITrapRegistry
    {
        public TrapDefinition? GetTrap(StatType stat) => null;
        public string? GetLlmInstruction(StatType stat) => null;
    }
}

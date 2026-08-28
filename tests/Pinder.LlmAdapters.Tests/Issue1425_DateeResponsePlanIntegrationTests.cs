using System;
using System.Collections.Generic;
using System.Linq;
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
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests;

public sealed class Issue1425_DateeResponsePlanIntegrationTests
{
    private static readonly Guid GeraldId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VelvetId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string SleepingBagSecret = "Gerald keeps a GBP 70 Soho silk sleeping bag hidden in plain sight.";
    private const string RawTransitionStyle = "RAW_TRANSITION_STYLE_MUST_NOT_REACH_PERFORMANCE";
    private const string RawCognitivePressure = "Velvet fears that direct desire will make her disposable.";

    [Fact]
    public async Task DeterministicTurn_AddsNoReconciliationAndRendersOnePlanOnlyBehaviorBlock()
    {
        var transport = new RecordingTransport(DirectorJson("controlled"), ValidPerformanceJson());
        var adapter = CreateAdapter(transport);

        DateeResponse response = await adapter.GetDateeResponseAsync(Context());

        Assert.Equal("Then ask me something real.", response.MessageText);
        Assert.Equal(1, transport.Count("emotional_director"));
        Assert.Equal(0, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(1, transport.Count(DateePerformanceStructuredContract.SchemaName));
        StructuredLlmRequest performance = transport.Single(DateePerformanceStructuredContract.SchemaName);
        Assert.Equal(1, Count(performance.UserMessage, "[ENGINE — DATEE RESPONSE PLAN]"));
        Assert.Contains("\"schema_version\":\"datee_response_plan.v1\"", performance.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("engine-state-cognitive-subtext-line", performance.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("engine-state-transition-style-line", performance.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("emotional-reaction-performance-direction", performance.UserMessage, StringComparison.Ordinal);
        Assert.Equal(DateePerformanceStructuredContract.SchemaVersion, performance.SchemaVersion);
    }

    [Fact]
    public async Task CreativeAmbiguity_UsesOnlyConfiguredBoundedReconciliationAndFailsClosed()
    {
        var transport = new RecordingTransport(
            DirectorJson("conflicted"),
            ValidPerformanceJson(),
            reconciliationResponses: new[] { "{}", "{}" });
        var adapter = CreateAdapter(transport, retries: 1);

        LlmContractException error = await Assert.ThrowsAsync<LlmContractException>(() =>
            adapter.GetDateeResponseAsync(Context()));

        Assert.Contains("datee_response_plan", error.Reason, StringComparison.Ordinal);
        Assert.Equal(1, transport.Count("emotional_director"));
        Assert.Equal(2, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(0, transport.Count(DateePerformanceStructuredContract.SchemaName));
        Assert.All(
            transport.Requests.Where(request => request.SchemaName == DateeResponsePlanStructuredContract.SchemaName),
            request => Assert.Contains("Candidate compiler plan:", request.UserMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreativeAmbiguity_AcceptedPlanLinksReconciliationWithoutModelSourceExpansion()
    {
        var transport = new RecordingTransport(
            DirectorJson("conflicted"),
            ValidPerformanceJson(),
            reconciliationResponses: new[] { "__candidate__" });
        var adapter = CreateAdapter(transport);

        await adapter.GetDateeResponseAsync(Context());

        Assert.Equal(1, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        StructuredLlmRequest performance = transport.Single(DateePerformanceStructuredContract.SchemaName);
        Assert.Contains(
            "datee-response-plan-reconciliation:turn:4:attempt:1",
            performance.UserMessage,
            StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"reconciliation\"", performance.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerformanceRetry_ReusesAcceptedPlanByteForByteWithoutRerunningUpstreamCalls()
    {
        var transport = new RecordingTransport(
            DirectorJson("controlled"),
            @"{""schema_version"":""datee_performance.v1"",""message"":""bad [SIGNALS]"",""signals"":{""tell"":null,""weakness"":null}}",
            ValidPerformanceJson());
        var adapter = CreateAdapter(transport, retries: 1);

        DateeResponse response = await adapter.GetDateeResponseAsync(Context());

        Assert.Equal("Then ask me something real.", response.MessageText);
        Assert.Equal(1, transport.Count("emotional_director"));
        Assert.Equal(0, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(2, transport.Count(DateePerformanceStructuredContract.SchemaName));
        string[] planJson = transport.Requests
            .Where(request => request.SchemaName == DateePerformanceStructuredContract.SchemaName)
            .Select(request => ExtractPlanJson(request.UserMessage))
            .ToArray();
        Assert.Equal(2, planJson.Length);
        Assert.Equal(planJson[0], planJson[1]);
    }

    [Fact]
    public async Task ResumedExecution_ReusesApplicableRestoredPlanAndSkipsDirectorAndReconciler()
    {
        DateeContext originalContext = Context();
        DateeResponsePlan accepted = Assert.IsType<DateeResponsePlan>(
            new DateeResponsePlanCompiler()
                .Compile(DateeResponsePlanInput.From(originalContext, Direction("controlled")))
                .Plan);
        string expected = DateeResponsePlanJson.Serialize(accepted);
        AcceptedDateeResponsePlanState acceptedState = PlanState(accepted);
        var transport = new RecordingTransport(DirectorJson("SHOULD_NOT_BE_CALLED"), ValidPerformanceJson());
        var adapter = CreateAdapter(transport);

        DateeResponse response = await adapter.GetDateeResponseAsync(Context(acceptedPlanState: acceptedState));

        Assert.Equal("Then ask me something real.", response.MessageText);
        Assert.Equal(0, transport.Count("emotional_director"));
        Assert.Equal(0, transport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(1, transport.Count(DateePerformanceStructuredContract.SchemaName));
        Assert.Equal(expected, ExtractPlanJson(transport.Single(DateePerformanceStructuredContract.SchemaName).UserMessage));
        Assert.Equal(expected, DateeResponsePlanJson.Serialize(response.EmotionalReactionDebug!.ResponsePlan!));
    }

    [Fact]
    public async Task PromptGolden_TargetAndPressureAppearOnlyInsideAcceptedPlanNotAsLegacyDirectives()
    {
        var transport = new RecordingTransport(DirectorJson("guarded"), ValidPerformanceJson());
        var adapter = CreateAdapter(transport);

        await adapter.GetDateeResponseAsync(Context(withDateeFacts: true));

        string prompt = transport.Single(DateePerformanceStructuredContract.SchemaName).UserMessage;
        Assert.Contains("\"disclosure\":\"voluntary\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"movement\":\"open\"", prompt, StringComparison.Ordinal);
        Assert.Contains(RawCognitivePressure, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(RawTransitionStyle, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Response posture:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Cognitive subtext:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Transition style:", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void GeraldPrivateFact_IsRejectedAtContextBoundaryBeforeAnyProviderCall()
    {
        var transport = new RecordingTransport(DirectorJson("controlled"), ValidPerformanceJson());
        var privatePlayerFact = new OwnedPromptFactV1(
            GeraldId,
            ConversationParticipantRole.PlayerAvatar,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceKind.CognitiveSubtext,
            PromptFactSourceIds.CognitiveSubtext(GeraldId, 4),
            SleepingBagSecret);

        RoleFactAccessDeniedException error = Assert.Throws<RoleFactAccessDeniedException>(() =>
            Context(cognitiveFact: privatePlayerFact));

        Assert.Equal("prompt_fact.access_denied", error.Code);
        Assert.Equal("denied.private_to_subject", error.Decision.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void GeraldVelvetRoleMatrix_AdmitsOnlySubjectOwnedPrivateFacts()
    {
        OwnedPromptFactV1 gerald = PrivateFact(
            GeraldId,
            ConversationParticipantRole.PlayerAvatar,
            SleepingBagSecret);
        OwnedPromptFactV1 velvet = PrivateFact(
            VelvetId,
            ConversationParticipantRole.Datee,
            RawCognitivePressure);

        Assert.True(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            GeraldId, ConversationParticipantRole.PlayerAvatar, gerald)).Admitted);
        Assert.False(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            VelvetId, ConversationParticipantRole.Datee, gerald)).Admitted);
        Assert.True(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            VelvetId, ConversationParticipantRole.Datee, velvet)).Admitted);
        Assert.False(RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
            GeraldId, ConversationParticipantRole.PlayerAvatar, velvet)).Admitted);
    }

    [Fact]
    public async Task GeraldVelvetProductionBoundary_ExcludesSleepingBagFromPlanReconciliationPerformanceAndSemanticHistory()
    {
        var records = new List<AgentJournalSinkRecord>();
        var transport = new RecordingTransport(
            DirectorJson("conflicted"),
            ValidPerformanceJson(),
            reconciliationResponses: new[] { "__candidate__" });
        var adapter = CreateAdapter(transport, sink: new RecordingSink(records));
        CharacterProfile gerald = TestHelpers.MakeCharacterProfile(
            TestHelpers.MakeStatBlock(2),
            "Gerald private system profile. " + SleepingBagSecret,
            "Gerald",
            new TimingProfile(5, 0, 0, "neutral"),
            level: 1,
            bio: "Public Gerald bio without private purchases.");
        CharacterProfile velvet = TestHelpers.MakeCharacterProfile(
            TestHelpers.MakeStatBlock(2),
            "Velvet private character system prompt.",
            "Velvet",
            new TimingProfile(5, 0, 0, "neutral"),
            level: 1,
            bio: "Public Velvet bio.");
        var session = new GameSession(
            gerald,
            velvet,
            adapter,
            new ProductionDice(),
            new EmptyTrapRegistry(),
            new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: new CloneableRandom(1425),
                statDrawRng: new CloneableRandom(2514),
                agentJournalContext: new GameRunAgentJournalContext(
                    "run-1425-production",
                    "datee-session-1425-production",
                    requestId: "request-1425-production",
                    branchId: "main")));

        await session.StartTurnAsync();
        TurnResult result = await session.ResolveTurnAsync(0);

        AgentJournalDateeResponsePlanRecord[] plans = records
            .Select(record => record.Record)
            .OfType<AgentJournalDateeResponsePlanRecord>()
            .ToArray();
        LlmInvocationRecord reconciliation = records
            .Select(record => record.Record)
            .OfType<LlmInvocationRecord>()
            .Single(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateeResponsePlanReconciliation);
        LlmInvocationRecord performance = records
            .Select(record => record.Record)
            .OfType<LlmInvocationRecord>()
            .Single(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance);

        Assert.Contains(SleepingBagSecret, gerald.AssembledSystemPrompt, StringComparison.Ordinal);
        Assert.All(plans, plan => Assert.DoesNotContain(SleepingBagSecret, plan.PayloadJson, StringComparison.Ordinal));
        Assert.All(reconciliation.InputDocuments, document =>
            Assert.DoesNotContain(SleepingBagSecret, document.Text, StringComparison.Ordinal));
        Assert.All(performance.InputDocuments, document =>
            Assert.DoesNotContain(SleepingBagSecret, document.Text, StringComparison.Ordinal));
        Assert.Contains(
            performance.InputDocuments,
            document => document.Text.Contains(gerald.Bio, StringComparison.Ordinal));
        Assert.DoesNotContain(result.StateAfter.DateeHistory, message =>
            message.Content.Contains(SleepingBagSecret, StringComparison.Ordinal)
            || message.Content.Contains("datee_response_plan", StringComparison.Ordinal));
        Assert.DoesNotContain(result.StateAfter.AvatarHistory, message =>
            message.Content.Contains(SleepingBagSecret, StringComparison.Ordinal)
            || message.Content.Contains("datee_response_plan", StringComparison.Ordinal));
        Assert.DoesNotContain(session.ConversationHistory, message =>
            message.Text.Contains(SleepingBagSecret, StringComparison.Ordinal)
            || message.Text.Contains("datee_response_plan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Journal_StoresDistinctPrivateArtifactsAndLinksPerformanceWithoutAddingChatEntries()
    {
        var records = new List<AgentJournalSinkRecord>();
        var transport = new RecordingTransport(DirectorJson("controlled"), ValidPerformanceJson());
        var adapter = CreateAdapter(transport, sink: new RecordingSink(records));

        StatefulDateeResult result = await adapter.GetDateeResponseAsync(
            Context(journal: true),
            Array.Empty<ConversationMessage>(),
            Array.Empty<ConversationMessage>(),
            null,
            null);

        AgentJournalDateeResponsePlanRecord[] artifacts = records
            .Where(record => record.CustomType == AgentJournalSchemaNames.DateeResponsePlanV1)
            .Select(record => Assert.IsType<AgentJournalDateeResponsePlanRecord>(record.Record))
            .ToArray();
        Assert.Collection(
            artifacts,
            source => Assert.Equal(AgentJournalDateeResponsePlanArtifactKind.SourceInput, source.ArtifactKind),
            compiler => Assert.Equal(AgentJournalDateeResponsePlanArtifactKind.CompilerOutcome, compiler.ArtifactKind),
            accepted => Assert.Equal(AgentJournalDateeResponsePlanArtifactKind.AcceptedPlan, accepted.ArtifactKind));
        Assert.Equal(artifacts[0].ArtifactId, artifacts[1].ParentArtifactId);
        Assert.Equal(artifacts[1].ArtifactId, artifacts[2].ParentArtifactId);

        LlmInvocationRecord performance = records
            .Where(record => record.CustomType == AgentJournalSchemaNames.LlmInvocationV1)
            .Select(record => record.Record)
            .OfType<LlmInvocationRecord>()
            .Single(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance);
        Assert.Equal(
            artifacts[2].ArtifactId,
            performance.Correlation.Context!["response_plan_accepted_artifact_id"]);
        Assert.Collection(
            result.NewHistoryEntries,
            player => Assert.Equal("Tell me what you actually want.", player.Content),
            datee => Assert.Equal("Then ask me something real.", datee.Content));
        Assert.DoesNotContain(
            result.NewHistoryEntries,
            message => message.Content.Contains("datee_response_plan", StringComparison.Ordinal)
                || message.Content.Contains(SleepingBagSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReconciledJournal_LinksCompletePrivatePlanAndPerformanceLifecycle()
    {
        var records = new List<AgentJournalSinkRecord>();
        var transport = new RecordingTransport(
            DirectorJson("conflicted"),
            ValidPerformanceJson(),
            reconciliationResponses: new[] { "__candidate__" });
        var adapter = CreateAdapter(transport, sink: new RecordingSink(records));

        StatefulDateeResult result = await adapter.GetDateeResponseAsync(
            Context(journal: true),
            Array.Empty<ConversationMessage>(),
            Array.Empty<ConversationMessage>(),
            null,
            null);

        AgentJournalDateeResponsePlanRecord[] artifacts = records
            .Select(record => record.Record)
            .OfType<AgentJournalDateeResponsePlanRecord>()
            .ToArray();
        Assert.Equal(3, artifacts.Length);
        AgentJournalDateeResponsePlanRecord source = artifacts.Single(record =>
            record.ArtifactKind == AgentJournalDateeResponsePlanArtifactKind.SourceInput);
        AgentJournalDateeResponsePlanRecord compiler = artifacts.Single(record =>
            record.ArtifactKind == AgentJournalDateeResponsePlanArtifactKind.CompilerOutcome);
        AgentJournalDateeResponsePlanRecord accepted = artifacts.Single(record =>
            record.ArtifactKind == AgentJournalDateeResponsePlanArtifactKind.AcceptedPlan);
        AgentJournalSinkRecord reconciliationInvocationSink = records.Single(record =>
            record.Record is LlmInvocationRecord invocation
            && invocation.Correlation.OperationId == GameRunConversationJournalInventory.DateeResponsePlanReconciliation);
        LlmInvocationRecord reconciliationInvocation = Assert.IsType<LlmInvocationRecord>(reconciliationInvocationSink.Record);
        AgentJournalSinkRecord reconciliationResultSink = records.Single(record =>
            record.Record is LlmResultRecord resultRecord
            && resultRecord.Correlation.OperationId == GameRunConversationJournalInventory.DateeResponsePlanReconciliation);
        LlmResultRecord reconciliationResult = Assert.IsType<LlmResultRecord>(reconciliationResultSink.Record);
        LlmInvocationRecord performanceInvocation = records
            .Select(record => record.Record)
            .OfType<LlmInvocationRecord>()
            .Single(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance);

        Assert.Equal(source.ArtifactId, compiler.ParentArtifactId);
        Assert.Equal(compiler.ArtifactId, accepted.ParentArtifactId);
        Assert.Equal(reconciliationInvocation.Correlation.InvocationId, reconciliationResult.Correlation.InvocationId);
        Assert.Equal(reconciliationInvocationSink.RecordId, accepted.ReconciliationInvocationId);
        Assert.Equal(reconciliationResultSink.RecordId, accepted.ReconciliationResultId);
        Assert.Equal(
            source.ArtifactId,
            reconciliationInvocation.Correlation.Context!["response_plan_source_artifact_id"]);
        Assert.Equal(
            compiler.ArtifactId,
            reconciliationInvocation.Correlation.Context!["response_plan_compiler_artifact_id"]);
        Assert.Equal(
            accepted.ArtifactId,
            performanceInvocation.Correlation.Context!["response_plan_accepted_artifact_id"]);
        Assert.Equal(AgentJournalTerminalStatus.Succeeded, reconciliationResult.TerminalStatus);
        Assert.Collection(
            result.NewHistoryEntries,
            player => Assert.Equal("Tell me what you actually want.", player.Content),
            datee => Assert.Equal("Then ask me something real.", datee.Content));
        Assert.DoesNotContain(result.NewHistoryEntries, message =>
            message.Content.Contains("datee_response_plan", StringComparison.Ordinal)
            || message.Content.Contains("reconciliation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoredReconciledPlan_JournalsCompleteOriginalChainAndReuseEvent()
    {
        var originalRecords = new List<AgentJournalSinkRecord>();
        var originalTransport = new RecordingTransport(
            DirectorJson("conflicted"),
            ValidPerformanceJson(),
            reconciliationResponses: new[] { "__candidate__" });
        DateeResponse original = await CreateAdapter(
                originalTransport,
                sink: new RecordingSink(originalRecords))
            .GetDateeResponseAsync(Context(journal: true));
        AcceptedDateeResponsePlanState state = Assert.IsType<AcceptedDateeResponsePlanState>(
            original.EmotionalReactionDebug!.ResponsePlanState);

        var replayRecords = new List<AgentJournalSinkRecord>();
        var replayTransport = new RecordingTransport(
            DirectorJson("SHOULD_NOT_RUN"),
            ValidPerformanceJson());
        DateeResponse replayed = await CreateAdapter(
                replayTransport,
                sink: new RecordingSink(replayRecords))
            .GetDateeResponseAsync(Context(journal: true, acceptedPlanState: state));

        Assert.Equal(0, replayTransport.Count("emotional_director"));
        Assert.Equal(0, replayTransport.Count(DateeResponsePlanStructuredContract.SchemaName));
        Assert.Equal(1, replayTransport.Count(DateePerformanceStructuredContract.SchemaName));
        AgentJournalDateeResponsePlanRecord reuse = replayRecords
            .Select(record => record.Record)
            .OfType<AgentJournalDateeResponsePlanRecord>()
            .Single(record => record.ArtifactKind == AgentJournalDateeResponsePlanArtifactKind.ReuseEvent);
        Assert.Equal(state.Provenance.AcceptedArtifactId, reuse.ParentArtifactId);
        Assert.Equal(state.Provenance.ReconciliationInvocationId, reuse.ReconciliationInvocationId);
        Assert.Equal(state.Provenance.ReconciliationResultId, reuse.ReconciliationResultId);
        LlmInvocationRecord performance = replayRecords
            .Select(record => record.Record)
            .OfType<LlmInvocationRecord>()
            .Single(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance);
        IReadOnlyDictionary<string, string> links = performance.Correlation.Context!;
        Assert.Equal(state.Provenance.SourceArtifactId, links["response_plan_source_artifact_id"]);
        Assert.Equal(state.Provenance.CompilerArtifactId, links["response_plan_compiler_artifact_id"]);
        Assert.Equal(state.Provenance.AcceptedArtifactId, links["response_plan_accepted_artifact_id"]);
        Assert.Equal(state.Provenance.ReconciliationInvocationId, links["response_plan_reconciliation_invocation_id"]);
        Assert.Equal(state.Provenance.ReconciliationResultId, links["response_plan_reconciliation_result_id"]);
        Assert.Equal(reuse.ArtifactId, links["response_plan_reuse_artifact_id"]);
        Assert.Equal("true", links["response_plan_reused"]);
        Assert.Equal(state.CanonicalPlanJson, DateeResponsePlanJson.Serialize(replayed.EmotionalReactionDebug!.ResponsePlan!));
    }

    private static PinderLlmAdapter CreateAdapter(
        RecordingTransport transport,
        int retries = 0,
        IAgentJournalSink? sink = null)
        => new PinderLlmAdapter(
            transport,
            new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = retries,
                ContractViolationBackoffMs = 1,
                AgentJournalHostSink = sink,
            });

    private static DateeContext Context(
        bool withDateeFacts = false,
        bool journal = false,
        OwnedPromptFactV1? cognitiveFact = null,
        AcceptedDateeResponsePlanState? acceptedPlanState = null,
        PublicProfileCard? playerAvatarCard = null,
        string? dateePrompt = null)
    {
        DateeReactionTarget? target = withDateeFacts ? Target() : null;
        if (withDateeFacts)
        {
            cognitiveFact = new OwnedPromptFactV1(
                VelvetId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(VelvetId, 4),
                RawCognitivePressure);
        }

        return new DateeContext(
            dateePrompt: dateePrompt ?? "Velvet's private character system prompt.",
            conversationHistory: new List<(string, string)> { ("Gerald", "Earlier visible line."), ("Velvet", "Earlier reply.") },
            dateeLastMessage: "Earlier reply.",
            activeTraps: Array.Empty<string>(),
            currentInterest: 68,
            playerDeliveredMessage: "Tell me what you actually want.",
            interestBefore: 61,
            interestAfter: 68,
            responseDelayMinutes: 1.0,
            playerName: "Gerald",
            dateeName: "Velvet",
            currentTurn: 4,
            interestBeforeState: InterestState.Interested,
            interestAfterState: InterestState.Interested,
            emotionalTurnEvent: new DateeEmotionalTurnEvent(
                StatType.Honesty,
                RollOutcomeIntensity.Strong,
                TestHelpers.MakePsychiatricDiagnosis()),
            agentJournalContext: journal
                ? new GameRunAgentJournalContext(
                    "run-1425",
                    "datee-session-1425",
                    requestId: "request-1425",
                    branchId: "main")
                : null,
            dateeReactionTarget: target,
            cognitiveSubtextFact: cognitiveFact,
            playerAvatarCard: playerAvatarCard,
            recipientCharacterId: target != null || cognitiveFact != null ? VelvetId : (Guid?)null,
            dramaticArcSourceId: "dramatic-arc:run-1425",
            acceptedDateeResponsePlanState: acceptedPlanState);
    }

    private static AcceptedDateeResponsePlanState PlanState(DateeResponsePlan plan)
        => AcceptedDateeResponsePlanState.Create(
            plan,
            new DateeResponsePlanProvenance(
                "source-artifact",
                "compiler-artifact",
                "accepted-artifact"));

    private static OwnedPromptFactV1 PrivateFact(
        Guid subjectId,
        ConversationParticipantRole role,
        string text)
        => new OwnedPromptFactV1(
            subjectId,
            role,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceKind.CognitiveSubtext,
            PromptFactSourceIds.CognitiveSubtext(subjectId, 4),
            text);

    private static DateeReactionTarget Target()
    {
        var resolved = new ResolvedRevelationTarget
        {
            Registry = EmotionStemSelectionRules.StakeRegistry,
            Field = "STAKE_LINE",
            Index = 2,
            Manner = "INTIMATE_BREAKTHROUGH",
            StemText = "Velvet hides tenderness behind theatrical confidence.",
            TransitionStyle = RawTransitionStyle,
        };
        return DateeReactionTarget.FromLegacyResolvedTarget(
            resolved,
            VelvetId,
            VelvetId,
            ConversationParticipantRole.Datee,
            PromptFactVisibility.PrivateToSubject,
            PromptFactSourceIds.PsychologicalStake(VelvetId, 2));
    }

    private static string DirectorJson(string regulatoryState)
        => "{\"schema_version\":\"emotional_director.v2\",\"primary_emotion\":\"desire\",\"secondary_emotion\":\"fear\",\"regulatory_state\":\""
            + regulatoryState
            + "\",\"activation\":4,\"trajectory\":\"escalating\",\"core_threat_or_desire\":\"being seen without losing control\",\"interpretation\":\"the invitation may be sincere\",\"impulse\":\"move closer\",\"restraint\":\"protect one vulnerable edge\",\"response_posture\":\"ACTIVE_POSTURE_SENTINEL\"}";

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

    private static string ValidPerformanceJson()
        => "{\"schema_version\":\"datee_performance.v1\",\"message\":\"Then ask me something real.\",\"signals\":{\"tell\":null,\"weakness\":null}}";

    private static int Count(string value, string needle)
        => value.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

    private static string ExtractPlanJson(string prompt)
    {
        int marker = prompt.IndexOf("{\"schema_version\":\"datee_response_plan.v1\"", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        int end = prompt.IndexOf("\n</ENGINE_STATE>", marker, StringComparison.Ordinal);
        Assert.True(end > marker);
        return prompt.Substring(marker, end - marker);
    }

    private sealed class RecordingTransport : ILlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport
    {
        private readonly string _directorResponse;
        private readonly Queue<string> _performanceResponses;
        private readonly Queue<string> _reconciliationResponses;

        public RecordingTransport(
            string directorResponse,
            params string[] performanceResponses)
            : this(directorResponse, performanceResponses, Array.Empty<string>())
        {
        }

        public RecordingTransport(
            string directorResponse,
            string performanceResponse,
            IReadOnlyList<string> reconciliationResponses)
            : this(directorResponse, new[] { performanceResponse }, reconciliationResponses)
        {
        }

        private RecordingTransport(
            string directorResponse,
            IReadOnlyList<string> performanceResponses,
            IReadOnlyList<string> reconciliationResponses)
        {
            _directorResponse = directorResponse;
            _performanceResponses = new Queue<string>(performanceResponses);
            _reconciliationResponses = new Queue<string>(reconciliationResponses);
        }

        public List<StructuredLlmRequest> Requests { get; } = new List<StructuredLlmRequest>();
        public bool SupportsStructuredConversationMessages => true;

        public int Count(string schemaName) => Requests.Count(request => request.SchemaName == schemaName);
        public StructuredLlmRequest Single(string schemaName) => Requests.Single(request => request.SchemaName == schemaName);

        public Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Issue #1425 requires structured provider calls.");

        public Task<StructuredLlmResponse> SendStructuredAsync(
            StructuredLlmRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            string json;
            if (request.SchemaName == "emotional_director")
                json = _directorResponse;
            else if (request.SchemaName == DialogueOptionsStructuredContract.SchemaName)
                json = ValidOptionsJson(request.UserMessage);
            else if (request.SchemaName == DateeResponsePlanStructuredContract.SchemaName)
            {
                json = _reconciliationResponses.Dequeue();
                if (json == "__candidate__") json = ExtractCandidatePlan(request.UserMessage);
            }
            else if (request.SchemaName == DateePerformanceStructuredContract.SchemaName)
                json = _performanceResponses.Dequeue();
            else
                throw new InvalidOperationException("Unexpected structured schema: " + request.SchemaName);
            return Task.FromResult(new StructuredLlmResponse(
                json,
                provider: "test",
                model: "issue-1425",
                usedNativeStructuredOutput: true,
                validationMode: "native_schema"));
        }

        private static string ValidOptionsJson(string prompt)
        {
            const string marker = "Each stat must be one of: ";
            int start = prompt.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("Available-stat marker is missing.");
            start += marker.Length;
            int end = prompt.IndexOf('.', start);
            string[] stats = prompt.Substring(start, end - start)
                .Split(',')
                .Select(value => value.Trim())
                .ToArray();
            if (stats.Length != 3) throw new InvalidOperationException("Exactly three stats are required.");
            return "{\"schema_version\":\"dialogue_options.v1\",\"options\":["
                + "{\"stat\":\"" + stats[0] + "\",\"text\":\"Tell me what you actually want.\",\"callback\":null,\"combo\":null},"
                + "{\"stat\":\"" + stats[1] + "\",\"text\":\"What surprised you today?\",\"callback\":null,\"combo\":null},"
                + "{\"stat\":\"" + stats[2] + "\",\"text\":\"Make one reckless confession.\",\"callback\":null,\"combo\":null}]}";
        }

        private static string ExtractCandidatePlan(string userMessage)
        {
            const string marker = "Candidate compiler plan:\n";
            int start = userMessage.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("Reconciliation candidate marker is missing.");
            start += marker.Length;
            int end = userMessage.IndexOf("\n\nAllowed movements:", start, StringComparison.Ordinal);
            if (end < 0) throw new InvalidOperationException("Reconciliation candidate terminator is missing.");
            return userMessage.Substring(start, end - start);
        }

        public Task<StructuredLlmResponse> SendStructuredConversationAsync(
            StructuredLlmRequest request,
            IReadOnlyList<ConversationMessage> priorMessages,
            CancellationToken cancellationToken = default)
            => SendStructuredAsync(request, cancellationToken);
    }

    private sealed class RecordingSink : IAgentJournalSink
    {
        private readonly List<AgentJournalSinkRecord> _records;

        public RecordingSink(List<AgentJournalSinkRecord> records) => _records = records;

        public Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class ProductionDice : IDiceRoller
    {
        public int Roll(int sides) => Math.Min(10, sides);
    }

    private sealed class EmptyTrapRegistry : ITrapRegistry
    {
        public TrapDefinition? GetTrap(StatType stat) => null;
        public string? GetLlmInstruction(StatType stat) => null;
    }
}

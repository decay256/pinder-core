using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        private const string DateePrivatePhaseResponsePlan = "response-plan-reconciliation";
        private const string DateeResponsePlanReconciliationPromptKey = "datee-response-plan-reconciliation";

        private sealed class CompiledDateeResponsePlan
        {
            public CompiledDateeResponsePlan(
                AcceptedDateeResponsePlanState state,
                IReadOnlyDictionary<string, string> journalLinks)
            {
                State = state ?? throw new ArgumentNullException(nameof(state));
                _journalLinks = journalLinks ?? throw new ArgumentNullException(nameof(journalLinks));
            }

            private readonly IReadOnlyDictionary<string, string> _journalLinks;
            public AcceptedDateeResponsePlanState State { get; }
            public DateeResponsePlan Plan => State.Plan;

            public IReadOnlyDictionary<string, string> JournalLinks()
                => _journalLinks;
        }

        private sealed class DateeResponsePlanJournalChain
        {
            public DateeResponsePlanJournalChain(string rootId, AgentJournalCorrelationIds? correlation)
            {
                SourceArtifactId = rootId + ".source";
                CompilerArtifactId = rootId + ".compiler";
                AcceptedArtifactId = rootId + ".accepted";
                Correlation = correlation;
            }

            public string SourceArtifactId { get; }
            public string CompilerArtifactId { get; }
            public string AcceptedArtifactId { get; }
            public AgentJournalCorrelationIds? Correlation { get; }
        }

        private async Task<CompiledDateeResponsePlan> CompileDateeResponsePlanAsync(
            DateeContext context,
            CharacterEmotionalDirection direction,
            PiConversationSession? dateeSession,
            CancellationToken cancellationToken)
        {
            DateeResponsePlanInput input = DateeResponsePlanInput.From(context, direction);
            DateeResponsePlanJournalChain journalChain = await CreateDateeResponsePlanJournalChainAsync(
                    context,
                    dateeSession)
                .ConfigureAwait(false);
            string[] inputSourceIds = ResponsePlanInputSourceIds(input).ToArray();
            await PersistDateeResponsePlanArtifactAsync(
                    journalChain,
                    dateeSession,
                    new AgentJournalDateeResponsePlanRecord(
                        journalChain.SourceArtifactId,
                        AgentJournalDateeResponsePlanArtifactKind.SourceInput,
                        AgentJournalJson.Serialize(input),
                        inputSourceIds),
                    cancellationToken)
                .ConfigureAwait(false);

            var compiler = new DateeResponsePlanCompiler();
            DateeResponsePlanCompilationResult compilation = compiler.Compile(input);
            await PersistDateeResponsePlanArtifactAsync(
                    journalChain,
                    dateeSession,
                    new AgentJournalDateeResponsePlanRecord(
                        journalChain.CompilerArtifactId,
                        AgentJournalDateeResponsePlanArtifactKind.CompilerOutcome,
                        CompilerArtifactJson(compilation),
                        inputSourceIds,
                        parentArtifactId: journalChain.SourceArtifactId,
                        compilerOutcome: compilation.Outcome.ToString()),
                    cancellationToken)
                .ConfigureAwait(false);
            if (compilation.Outcome == DateeResponsePlanCompilationOutcome.Rejected)
                throw compilation.Rejection ?? new DateeResponsePlanContractException(
                    "datee_response_plan_incompatible.rejected",
                    "DATEE response plan compilation was rejected.");
            if (compilation.Outcome == DateeResponsePlanCompilationOutcome.Accepted)
            {
                DateeResponsePlan accepted = compilation.Plan
                    ?? throw new InvalidOperationException("Accepted response-plan compilation omitted its plan.");
                await PersistAcceptedDateeResponsePlanAsync(
                        journalChain,
                        dateeSession,
                        accepted,
                        compilation.Outcome,
                        reconciliationInvocationId: null,
                        reconciliationResultId: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                return AcceptedEnvelope(journalChain, accepted, null, null);
            }

            if (!(_transport is IStructuredLlmTransport structuredTransport))
            {
                throw new LlmContractException(
                    phase: "datee_response_plan_reconciliation",
                    reason: "structured_transport_required",
                    message: "DATEE response-plan reconciliation requires provider-neutral structured output transport.",
                    parserName: DateeResponsePlanStructuredContract.ParserName,
                    turnId: context.CurrentTurn);
            }

            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            PromptEntry prompt = catalog.RequireCompleteEntry(
                DateeResponsePlanReconciliationPromptKey,
                "prompt-catalog: missing required runtime prompt key 'datee-response-plan-reconciliation'. The yaml file is incomplete or missing.");
            DateeResponsePlan candidate = compilation.Plan!;
            string candidateJson = DateeResponsePlanJson.Serialize(candidate);
            GameRunPromptDocumentPair promptDocuments = GameRunPromptDocumentBuilder.BuildReconciliationDocuments(
                prompt, ReconciliationValues(compilation, candidateJson));
            string userPrompt = promptDocuments.User.Text;
            string systemPrompt = promptDocuments.System.Text;
            int maxAttempts = GetContractViolationAttemptLimit();
            LlmContractException? finalRejection = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var metadata = BuildResponsePlanMetadata(compilation, candidateJson, journalChain);
                AgentJournalCallScope journal = await StartConversationJournalAttemptAsync(
                        ResolveConversationCallPath(context.AgentJournalContext, GameRunConversationJournalInventory.DateeResponsePlanReconciliation),
                        LlmPhase.OpponentResponse,
                        context.CurrentTurn,
                        attempt,
                        maxAttempts,
                        "datee-plan-reconciler",
                        promptDocuments.System,
                        promptDocuments.User,
                        session: dateeSession,
                        correlationContext: context.AgentJournalContext,
                        roleFactAccessDecisions: context.PromptFactAccessDecisions,
                        journalLinks: JournalLinks(journalChain))
                    .ConfigureAwait(false);
                try
                {
                    StructuredLlmRequest request = DateeResponsePlanStructuredContract.CreateRequest(
                        prompt, compilation, systemPrompt, userPrompt, context.CurrentTurn, metadata);
                    StructuredLlmResponse response = await SendStructuredWithDiagnosticsAsync(
                            structuredTransport,
                            request,
                            LlmPhase.OpponentResponse,
                            context.CurrentTurn,
                            cancellationToken,
                            attempt,
                            maxAttempts,
                            DateePrivatePhaseResponsePlan,
                            metadata,
                            priorMessages: null,
                            callId: journal.CallId,
                            promptContract: new PromptProviderContract(PromptContractRoleScope.Datee, new[] { promptDocuments.System, promptDocuments.User }, context.PromptFactAccessDecisions, request.SchemaName + ":" + request.SchemaVersion))
                        .ConfigureAwait(false);
                    DateeResponsePlan parsed = DateeResponsePlanStructuredContract.ParseStrict(
                        response.JsonText, context.CurrentTurn, response.Provider, response.Model);
                    DateeResponsePlan accepted = compiler.AcceptReconciled(compilation, parsed);
                    accepted = compiler.AttachReconciliationSource(
                        accepted,
                        "datee-response-plan-reconciliation:turn:"
                            + context.CurrentTurn.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + ":attempt:"
                            + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    response.ReportValidation("accepted");
                    await journal.CompleteAcceptedAsync(
                            DateeResponsePlanJson.Serialize(accepted),
                            resultMetadata: AcceptedMetadata(metadata))
                        .ConfigureAwait(false);
                    await PersistAcceptedDateeResponsePlanAsync(
                            journalChain,
                            dateeSession,
                            accepted,
                            compilation.Outcome,
                            journal.InvocationRecordId,
                            journal.ResultRecordId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return AcceptedEnvelope(
                        journalChain,
                        accepted,
                        journal.InvocationRecordId,
                        journal.ResultRecordId);
                }
                catch (LlmContractException ex)
                {
                    finalRejection = ex;
                    await journal.CompleteValidationRejectedAsync(
                            ex.Reason,
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["schema_name"] = DateeResponsePlanStructuredContract.SchemaName,
                                ["schema_version"] = DateeResponsePlanStructuredContract.SchemaVersion,
                                ["validation_outcome"] = "rejected",
                                ["validation_reason"] = ex.Reason,
                            })
                        .ConfigureAwait(false);
                    NotifyContractViolation(
                        new SemanticOutputRecoveryRejection<LlmContractException>(attempt, maxAttempts, ex, attempt == maxAttempts),
                        DateePrivatePhaseResponsePlan);
                    if (attempt < maxAttempts)
                    {
                        int delay = GetContractViolationBackoffDelayMs(_options.ContractViolationBackoffMs, attempt);
                        if (delay > 0) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    await journal.CompleteCancelledAsync(AgentJournalTerminalCodes.Cancelled).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await journal.CompleteProviderFailedAsync(ex.GetType().Name).ConfigureAwait(false);
                    throw;
                }
            }

            throw finalRejection ?? new LlmContractException(
                phase: "datee_response_plan_reconciliation",
                reason: "semantic_recovery_exhausted",
                message: "DATEE response-plan reconciliation exhausted bounded recovery.",
                parserName: DateeResponsePlanStructuredContract.ParserName,
                turnId: context.CurrentTurn);
        }

        private static IReadOnlyDictionary<string, string> ReconciliationValues(
            DateeResponsePlanCompilationResult compilation,
            string candidateJson)
        {
            string allowedMovements = string.Join(",", compilation.AllowedMovements.Select(DateeResponsePlanJson.Token));
            string allowedMoves = string.Join(",", compilation.AllowedConversationalMoves.Select(DateeResponsePlanJson.Token));
            string stageOrders = compilation.AllowedStageOrders.Count == 0
                ? "none"
                : string.Join(" | ", compilation.AllowedStageOrders.Select(order =>
                    string.Join("->", order.Select(stage => DateeResponsePlanJson.Token(stage.Movement) + (stage.OwnsDisclosure ? "[disclosure]" : string.Empty)))));
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidate_plan_json"] = candidateJson,
                ["allowed_movements"] = allowedMovements,
                ["allowed_conversational_moves"] = allowedMoves,
                ["allowed_stage_orders"] = stageOrders,
            };
        }

        private static IReadOnlyDictionary<string, string> BuildResponsePlanMetadata(
            DateeResponsePlanCompilationResult compilation,
            string candidateJson,
            DateeResponsePlanJournalChain journalChain)
            => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["response_plan_schema"] = DateeResponsePlan.CurrentSchemaVersion,
                ["compiler_outcome"] = compilation.Outcome.ToString(),
                ["candidate_plan_length"] = candidateJson.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["source_ids"] = string.Join(",", compilation.Plan!.Sources.Select(source => source.Id)),
                ["response_plan_source_artifact_id"] = journalChain.SourceArtifactId,
                ["response_plan_compiler_artifact_id"] = journalChain.CompilerArtifactId,
            };

        private static IReadOnlyDictionary<string, string> AcceptedMetadata(IReadOnlyDictionary<string, string> metadata)
        {
            Dictionary<string, string> result = metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            result["validation_outcome"] = "accepted";
            result["parser_name"] = DateeResponsePlanStructuredContract.ParserName;
            return result;
        }

        private async Task<DateeResponsePlanJournalChain> CreateDateeResponsePlanJournalChainAsync(
            DateeContext context,
            PiConversationSession? session)
        {
            string rootId = "datee-response-plan-" + Guid.NewGuid().ToString("N");
            bool journalingEnabled = _options.AgentJournalHostSink != null
                || ResolveProjectionSink(session, branch: null) != null;
            if (!journalingEnabled)
                return new DateeResponsePlanJournalChain(rootId, correlation: null);
            if (context.AgentJournalContext == null)
            {
                throw new InvalidOperationException("DATEE response-plan journaling requires GameRunAgentJournalContext.");
            }
            if (string.IsNullOrWhiteSpace(context.AgentJournalContext.RequestId))
                throw new DateeResponsePlanContractException(
                    "datee_response_plan_incompatible.agent_journal.request_id.required",
                    "DATEE response-plan provenance requires a real request ID.");

            string agentSessionId = await ResolveAgentSessionIdAsync(
                    "datee-plan",
                    session,
                    branch: null,
                    context.AgentJournalContext)
                .ConfigureAwait(false);
            string branchId = await ResolveBranchIdAsync(
                    branch: null,
                    branchKind: null,
                    context.AgentJournalContext)
                .ConfigureAwait(false);
            var correlation = new AgentJournalCorrelationIds(
                context.AgentJournalContext.GameRunId,
                agentSessionId,
                rootId,
                GameRunConversationJournalInventory.DateeResponsePlanCompiler,
                attemptOrdinal: 1,
                attemptId: "attempt-1",
                requestId: context.AgentJournalContext.RequestId,
                turnId: "turn-" + context.CurrentTurn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                branchId: branchId);
            return new DateeResponsePlanJournalChain(rootId, correlation);
        }

        private async Task PersistDateeResponsePlanArtifactAsync(
            DateeResponsePlanJournalChain chain,
            PiConversationSession? session,
            AgentJournalDateeResponsePlanRecord record,
            CancellationToken cancellationToken)
        {
            if (chain.Correlation == null) return;
            AgentJournalValidationResult validation = AgentJournalValidator.Validate(record);
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    "Invalid DATEE response-plan journal artifact: "
                    + string.Join(",", validation.Errors.Select(error => error.Code + "@" + error.Path)));

            AgentJournalSinkRecord sinkRecord = AgentJournalSinkRecord.DateeResponsePlan(record, chain.Correlation);
            IAgentJournalProjectionSink? projection = ResolveProjectionSink(session, branch: null);
            if (projection != null)
            {
                try
                {
                    await WritePlanJournalWithTimeoutAsync(
                            token => projection.ProjectAsync(sinkRecord, token),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    throw new AgentJournalPiProjectionException(sinkRecord.RecordId, sinkRecord.CustomType, ex);
                }
            }

            if (_options.AgentJournalHostSink != null)
            {
                try
                {
                    await WritePlanJournalWithTimeoutAsync(
                            token => _options.AgentJournalHostSink.PersistAsync(sinkRecord, token),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    throw new AgentJournalSinkPersistenceException(sinkRecord.RecordId, sinkRecord.CustomType, ex);
                }
            }
        }

        private async Task WritePlanJournalWithTimeoutAsync(
            Func<CancellationToken, Task> write,
            CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(_options.AgentJournalWriteTimeout);
                await write(timeout.Token).ConfigureAwait(false);
            }
        }

        private async Task PersistAcceptedDateeResponsePlanAsync(
            DateeResponsePlanJournalChain chain,
            PiConversationSession? session,
            DateeResponsePlan accepted,
            DateeResponsePlanCompilationOutcome compilerOutcome,
            string? reconciliationInvocationId,
            string? reconciliationResultId,
            CancellationToken cancellationToken)
        {
            await PersistDateeResponsePlanArtifactAsync(
                    chain,
                    session,
                    new AgentJournalDateeResponsePlanRecord(
                        chain.AcceptedArtifactId,
                        AgentJournalDateeResponsePlanArtifactKind.AcceptedPlan,
                        DateeResponsePlanJson.Serialize(accepted),
                        accepted.Sources.Select(source => source.Id).ToArray(),
                        parentArtifactId: chain.CompilerArtifactId,
                        compilerOutcome: compilerOutcome.ToString(),
                        reconciliationInvocationId: reconciliationInvocationId,
                        reconciliationResultId: reconciliationResultId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static CompiledDateeResponsePlan AcceptedEnvelope(
            DateeResponsePlanJournalChain chain,
            DateeResponsePlan plan,
            string? reconciliationInvocationId,
            string? reconciliationResultId)
        {
            var provenance = new DateeResponsePlanProvenance(
                chain.SourceArtifactId,
                chain.CompilerArtifactId,
                chain.AcceptedArtifactId,
                reconciliationInvocationId,
                reconciliationResultId);
            var state = AcceptedDateeResponsePlanState.Create(plan, provenance);
            return new CompiledDateeResponsePlan(state, JournalLinks(provenance));
        }

        private async Task<CompiledDateeResponsePlan> ReusedEnvelopeAsync(
            DateeContext context,
            AcceptedDateeResponsePlanState state,
            PiConversationSession? session,
            CancellationToken cancellationToken)
        {
            DateeResponsePlanJournalChain reuseChain = await CreateDateeResponsePlanJournalChainAsync(context, session)
                .ConfigureAwait(false);
            string reuseArtifactId = reuseChain.SourceArtifactId + ".reuse";
            await PersistDateeResponsePlanArtifactAsync(
                    reuseChain,
                    session,
                    new AgentJournalDateeResponsePlanRecord(
                        reuseArtifactId,
                        AgentJournalDateeResponsePlanArtifactKind.ReuseEvent,
                        AgentJournalJson.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["accepted_artifact_id"] = state.Provenance.AcceptedArtifactId,
                            ["message_reference"] = state.MessageReference,
                        }),
                        state.Plan.Sources.Select(source => source.Id).ToArray(),
                        parentArtifactId: state.Provenance.AcceptedArtifactId,
                        compilerOutcome: "Reused",
                        reconciliationInvocationId: state.Provenance.ReconciliationInvocationId,
                        reconciliationResultId: state.Provenance.ReconciliationResultId),
                    cancellationToken)
                .ConfigureAwait(false);
            return new CompiledDateeResponsePlan(
                state,
                JournalLinks(state.Provenance, reuseArtifactId));
        }

        private static bool AppliesToCurrentTurn(AcceptedDateeResponsePlanState state, DateeContext context)
            => state.OriginatingTurn == context.CurrentTurn
                && state.Plan.VisibleEvidence.MessageReference.SenderRole == ConversationParticipantRole.PlayerAvatar
                && string.Equals(state.MessageReference, state.Plan.VisibleEvidence.MessageReference.Value, StringComparison.Ordinal)
                && string.Equals(state.VisibleMessageText, context.PlayerDeliveredMessage, StringComparison.Ordinal);

        private static CharacterEmotionalDirection DirectionFromPlan(DateeResponsePlan plan)
            => new CharacterEmotionalDirection(
                plan.PrimaryEmotion,
                plan.SecondaryEmotion,
                plan.RegulatoryState,
                plan.Activation,
                plan.Trajectory,
                "restored accepted response plan",
                plan.DateeInterpretation,
                "perform the accepted response plan",
                "preserve every hard plan constraint",
                "reuse the accepted response plan byte-for-byte");

        private static IReadOnlyDictionary<string, string> JournalLinks(DateeResponsePlanJournalChain chain)
            => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["response_plan_source_artifact_id"] = chain.SourceArtifactId,
                ["response_plan_compiler_artifact_id"] = chain.CompilerArtifactId,
            };

        private static IReadOnlyDictionary<string, string> JournalLinks(
            DateeResponsePlanProvenance provenance,
            string? reuseArtifactId = null)
        {
            var links = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["response_plan_source_artifact_id"] = provenance.SourceArtifactId,
                ["response_plan_compiler_artifact_id"] = provenance.CompilerArtifactId,
                ["response_plan_accepted_artifact_id"] = provenance.AcceptedArtifactId,
            };
            if (provenance.ReconciliationInvocationId != null)
            {
                links[AgentJournalDateeResponsePlanRecord.ReconciliationInvocationLinkContextKey] =
                    provenance.ReconciliationInvocationId;
                links[AgentJournalDateeResponsePlanRecord.ReconciliationResultLinkContextKey] =
                    provenance.ReconciliationResultId!;
            }
            if (reuseArtifactId != null)
            {
                links["response_plan_reused"] = "true";
                links["response_plan_reuse_artifact_id"] = reuseArtifactId;
            }
            return links;
        }

        private static IEnumerable<string> ResponsePlanInputSourceIds(DateeResponsePlanInput input)
        {
            yield return input.VisibleEvidence.MessageReference.Value;
            yield return "relationship-state:turn:" + input.VisibleEvidence.MessageReference.Turn;
            yield return "emotional-director:turn:" + input.VisibleEvidence.MessageReference.Turn;
            if (input.ReactionTarget != null) yield return input.ReactionTarget.SourceId;
            if (input.CognitivePressure != null) yield return input.CognitivePressure.SourceId;
            foreach (string trap in input.ActiveTrapIds.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
                yield return "trap:" + trap;
            if (input.ArchetypeId != null) yield return input.ArchetypeId;
            if (input.DramaticArcSourceId != null) yield return input.DramaticArcSourceId;
        }

        private static string CompilerArtifactJson(DateeResponsePlanCompilationResult compilation)
        {
            if (compilation.Plan != null) return DateeResponsePlanJson.Serialize(compilation.Plan);
            return AgentJournalJson.Serialize(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["diagnostic_code"] = compilation.Rejection?.Code,
                ["source_id"] = compilation.Rejection?.SourceId,
            });
        }
    }
}

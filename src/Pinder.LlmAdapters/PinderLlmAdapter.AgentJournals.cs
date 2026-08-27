using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        private async Task<AgentJournalCallScope> StartConversationJournalAttemptAsync(
            string callPathId,
            string phase,
            int? turnId,
            int attemptOrdinal,
            int? totalAttempts,
            string agentSessionKind,
            IReadOnlyList<AnnotatedInvocationDocument> documents,
            PiConversationSession? session = null,
            PiConversationBranch? branch = null,
            string? branchKind = null,
            GameRunAgentJournalContext? correlationContext = null,
            bool measureTransportUsage = true,
            IReadOnlyList<RoleFactAccessDecision>? roleFactAccessDecisions = null)
        {
            GameRunConversationJournalInventory.ThrowIfNotApproved(callPathId);
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            if (correlationContext == null)
            {
                if (_options.AgentJournalHostSink == null)
                {
                    return AgentJournalCallScope.Disabled;
                }

                throw new InvalidOperationException(
                    "A per-call GameRunAgentJournalContext is required for conversational Agent Journal persistence.");
            }

            string agentSessionId = await ResolveAgentSessionIdAsync(
                    agentSessionKind,
                    session,
                    branch,
                    correlationContext)
                .ConfigureAwait(false);
            string branchId = await ResolveBranchIdAsync(branch, branchKind, correlationContext).ConfigureAwait(false);
            string turnPart = turnId.HasValue
                ? "turn-" + turnId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "turn-none";
            string attemptPart = "attempt-" + attemptOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // One invocation id names one actual provider call. Only the live diagnostic
            // and durable mirrors of that exact call may share it. Operation, turn, and
            // branch stay in their typed correlation fields rather than bloating this id.
            string callDiscriminator = OperationalDiagnostics.CreateCallId();
            string invocationId = "call-" + callDiscriminator + ":" + attemptPart;
            string attemptId = attemptPart;

            var correlation = new AgentJournalCorrelationIds(
                correlationContext.GameRunId,
                agentSessionId,
                invocationId,
                callPathId,
                attemptOrdinal,
                attemptId: attemptId,
                requestId: correlationContext.RequestId,
                turnId: turnId.HasValue ? turnPart : null,
                branchId: branchId);

            var context = new AgentJournalRecorderContext(
                correlation,
                ResolveModelId(),
                phase,
                documents.Select(document => document.ToAgentJournalInputDocument()).ToArray())
            {
                HostSink = _options.AgentJournalHostSink,
                PiProjectionSink = ResolveProjectionSink(session, branch),
                SinkFailureMode = _options.AgentJournalSinkFailureMode,
                OnDiagnostic = GetDiagnosticSink(),
                Clock = _options.AgentJournalClock,
                WriteTimeout = _options.AgentJournalWriteTimeout,
                RoleFactAccessDecisions = roleFactAccessDecisions,
            };

            AgentJournalAttempt attempt = await new AgentJournalRecorder(context)
                .StartAsync()
                .ConfigureAwait(false);
            return new AgentJournalCallScope(attempt, measureTransportUsage ? _transport : null);
        }

        private Task<AgentJournalCallScope> StartConversationJournalAttemptAsync(
            string callPathId,
            string phase,
            int? turnId,
            int attemptOrdinal,
            int? totalAttempts,
            string agentSessionKind,
            AnnotatedInvocationDocument systemDocument,
            AnnotatedInvocationDocument userDocument,
            PiConversationSession? session = null,
            PiConversationBranch? branch = null,
            string? branchKind = null,
            GameRunAgentJournalContext? correlationContext = null,
            bool measureTransportUsage = true,
            IReadOnlyList<RoleFactAccessDecision>? roleFactAccessDecisions = null)
            => StartConversationJournalAttemptAsync(
                callPathId,
                phase,
                turnId,
                attemptOrdinal,
                totalAttempts,
                agentSessionKind,
                new[] { systemDocument, userDocument },
                session,
                branch,
                branchKind,
                correlationContext,
                measureTransportUsage,
                roleFactAccessDecisions);

        private static AnnotatedInvocationDocument BuildRuntimeJournalDocument(string documentId, string text)
        {
            return AnnotatedInvocationDocument.Create(
                documentId,
                AgentJournalInputRole.User,
                documentId,
                text,
                string.IsNullOrEmpty(text)
                    ? Array.Empty<AgentJournalProvenanceRange>()
                    : new[]
                    {
                        new AgentJournalProvenanceRange(
                            documentId,
                            0,
                            text.Length,
                            AgentJournalRangeKind.RuntimeGenerated,
                            AgentJournalRedactionClass.None,
                            new AgentJournalSourceIdentity(
                                AgentJournalSourceKind.RuntimeGenerated,
                                "runtime",
                                documentId)),
                    });
        }

        private async Task<string> ResolveAgentSessionIdAsync(
            string agentSessionKind,
            PiConversationSession? session,
            PiConversationBranch? branch,
            GameRunAgentJournalContext correlationContext)
        {
            if (branch != null)
            {
                return await branch.GetAgentSessionIdAsync().ConfigureAwait(false);
            }

            if (session != null)
            {
                return await session.GetAgentSessionIdAsync().ConfigureAwait(false);
            }

            return correlationContext.AgentSessionId;
        }

        private async Task<string> ResolveBranchIdAsync(
            PiConversationBranch? branch,
            string? branchKind,
            GameRunAgentJournalContext correlationContext)
        {
            if (branch != null)
            {
                return await branch.GetAgentSessionIdAsync().ConfigureAwait(false);
            }

            return NonEmpty(correlationContext.BranchId, branchKind ?? "main");
        }

        private static string ResolveConversationCallPath(
            GameRunAgentJournalContext? context,
            string mainCallPath)
        {
            if (context == null) return mainCallPath;
            switch (context.BranchKind)
            {
                case GameRunConversationBranchKind.Prefetch:
                    return GameRunConversationJournalInventory.PrefetchBranchClone;
                case GameRunConversationBranchKind.Speculative:
                    return GameRunConversationJournalInventory.SpeculativeBranchClone;
                default:
                    return mainCallPath;
            }
        }

        private static IAgentJournalProjectionSink? ResolveProjectionSink(
            PiConversationSession? session,
            PiConversationBranch? branch)
        {
            if (branch != null)
            {
                return new PiAgentJournalProjectionSink(branch.Session);
            }

            return session == null ? null : new PiAgentJournalProjectionSink(session.Session);
        }

        private string ResolveModelId()
            => _transport.GetType().Name;

        private static string NonEmpty(string? value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value!;

        private sealed class AgentJournalCallScope : IAsyncDisposable
        {
            public static readonly AgentJournalCallScope Disabled = new AgentJournalCallScope();

            private readonly AgentJournalAttempt? _attempt;
            private readonly AgentJournalUsageMeasurementScope? _usageMeasurement;
            private AgentJournalUsageCapture? _completedUsage;

            private AgentJournalCallScope()
            {
            }

            public AgentJournalCallScope(AgentJournalAttempt attempt, object? usageSource)
            {
                _attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
                CallId = attempt.InvocationRecord.Correlation.InvocationId;
                _usageMeasurement = AgentJournalUsageMeasurementScope.Start(usageSource, CallId);
            }

            public string? CallId { get; }

            public Task CompleteAcceptedAsync(string outputText, string? semanticEntryId = null)
            {
                AgentJournalUsageCapture capture = CompleteUsage();
                return _attempt?.CompleteAcceptedAsync(
                    outputText ?? string.Empty,
                    capture.Usage,
                    semanticEntryId,
                    capture.Status,
                    capture) ?? Task.CompletedTask;
            }

            public Task CompleteValidationRejectedAsync(string validationCode)
            {
                AgentJournalUsageCapture capture = CompleteUsage();
                return _attempt?.CompleteValidationRejectedAsync(
                    validationCode,
                    capture.Usage,
                    capture.Status,
                    capture) ?? Task.CompletedTask;
            }

            public Task CompleteProviderFailedAsync(string errorCode)
            {
                AgentJournalUsageCapture capture = CompleteUsage();
                return _attempt?.CompleteProviderFailedAsync(
                    errorCode,
                    capture.Usage,
                    capture.Status,
                    capture) ?? Task.CompletedTask;
            }

            public Task CompleteCancelledAsync(string errorCode)
            {
                AgentJournalUsageCapture capture = CompleteUsage();
                return _attempt?.CompleteCancelledAsync(
                    errorCode,
                    usage: capture.Usage,
                    usageStatus: capture.Status,
                    usageCapture: capture) ?? Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                _usageMeasurement?.Dispose();
                return _attempt == null ? default : _attempt.DisposeAsync();
            }

            private AgentJournalUsageCapture CompleteUsage()
            {
                if (_completedUsage != null)
                {
                    return _completedUsage;
                }

                _completedUsage = _usageMeasurement == null
                    ? AgentJournalUsageCapture.Unavailable
                    : _usageMeasurement.Complete();
                return _completedUsage;
            }
        }

        private sealed class DateeResponseCoreResult
        {
            public DateeResponseCoreResult(StatefulDateeResult result, AgentJournalCallScope? journal)
            {
                Result = result ?? throw new ArgumentNullException(nameof(result));
                Journal = journal;
            }

            public StatefulDateeResult Result { get; }

            public AgentJournalCallScope? Journal { get; }
        }
    }
}

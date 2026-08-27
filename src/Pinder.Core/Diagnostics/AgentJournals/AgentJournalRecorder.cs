using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public static class AgentJournalTerminalCodes
    {
        public const string Accepted = "accepted";
        public const string ValidationRejected = "validation_rejected";
        public const string ProviderFailed = "provider_failed";
        public const string Cancelled = "cancelled";
        public const string Abandoned = "abandoned";
    }

    public static class AgentJournalOperationalDiagnostics
    {
        public const string Source = "AgentJournalRecorder";
        public const string SinkPersistenceFailedEventName = "AgentJournalSinkPersistenceFailed";
        public const string PhaseCode = "agent_journal_persistence";
        public const string RoleFactAccessRejectedEventName = "AgentJournalRoleFactAccessRejected";
        public const string RoleFactContractRejectedEventName = "AgentJournalRoleFactContractRejected";
        public const string RoleFactPolicyCorrelationRejectedEventName = "AgentJournalRoleFactPolicyCorrelationRejected";
        public const string RoleFactAccessPhaseCode = "role_fact_access";

        public static OperationalDiagnosticEvent RoleFactAccessRejected(
            RoleFactAccessDeniedException exception,
            GameRunAgentJournalContext? journalContext,
            string operationKind,
            int turn)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            RoleFactAccessDecision decision = exception.Decision;
            string correlationId = journalContext?.RequestId
                ?? journalContext?.GameRunId
                ?? string.Empty;
            return new OperationalDiagnosticEvent(
                Source,
                RoleFactAccessRejectedEventName,
                OperationalDiagnosticSeverity.Error,
                "A turn-local prompt fact was rejected before provider invocation.",
                exception,
                operationKind: operationKind,
                phaseCode: RoleFactAccessPhaseCode,
                lifecycle: OperationalDiagnosticLifecycle.Terminal,
                outcome: OperationalDiagnosticOutcome.Failed,
                failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                correlationId: correlationId,
                correlationHints: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["turn"] = turn.ToString(CultureInfo.InvariantCulture),
                    ["decision_code"] = decision.Code,
                    ["fact_source_id"] = decision.FactSourceId,
                    ["fact_source_kind"] = decision.FactSourceKind.ToString(),
                    ["subject_character_id"] = decision.SubjectCharacterId.ToString("D"),
                    ["subject_role"] = decision.SubjectRole.ToString(),
                    ["recipient_character_id"] = decision.RecipientCharacterId.ToString("D"),
                    ["recipient_role"] = decision.RecipientRole.ToString(),
                    ["visibility"] = decision.Visibility.ToString(),
                },
                branchId: journalContext?.BranchId);
        }


        public static OperationalDiagnosticEvent RoleFactContractRejected(
            RoleFactContractException exception,
            OwnedPromptFactV1? fact,
            GameRunAgentJournalContext? journalContext,
            string operationKind,
            int turn)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            var hints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["turn"] = turn.ToString(CultureInfo.InvariantCulture),
                ["error_code"] = exception.Code,
            };
            if (fact != null)
            {
                hints["fact_source_id"] = fact.SourceId;
                hints["fact_source_kind"] = fact.SourceKind.ToString();
                hints["owner_character_id"] = fact.SubjectCharacterId.ToString("D");
                hints["owner_role"] = fact.SubjectRole.ToString();
                hints["visibility"] = fact.Visibility.ToString();
            }
            return new OperationalDiagnosticEvent(
                Source,
                RoleFactContractRejectedEventName,
                OperationalDiagnosticSeverity.Error,
                "A malformed prompt-fact request was rejected before provider invocation.",
                exception,
                operationKind: operationKind,
                phaseCode: RoleFactAccessPhaseCode,
                lifecycle: OperationalDiagnosticLifecycle.Terminal,
                outcome: OperationalDiagnosticOutcome.Failed,
                failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                correlationId: journalContext?.RequestId ?? journalContext?.GameRunId ?? string.Empty,
                correlationHints: hints,
                branchId: journalContext?.BranchId);
        }

        public static OperationalDiagnosticEvent RoleFactPolicyCorrelationRejected(
            RoleFactContractException exception,
            RoleFactAccessDecision decision,
            GameRunAgentJournalContext journalContext,
            string operationKind,
            int turn)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (journalContext == null) throw new ArgumentNullException(nameof(journalContext));
            return new OperationalDiagnosticEvent(
                Source,
                RoleFactPolicyCorrelationRejectedEventName,
                OperationalDiagnosticSeverity.Error,
                "A role-fact rejection could not be journaled because request correlation was missing.",
                exception,
                operationKind: operationKind,
                phaseCode: RoleFactAccessPhaseCode,
                lifecycle: OperationalDiagnosticLifecycle.Terminal,
                outcome: OperationalDiagnosticOutcome.Failed,
                failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                correlationId: journalContext.GameRunId,
                correlationHints: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["turn"] = turn.ToString(CultureInfo.InvariantCulture),
                    ["error_code"] = exception.Code,
                    ["fact_source_id"] = decision.FactSourceId,
                    ["fact_source_kind"] = decision.FactSourceKind.ToString(),
                    ["subject_character_id"] = decision.SubjectCharacterId.ToString("D"),
                    ["subject_role"] = decision.SubjectRole.ToString(),
                    ["recipient_character_id"] = decision.RecipientCharacterId.ToString("D"),
                    ["recipient_role"] = decision.RecipientRole.ToString(),
                    ["visibility"] = decision.Visibility.ToString(),
                },
                branchId: journalContext.BranchId);
        }

        public static OperationalDiagnosticEvent SinkPersistenceFailed(
            AgentJournalSinkRecord record,
            AgentJournalSinkFailureMode failureMode,
            Exception exception)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            AgentJournalCorrelationIds? provider = record.Correlation;
            AgentJournalRoleFactPolicyCorrelation? policy = record.PolicyCorrelation;
            bool failClosed = failureMode == AgentJournalSinkFailureMode.FailClosed;
            var hints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["record_id"] = record.RecordId,
                ["custom_type"] = record.CustomType,
                ["failure_mode"] = failureMode.ToString(),
                ["game_run_id"] = provider?.GameRunId ?? policy?.GameRunId ?? string.Empty,
                ["agent_session_id"] = provider?.AgentSessionId ?? policy?.AgentSessionId ?? string.Empty,
            };
            if (provider != null)
            {
                hints["invocation_id"] = provider.InvocationId;
                hints["attempt_id"] = provider.AttemptId ?? string.Empty;
                hints["attempt_ordinal"] = provider.AttemptOrdinal.ToString(CultureInfo.InvariantCulture);
                hints["owner"] = provider.Owner ?? string.Empty;
                hints["journal_destination"] = provider.JournalDestination ?? string.Empty;
                hints["execution_class"] = provider.ExecutionClass ?? string.Empty;
                hints["output_link_id"] = provider.OutputLinkId ?? string.Empty;
            }
            if (policy != null)
            {
                hints["request_id"] = policy.RequestId;
                hints["turn_id"] = policy.TurnId;
            }
            return new OperationalDiagnosticEvent(
                Source,
                SinkPersistenceFailedEventName,
                failClosed ? OperationalDiagnosticSeverity.Error : OperationalDiagnosticSeverity.Warning,
                "Agent journal host sink persistence failed.",
                exception,
                operationKind: "agent_journal",
                phaseCode: PhaseCode,
                lifecycle: failClosed ? OperationalDiagnosticLifecycle.Terminal : OperationalDiagnosticLifecycle.Phase,
                outcome: failClosed ? OperationalDiagnosticOutcome.Failed : OperationalDiagnosticOutcome.Degraded,
                failureClassification: OperationalDiagnostics.ClassifyException(exception),
                correlationId: provider?.InvocationId ?? policy?.RequestId ?? string.Empty,
                callId: provider?.InvocationId,
                correlationHints: hints,
                branchId: provider?.BranchId ?? policy?.BranchId);
        }
    }

    public sealed class AgentJournalRecorderContext
    {
        private TimeSpan _writeTimeout = TimeSpan.FromSeconds(2);

        public AgentJournalRecorderContext(
            AgentJournalCorrelationIds correlation,
            string modelId,
            string phase,
            IReadOnlyList<AgentJournalInputDocument> inputDocuments)
        {
            Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
            Phase = phase ?? throw new ArgumentNullException(nameof(phase));
            InputDocuments = inputDocuments ?? throw new ArgumentNullException(nameof(inputDocuments));
        }

        public AgentJournalCorrelationIds Correlation { get; }
        public string ModelId { get; }
        public string Phase { get; }
        public IReadOnlyList<AgentJournalInputDocument> InputDocuments { get; }
        public IReadOnlyList<RoleFactAccessDecision>? RoleFactAccessDecisions { get; set; }
        public IAgentJournalProjectionSink? PiProjectionSink { get; set; }
        public IAgentJournalSink? HostSink { get; set; }
        public AgentJournalSinkFailureMode SinkFailureMode { get; set; } = AgentJournalSinkFailureMode.BestEffort;
        public Action<OperationalDiagnosticEvent>? OnDiagnostic { get; set; }
        public Func<DateTimeOffset>? Clock { get; set; }

        public TimeSpan WriteTimeout
        {
            get => _writeTimeout;
            set
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value), "Agent journal write timeout must be positive.");
                _writeTimeout = value;
            }
        }

        internal string TimestampUtc()
        {
            DateTimeOffset now = Clock == null ? DateTimeOffset.UtcNow : Clock();
            return now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
    }

    public sealed class AgentJournalRecorder
    {
        private readonly AgentJournalRecorderContext _context;
        private readonly object _startGate = new object();
        private Task<AgentJournalAttempt>? _startTask;
        private AgentJournalAttempt? _startedAttempt;
        private PendingStart? _pendingStart;

        public AgentJournalRecorder(AgentJournalRecorderContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task<AgentJournalAttempt> StartAsync(CancellationToken cancellationToken = default)
            => StartOrRetryAsync(createIfMissing: true, cancellationToken);

        public Task<AgentJournalAttempt> RetryStartAsync(CancellationToken cancellationToken = default)
            => StartOrRetryAsync(createIfMissing: false, cancellationToken);

        private Task<AgentJournalAttempt> StartOrRetryAsync(
            bool createIfMissing,
            CancellationToken cancellationToken)
        {
            lock (_startGate)
            {
                if (_startedAttempt != null)
                {
                    return Task.FromResult(_startedAttempt);
                }

                if (_startTask != null)
                {
                    return _startTask;
                }

                if (_pendingStart == null)
                {
                    if (!createIfMissing)
                    {
                        return Task.FromException<AgentJournalAttempt>(new InvalidOperationException(
                            "Agent journal start has no pending invocation to retry."));
                    }

                    var invocation = new LlmInvocationRecord(
                        _context.Correlation,
                        _context.ModelId,
                        _context.Phase,
                        SnapshotInputDocuments(_context.InputDocuments),
                        _context.TimestampUtc(),
                        SnapshotRoleFactAccessDecisions(_context.RoleFactAccessDecisions));
                    ThrowIfInvalid(AgentJournalValidator.Validate(invocation), AgentJournalSchemaNames.LlmInvocationV1);
                    _pendingStart = new PendingStart(
                        invocation,
                        AgentJournalSinkRecord.Invocation(invocation));
                }

                var completion = new TaskCompletionSource<AgentJournalAttempt>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _startTask = completion.Task;
                _ = RunStartAsync(_pendingStart, cancellationToken, completion);
                return completion.Task;
            }
        }

        private async Task RunStartAsync(
            PendingStart pending,
            CancellationToken cancellationToken,
            TaskCompletionSource<AgentJournalAttempt> completion)
        {
            try
            {
                if (!pending.PiProjected)
                {
                    await ProjectToPiAsync(pending.Record, cancellationToken).ConfigureAwait(false);
                    pending.PiProjected = true;
                }

                if (!pending.HostDelivered)
                {
                    pending.SinkFailures.AddRange(await PersistToHostAsync(
                        pending.Record,
                        cancellationToken).ConfigureAwait(false));
                    pending.HostDelivered = true;
                }

                var attempt = new AgentJournalAttempt(
                    _context,
                    this,
                    pending.Invocation,
                    pending.SinkFailures.AsReadOnly());
                lock (_startGate)
                {
                    _startedAttempt = attempt;
                    _pendingStart = null;
                    _startTask = null;
                }
                completion.TrySetResult(attempt);
            }
            catch (Exception ex)
            {
                lock (_startGate)
                {
                    _startTask = null;
                }
                completion.TrySetException(ex);
            }
        }

        private sealed class PendingStart
        {
            public PendingStart(LlmInvocationRecord invocation, AgentJournalSinkRecord record)
            {
                Invocation = invocation;
                Record = record;
            }

            public LlmInvocationRecord Invocation { get; }
            public AgentJournalSinkRecord Record { get; }
            public bool PiProjected { get; set; }
            public bool HostDelivered { get; set; }
            public List<AgentJournalSinkPersistenceException> SinkFailures { get; }
                = new List<AgentJournalSinkPersistenceException>();
        }

        internal async Task ProjectToPiAsync(
            AgentJournalSinkRecord record,
            CancellationToken cancellationToken)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            if (_context.PiProjectionSink != null)
            {
                if (record.Correlation == null
                    || string.IsNullOrWhiteSpace(record.Correlation.AgentSessionId))
                {
                    throw new InvalidOperationException(
                        "Agent journal Pi projection requires a real Agent Session id.");
                }

                try
                {
                    await WithTimeout(
                        token => _context.PiProjectionSink.ProjectAsync(record, token),
                        cancellationToken,
                        _context.WriteTimeout).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    throw new AgentJournalPiProjectionException(record.RecordId, record.CustomType, ex);
                }
            }
        }

        internal async Task<IReadOnlyList<AgentJournalSinkPersistenceException>> PersistToHostAsync(
            AgentJournalSinkRecord record,
            CancellationToken cancellationToken)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            if (_context.HostSink == null)
            {
                return Array.Empty<AgentJournalSinkPersistenceException>();
            }

            try
            {
                await WithTimeout(
                    token => _context.HostSink.PersistAsync(record, token),
                    cancellationToken,
                    _context.WriteTimeout).ConfigureAwait(false);
                return Array.Empty<AgentJournalSinkPersistenceException>();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                var failure = new AgentJournalSinkPersistenceException(record.RecordId, record.CustomType, ex);
                if (_context.SinkFailureMode == AgentJournalSinkFailureMode.FailClosed)
                {
                    throw failure;
                }

                OperationalDiagnostics.Emit(
                    _context.OnDiagnostic,
                    AgentJournalOperationalDiagnostics.SinkPersistenceFailed(
                        record,
                        _context.SinkFailureMode,
                        failure.InnerException ?? failure));
                return new[] { failure };
            }
        }

        private static IReadOnlyList<AgentJournalInputDocument> SnapshotInputDocuments(
            IReadOnlyList<AgentJournalInputDocument> inputDocuments)
        {
            var documents = new AgentJournalInputDocument[inputDocuments.Count];
            for (int documentIndex = 0; documentIndex < inputDocuments.Count; documentIndex++)
            {
                AgentJournalInputDocument document = inputDocuments[documentIndex]
                    ?? throw new ArgumentException("Input documents cannot contain null entries.", nameof(inputDocuments));
                var ranges = new AgentJournalProvenanceRange[document.Ranges.Count];
                for (int rangeIndex = 0; rangeIndex < document.Ranges.Count; rangeIndex++)
                {
                    AgentJournalProvenanceRange range = document.Ranges[rangeIndex]
                        ?? throw new ArgumentException("Input document ranges cannot contain null entries.", nameof(inputDocuments));
                    AgentJournalSourceIdentity source = range.Source;
                    ranges[rangeIndex] = new AgentJournalProvenanceRange(
                        range.DocumentId,
                        range.StartUtf16,
                        range.EndUtf16,
                        range.RangeKind,
                        range.RedactionClass,
                        new AgentJournalSourceIdentity(
                            source.Kind,
                            source.SourceId,
                            source.KeyPath,
                            source.Revision,
                            source.ContentHash,
                            source.EditorTargetId));
                }

                documents[documentIndex] = new AgentJournalInputDocument(
                    document.DocumentId,
                    document.Role,
                    document.Text,
                    Array.AsReadOnly(ranges));
            }

            return Array.AsReadOnly(documents);
        }

        private static IReadOnlyList<AgentJournalRoleFactAccessDecision>? SnapshotRoleFactAccessDecisions(
            IReadOnlyList<RoleFactAccessDecision>? decisions)
        {
            if (decisions == null || decisions.Count == 0) return null;
            var snapshot = new AgentJournalRoleFactAccessDecision[decisions.Count];
            for (int i = 0; i < decisions.Count; i++)
                snapshot[i] = AgentJournalRoleFactAccessDecision.From(
                    decisions[i] ?? throw new ArgumentException("Role fact access decisions cannot contain null entries.", nameof(decisions)));
            return Array.AsReadOnly(snapshot);
        }

        private static async Task WithTimeout(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken,
            TimeSpan timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var timeoutSource = new CancellationTokenSource())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
            {
                Task operationTask;
                try
                {
                    operationTask = operation(linked.Token);
                }
                catch
                {
                    throw;
                }

                if (operationTask == null)
                {
                    throw new InvalidOperationException("Agent journal sink returned a null task.");
                }

                Task delayTask = Task.Delay(timeout);
                Task completed = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);
                if (!ReferenceEquals(completed, operationTask))
                {
                    timeoutSource.Cancel();
                    throw new TimeoutException("Agent journal persistence did not complete within the configured timeout.");
                }

                await operationTask.ConfigureAwait(false);
            }
        }

        private static void ThrowIfInvalid(AgentJournalValidationResult result, string customType)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.IsValid)
            {
                return;
            }

            throw new ArgumentException(
                "Agent journal " + customType + " record is invalid: " + string.Join(", ", ErrorCodes(result)),
                nameof(result));
        }

        private static IEnumerable<string> ErrorCodes(AgentJournalValidationResult result)
        {
            foreach (AgentJournalValidationError error in result.Errors)
            {
                yield return error.Code + "@" + error.Path;
            }
        }
    }

    public sealed class AgentJournalAttempt : IAsyncDisposable
    {
        private readonly AgentJournalRecorderContext _context;
        private readonly AgentJournalRecorder _recorder;
        private readonly IReadOnlyList<AgentJournalSinkPersistenceException> _startSinkFailures;
        private readonly object _terminalGate = new object();
        private Task<AgentJournalTerminalResult>? _completionTask;
        private AgentJournalTerminalResult? _terminalResult;
        private PendingTerminal? _pendingTerminal;

        internal AgentJournalAttempt(
            AgentJournalRecorderContext context,
            AgentJournalRecorder recorder,
            LlmInvocationRecord invocationRecord,
            IReadOnlyList<AgentJournalSinkPersistenceException> startSinkFailures)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            InvocationRecord = invocationRecord ?? throw new ArgumentNullException(nameof(invocationRecord));
            _startSinkFailures = startSinkFailures ?? Array.Empty<AgentJournalSinkPersistenceException>();
        }

        public LlmInvocationRecord InvocationRecord { get; }
        public IReadOnlyList<AgentJournalSinkPersistenceException> StartSinkFailures => _startSinkFailures;

        public Task<AgentJournalTerminalResult> CompleteAcceptedAsync(
            string outputText,
            AgentJournalUsage? usage,
            string? semanticEntryId = null,
            IReadOnlyDictionary<string, string>? resultMetadata = null,
            AgentJournalUsageStatus usageStatus = AgentJournalUsageStatus.Unknown,
            AgentJournalUsageCapture? usageCapture = null)
        {
            return CompleteTerminalAsync(
                AgentJournalTerminalStatus.Succeeded,
                outputText,
                usage,
                validationCode: AgentJournalTerminalCodes.Accepted,
                errorCode: null,
                semanticEntryId: semanticEntryId,
                resultMetadata: resultMetadata,
                usageStatus: usageStatus,
                usageCapture: usageCapture);
        }

        public Task<AgentJournalTerminalResult> CompleteValidationRejectedAsync(
            string validationCode,
            AgentJournalUsage? usage = null,
            AgentJournalUsageStatus usageStatus = AgentJournalUsageStatus.Unknown,
            AgentJournalUsageCapture? usageCapture = null,
            IReadOnlyDictionary<string, string>? resultMetadata = null)
        {
            return CompleteTerminalAsync(
                AgentJournalTerminalStatus.Rejected,
                outputText: null,
                usage,
                validationCode: string.IsNullOrWhiteSpace(validationCode)
                    ? AgentJournalTerminalCodes.ValidationRejected
                    : validationCode,
                errorCode: null,
                semanticEntryId: null,
                resultMetadata: resultMetadata,
                usageStatus: usageStatus,
                usageCapture: usageCapture);
        }

        public Task<AgentJournalTerminalResult> CompleteProviderFailedAsync(
            string errorCode,
            AgentJournalUsage? usage = null,
            AgentJournalUsageStatus usageStatus = AgentJournalUsageStatus.Unknown,
            AgentJournalUsageCapture? usageCapture = null)
        {
            return CompleteTerminalAsync(
                AgentJournalTerminalStatus.Failed,
                outputText: null,
                usage,
                validationCode: null,
                errorCode: string.IsNullOrWhiteSpace(errorCode)
                    ? AgentJournalTerminalCodes.ProviderFailed
                    : errorCode,
                semanticEntryId: null,
                resultMetadata: null,
                usageStatus: usageStatus,
                usageCapture: usageCapture);
        }

        public Task<AgentJournalTerminalResult> CompleteCancelledAsync(
            string errorCode,
            CancellationToken providerCancellationToken = default,
            AgentJournalUsage? usage = null,
            AgentJournalUsageStatus usageStatus = AgentJournalUsageStatus.Unknown,
            AgentJournalUsageCapture? usageCapture = null)
        {
            return CompleteTerminalAsync(
                AgentJournalTerminalStatus.Cancelled,
                outputText: null,
                usage,
                validationCode: null,
                errorCode: string.IsNullOrWhiteSpace(errorCode)
                    ? AgentJournalTerminalCodes.Cancelled
                    : errorCode,
                semanticEntryId: null,
                resultMetadata: null,
                usageStatus: usageStatus,
                usageCapture: usageCapture);
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask(CompleteTerminalAsync(
                AgentJournalTerminalStatus.Failed,
                outputText: null,
                usage: null,
                validationCode: null,
                errorCode: AgentJournalTerminalCodes.Abandoned,
                semanticEntryId: null,
                resultMetadata: null,
                usageStatus: AgentJournalUsageStatus.Unavailable,
                usageCapture: null));
        }

        private Task<AgentJournalTerminalResult> CompleteTerminalAsync(
            AgentJournalTerminalStatus terminalStatus,
            string? outputText,
            AgentJournalUsage? usage,
            string? validationCode,
            string? errorCode,
            string? semanticEntryId,
            IReadOnlyDictionary<string, string>? resultMetadata,
            AgentJournalUsageStatus usageStatus,
            AgentJournalUsageCapture? usageCapture)
        {
            lock (_terminalGate)
            {
                if (_terminalResult != null)
                {
                    return Task.FromResult(_terminalResult.AsAlreadyCompleted());
                }

                if (_completionTask != null)
                {
                    return AlreadyCompletedAsync(_completionTask);
                }

                if (_pendingTerminal == null)
                {
                    _pendingTerminal = CreatePendingTerminal(
                        terminalStatus,
                        outputText,
                        usage,
                        validationCode,
                        errorCode,
                        semanticEntryId,
                        resultMetadata,
                        ResolveEmittedUsageCapture(usageStatus, usage, usageCapture));
                }

                var completion = new TaskCompletionSource<AgentJournalTerminalResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _completionTask = completion.Task;
                _ = RunCompletionAsync(_pendingTerminal, completion);
                return completion.Task;
            }
        }

        private static AgentJournalUsageCapture ResolveEmittedUsageCapture(
            AgentJournalUsageStatus requestedStatus,
            AgentJournalUsage? usage,
            AgentJournalUsageCapture? usageCapture)
        {
            if (usageCapture != null)
            {
                return usageCapture;
            }

            AgentJournalUsageStatus status = ResolveEmittedUsageStatus(requestedStatus, usage);
            return AgentJournalUsageCapture.FromResultUsage(
                usage,
                status,
                status == AgentJournalUsageStatus.Unavailable
                    ? "legacy_result_usage_unavailable"
                    : "legacy_result_usage");
        }

        private static AgentJournalUsageStatus ResolveEmittedUsageStatus(
            AgentJournalUsageStatus requestedStatus,
            AgentJournalUsage? usage)
        {
            if (requestedStatus != AgentJournalUsageStatus.Unknown)
            {
                return requestedStatus;
            }

            if (usage == null)
            {
                return AgentJournalUsageStatus.Unavailable;
            }

            return usage.CacheCreationInputTokens.HasValue && usage.CacheReadInputTokens.HasValue
                ? AgentJournalUsageStatus.Complete
                : AgentJournalUsageStatus.Incomplete;
        }

        private async Task RunCompletionAsync(
            PendingTerminal pending,
            TaskCompletionSource<AgentJournalTerminalResult> completion)
        {
            try
            {
                AgentJournalTerminalResult result = await CompleteTerminalCoreAsync(pending).ConfigureAwait(false);
                lock (_terminalGate)
                {
                    _terminalResult = result;
                    _pendingTerminal = null;
                    _completionTask = null;
                }
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                lock (_terminalGate)
                {
                    _completionTask = null;
                }
                completion.TrySetException(ex);
            }
        }

        private static async Task<AgentJournalTerminalResult> AlreadyCompletedAsync(
            Task<AgentJournalTerminalResult> terminalTask)
        {
            AgentJournalTerminalResult result = await terminalTask.ConfigureAwait(false);
            return result.AsAlreadyCompleted();
        }

        private PendingTerminal CreatePendingTerminal(
            AgentJournalTerminalStatus terminalStatus,
            string? outputText,
            AgentJournalUsage? usage,
            string? validationCode,
            string? errorCode,
            string? semanticEntryId,
            IReadOnlyDictionary<string, string>? resultMetadata,
            AgentJournalUsageCapture usageCapture)
        {
            // Validate the complete immutable terminal payload before reserving lifecycle work.
            var resultRecord = new LlmResultRecord(
                InvocationRecord.Correlation,
                terminalStatus,
                outputText,
                usageCapture.Usage,
                validationCode,
                errorCode,
                _context.TimestampUtc(),
                usageCapture.Status,
                usageStatusReason: usageCapture.UsageStatusReason,
                providerId: usageCapture.ProviderId,
                modelId: usageCapture.ModelId,
                requestedProviderId: usageCapture.RequestedProviderId,
                requestedModelId: usageCapture.RequestedModelId,
                observedStartedAtUnixMilliseconds: usageCapture.ObservedStartedAtUnixMilliseconds,
                observedCompletedAtUnixMilliseconds: usageCapture.ObservedCompletedAtUnixMilliseconds,
                observedDurationMilliseconds: usageCapture.ObservedDurationMilliseconds,
                effectiveInputTokens: usageCapture.EffectiveInputTokens,
                effectiveOutputTokens: usageCapture.EffectiveOutputTokens,
                effectiveTotalTokens: usageCapture.EffectiveTotalTokens,
                telemetryDiscrepancyCode: usageCapture.TelemetryDiscrepancyCode,
                resultMetadata: resultMetadata);
            ThrowIfInvalidResult(resultRecord);

            MessageLinkRecord? linkRecord = null;
            if (terminalStatus == AgentJournalTerminalStatus.Succeeded
                && !string.IsNullOrWhiteSpace(semanticEntryId))
            {
                linkRecord = new MessageLinkRecord(
                    semanticEntryId!,
                    InvocationRecord.Correlation.InvocationId,
                    InvocationRecord.Correlation.AgentSessionId!,
                    InvocationRecord.Correlation.TurnId,
                    InvocationRecord.Correlation.BranchId);
                ThrowIfInvalidLink(linkRecord);
            }

            return new PendingTerminal(
                resultRecord,
                linkRecord,
                new PendingProjection(AgentJournalSinkRecord.Result(resultRecord)),
                linkRecord == null
                    ? null
                    : new PendingProjection(AgentJournalSinkRecord.MessageLink(
                        linkRecord,
                        InvocationRecord.Correlation)));
        }

        private async Task<AgentJournalTerminalResult> CompleteTerminalCoreAsync(PendingTerminal pending)
        {
            await DeliverAsync(pending.ResultProjection, pending.SinkFailures).ConfigureAwait(false);
            if (pending.LinkProjection != null)
            {
                await DeliverAsync(pending.LinkProjection, pending.SinkFailures).ConfigureAwait(false);
            }

            return new AgentJournalTerminalResult(
                recorded: true,
                alreadyCompleted: false,
                pending.ResultRecord,
                pending.LinkRecord,
                pending.SinkFailures.AsReadOnly());
        }

        private async Task DeliverAsync(
            PendingProjection projection,
            List<AgentJournalSinkPersistenceException> failures)
        {
            if (!projection.PiProjected)
            {
                await _recorder.ProjectToPiAsync(projection.Record, CancellationToken.None).ConfigureAwait(false);
                projection.PiProjected = true;
            }

            if (!projection.HostDelivered)
            {
                failures.AddRange(await _recorder.PersistToHostAsync(
                    projection.Record,
                    CancellationToken.None).ConfigureAwait(false));
                projection.HostDelivered = true;
            }
        }

        private static void ThrowIfInvalidResult(LlmResultRecord resultRecord)
        {
            AgentJournalValidationResult validation = AgentJournalValidator.Validate(resultRecord);
            if (!validation.IsValid)
            {
                throw new ArgumentException("Agent journal result record is invalid.");
            }
        }

        private static void ThrowIfInvalidLink(MessageLinkRecord linkRecord)
        {
            AgentJournalValidationResult validation = AgentJournalValidator.Validate(linkRecord);
            if (!validation.IsValid)
            {
                throw new ArgumentException("Agent journal message-link record is invalid.");
            }
        }

        private sealed class PendingTerminal
        {
            public PendingTerminal(
                LlmResultRecord resultRecord,
                MessageLinkRecord? linkRecord,
                PendingProjection resultProjection,
                PendingProjection? linkProjection)
            {
                ResultRecord = resultRecord;
                LinkRecord = linkRecord;
                ResultProjection = resultProjection;
                LinkProjection = linkProjection;
            }

            public LlmResultRecord ResultRecord { get; }
            public MessageLinkRecord? LinkRecord { get; }
            public PendingProjection ResultProjection { get; }
            public PendingProjection? LinkProjection { get; }
            public List<AgentJournalSinkPersistenceException> SinkFailures { get; }
                = new List<AgentJournalSinkPersistenceException>();
        }

        private sealed class PendingProjection
        {
            public PendingProjection(AgentJournalSinkRecord record)
            {
                Record = record;
            }

            public AgentJournalSinkRecord Record { get; }
            public bool PiProjected { get; set; }
            public bool HostDelivered { get; set; }
        }
    }

    public sealed class AgentJournalTerminalResult
    {
        internal AgentJournalTerminalResult(
            bool recorded,
            bool alreadyCompleted,
            LlmResultRecord resultRecord,
            MessageLinkRecord? messageLinkRecord,
            IReadOnlyList<AgentJournalSinkPersistenceException> sinkFailures)
        {
            Recorded = recorded;
            AlreadyCompleted = alreadyCompleted;
            ResultRecord = resultRecord ?? throw new ArgumentNullException(nameof(resultRecord));
            MessageLinkRecord = messageLinkRecord;
            SinkFailures = sinkFailures ?? Array.Empty<AgentJournalSinkPersistenceException>();
        }

        public bool Recorded { get; }
        public bool AlreadyCompleted { get; }
        public LlmResultRecord ResultRecord { get; }
        public MessageLinkRecord? MessageLinkRecord { get; }
        public IReadOnlyList<AgentJournalSinkPersistenceException> SinkFailures { get; }

        internal AgentJournalTerminalResult AsAlreadyCompleted()
            => new AgentJournalTerminalResult(
                recorded: false,
                alreadyCompleted: true,
                ResultRecord,
                MessageLinkRecord,
                SinkFailures);
    }

    public sealed class AgentJournalSinkRecord
    {
        private AgentJournalSinkRecord(
            string recordId,
            string customType,
            object record,
            AgentJournalCorrelationIds? correlation,
            AgentJournalRoleFactPolicyCorrelation? policyCorrelation = null)
        {
            RecordId = recordId ?? throw new ArgumentNullException(nameof(recordId));
            CustomType = customType ?? throw new ArgumentNullException(nameof(customType));
            Record = record ?? throw new ArgumentNullException(nameof(record));
            if ((correlation == null) == (policyCorrelation == null))
                throw new ArgumentException("Exactly one journal correlation shape is required.");
            Correlation = correlation;
            PolicyCorrelation = policyCorrelation;
        }

        public string RecordId { get; }
        public string CustomType { get; }
        public object Record { get; }
        public AgentJournalCorrelationIds? Correlation { get; }
        public AgentJournalRoleFactPolicyCorrelation? PolicyCorrelation { get; }

        public static AgentJournalSinkRecord Invocation(LlmInvocationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return new AgentJournalSinkRecord(
                BaseRecordId(record.Correlation) + "/invocation",
                AgentJournalSchemaNames.LlmInvocationV1,
                record,
                record.Correlation);
        }

        public static AgentJournalSinkRecord Result(LlmResultRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return new AgentJournalSinkRecord(
                BaseRecordId(record.Correlation) + "/result",
                AgentJournalSchemaNames.LlmResultV1,
                record,
                record.Correlation);
        }

        public static AgentJournalSinkRecord MessageLink(
            MessageLinkRecord record,
            AgentJournalCorrelationIds correlation)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (correlation == null) throw new ArgumentNullException(nameof(correlation));
            return new AgentJournalSinkRecord(
                BaseRecordId(correlation) + "/message-link/" + record.SemanticEntryId,
                AgentJournalSchemaNames.MessageLinkV1,
                record,
                correlation);
        }


        public static AgentJournalSinkRecord RoleFactPolicyDecision(
            AgentJournalRoleFactPolicyDecisionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            AgentJournalRoleFactPolicyCorrelation policy = record.Correlation;
            return new AgentJournalSinkRecord(
                "agent-journal/"
                    + policy.GameRunId
                    + "/"
                    + policy.AgentSessionId
                    + "/policy/"
                    + policy.RequestId
                    + "/"
                    + policy.TurnId
                    + "/"
                    + record.FactSourceId,
                AgentJournalSchemaNames.RoleFactPolicyDecisionV1,
                record,
                correlation: null,
                policyCorrelation: policy);
        }

        private static string BaseRecordId(AgentJournalCorrelationIds correlation)
        {
            string ownerSegment = !string.IsNullOrWhiteSpace(correlation.AgentSessionId)
                ? correlation.AgentSessionId!
                : (correlation.Owner ?? "host_one_shot")
                    + "/"
                    + (correlation.ExecutionClass ?? correlation.JournalDestination ?? "unclassified");
            return "agent-journal/"
                + correlation.GameRunId
                + "/"
                + ownerSegment
                + "/"
                + correlation.InvocationId
                + "/"
                + (correlation.AttemptId ?? correlation.AttemptOrdinal.ToString(CultureInfo.InvariantCulture));
        }
    }
}

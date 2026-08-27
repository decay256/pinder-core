using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Pinder.Core.Conversation;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public sealed class AgentJournalCorrelationIds
    {
        public AgentJournalCorrelationIds(
            string gameRunId,
            string? agentSessionId,
            string invocationId,
            string operationId,
            int attemptOrdinal,
            string? attemptId = null,
            string? requestId = null,
            string? turnId = null,
            string? branchId = null,
            string? owner = null,
            string? journalDestination = null,
            string? executionClass = null,
            string? outputLinkId = null,
            IReadOnlyDictionary<string, string>? context = null)
        {
            GameRunId = gameRunId;
            AgentSessionId = agentSessionId;
            InvocationId = invocationId;
            OperationId = operationId;
            AttemptOrdinal = attemptOrdinal;
            AttemptId = attemptId;
            RequestId = requestId;
            TurnId = turnId;
            BranchId = branchId;
            Owner = owner;
            JournalDestination = journalDestination;
            ExecutionClass = executionClass;
            OutputLinkId = outputLinkId;
            Context = CopyContext(context);
        }

        public string GameRunId { get; }
        public string? AgentSessionId { get; }
        public string InvocationId { get; }
        public string OperationId { get; }
        public int AttemptOrdinal { get; }
        public string? AttemptId { get; }
        public string? RequestId { get; }
        public string? TurnId { get; }
        public string? BranchId { get; }
        public string? Owner { get; }
        public string? JournalDestination { get; }
        public string? ExecutionClass { get; }
        public string? OutputLinkId { get; }
        public IReadOnlyDictionary<string, string>? Context { get; }

        private static IReadOnlyDictionary<string, string>? CopyContext(
            IReadOnlyDictionary<string, string>? context)
        {
            if (context == null)
            {
                return null;
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in context)
            {
                copy[entry.Key] = entry.Value;
            }

            return copy;
        }
    }

    public sealed class AgentJournalOneShotContext
    {
        public const string GameRunBundleOwner = "game_run_bundle";

        public AgentJournalOneShotContext(
            string gameRunId,
            string operationId,
            string executionClass,
            string journalDestination,
            string modelId,
            string? turnId = null,
            string? outputLinkId = null,
            IReadOnlyDictionary<string, string>? context = null,
            string? requestId = null,
            string? invocationIdPrefix = null,
            string owner = GameRunBundleOwner)
        {
            RequireId(gameRunId, nameof(gameRunId));
            RequireId(operationId, nameof(operationId));
            RequireId(executionClass, nameof(executionClass));
            RequireId(journalDestination, nameof(journalDestination));
            RequireId(modelId, nameof(modelId));
            RequireId(owner, nameof(owner));
            RequireOpaqueId(gameRunId, nameof(gameRunId));
            RequireOpaqueId(operationId, nameof(operationId));
            RequireOpaqueId(executionClass, nameof(executionClass));
            RequireOpaqueId(journalDestination, nameof(journalDestination));
            RequireOpaqueId(turnId, nameof(turnId));
            RequireOpaqueId(outputLinkId, nameof(outputLinkId));
            RequireOpaqueId(requestId, nameof(requestId));
            RequireOpaqueId(invocationIdPrefix, nameof(invocationIdPrefix));
            RequireOpaqueId(owner, nameof(owner));
            if (context != null)
            {
                foreach (KeyValuePair<string, string> entry in context)
                {
                    RequireId(entry.Key, nameof(context));
                    RequireId(entry.Value, nameof(context));
                    RequireOpaqueId(entry.Key, nameof(context));
                    RequireOpaqueId(entry.Value, nameof(context));
                }
            }
            if (!string.Equals(owner, GameRunBundleOwner, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Game Run one-shot journal owner must be '" + GameRunBundleOwner + "'.",
                    nameof(owner));
            }

            GameRunId = gameRunId;
            OperationId = operationId;
            ExecutionClass = executionClass;
            JournalDestination = journalDestination;
            ModelId = modelId;
            TurnId = turnId;
            OutputLinkId = outputLinkId;
            RequestId = requestId;
            InvocationIdPrefix = string.IsNullOrWhiteSpace(invocationIdPrefix)
                ? operationId
                : invocationIdPrefix!;
            Owner = owner;
            Context = CopyContext(context);
        }

        public string GameRunId { get; }
        public string OperationId { get; }
        public string ExecutionClass { get; }
        public string JournalDestination { get; }
        public string ModelId { get; }
        public string? TurnId { get; }
        public string? OutputLinkId { get; }
        public string? RequestId { get; }
        public string InvocationIdPrefix { get; }
        public string Owner { get; }
        public IReadOnlyDictionary<string, string>? Context { get; }

        private static IReadOnlyDictionary<string, string>? CopyContext(
            IReadOnlyDictionary<string, string>? context)
        {
            if (context == null)
            {
                return null;
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in context)
            {
                copy[entry.Key] = entry.Value;
            }

            return copy;
        }

        public AgentJournalCorrelationIds ToCorrelation(
            int attemptOrdinal,
            string? invocationDiscriminator = null)
        {
            if (attemptOrdinal < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attemptOrdinal),
                    "Agent journal attempt ordinal must be positive.");
            }

            RequireOpaqueId(invocationDiscriminator, nameof(invocationDiscriminator));
            string attemptId = "attempt-" + attemptOrdinal.ToString(CultureInfo.InvariantCulture);
            string invocationId = string.IsNullOrWhiteSpace(invocationDiscriminator)
                ? InvocationIdPrefix + "." + attemptId
                : "call-" + invocationDiscriminator + "." + attemptId;
            return new AgentJournalCorrelationIds(
                GameRunId,
                agentSessionId: null,
                invocationId: invocationId,
                operationId: OperationId,
                attemptOrdinal: attemptOrdinal,
                attemptId: attemptId,
                requestId: RequestId,
                turnId: TurnId,
                branchId: null,
                owner: Owner,
                journalDestination: JournalDestination,
                executionClass: ExecutionClass,
                outputLinkId: OutputLinkId,
                context: Context);
        }

        private static void RequireId(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(parameterName + " is required.", parameterName);
            }
        }

        private static void RequireOpaqueId(string? value, string parameterName)
        {
            string? errorCode = AgentJournalSourceIdentifierPolicy.GetErrorCode(value);
            if (errorCode != null)
            {
                throw new ArgumentException(
                    parameterName + " violates the agent journal opaque identifier policy: " + errorCode + ".",
                    parameterName);
            }
        }
    }

    public sealed class AgentJournalSourceIdentity
    {
        public AgentJournalSourceIdentity(
            AgentJournalSourceKind kind,
            string sourceId,
            string keyPath,
            string? revision = null,
            string? contentHash = null,
            string? editorTargetId = null)
        {
            Kind = kind;
            SourceId = sourceId;
            KeyPath = keyPath;
            Revision = revision;
            ContentHash = contentHash;
            EditorTargetId = editorTargetId;
        }

        public AgentJournalSourceKind Kind { get; }
        public string SourceId { get; }
        public string KeyPath { get; }
        public string? Revision { get; }
        public string? ContentHash { get; }
        public string? EditorTargetId { get; }
    }

    public sealed class AgentJournalProvenanceRange
    {
        public AgentJournalProvenanceRange(
            string documentId,
            int startUtf16,
            int endUtf16,
            AgentJournalRangeKind rangeKind,
            AgentJournalRedactionClass redactionClass,
            AgentJournalSourceIdentity source)
        {
            DocumentId = documentId;
            StartUtf16 = startUtf16;
            EndUtf16 = endUtf16;
            RangeKind = rangeKind;
            RedactionClass = redactionClass;
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public string DocumentId { get; }
        public int StartUtf16 { get; }
        public int EndUtf16 { get; }
        public AgentJournalRangeKind RangeKind { get; }
        public AgentJournalRedactionClass RedactionClass { get; }
        public AgentJournalSourceIdentity Source { get; }
    }

    public sealed class AgentJournalInputDocument
    {
        public AgentJournalInputDocument(
            string documentId,
            AgentJournalInputRole role,
            string text,
            IReadOnlyList<AgentJournalProvenanceRange> ranges)
        {
            DocumentId = documentId;
            Role = role;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        }

        public string DocumentId { get; }
        public AgentJournalInputRole Role { get; }
        public string Text { get; }
        public IReadOnlyList<AgentJournalProvenanceRange> Ranges { get; }
    }

    public sealed class AgentJournalUsage
    {
        public AgentJournalUsage(
            int? inputTokens = null,
            int? outputTokens = null,
            int? totalTokens = null,
            int? cacheCreationInputTokens = null,
            int? cacheReadInputTokens = null)
        {
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            TotalTokens = totalTokens;
            CacheCreationInputTokens = cacheCreationInputTokens;
            CacheReadInputTokens = cacheReadInputTokens;
        }

        public int? InputTokens { get; }
        public int? OutputTokens { get; }
        public int? TotalTokens { get; }
        public int? CacheCreationInputTokens { get; }
        public int? CacheReadInputTokens { get; }
    }

    /// <summary>
    /// Text-free role-fact admission provenance attached to one exact provider invocation.
    /// </summary>
    public sealed class AgentJournalRoleFactAccessDecision
    {
        public const int CurrentSchemaVersion = 1;

        public AgentJournalRoleFactAccessDecision(
            bool admitted,
            string code,
            string factSourceId,
            PromptFactSourceKind factSourceKind,
            Guid subjectCharacterId,
            ConversationParticipantRole subjectRole,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility,
            int schemaVersion = CurrentSchemaVersion)
        {
            SchemaVersion = schemaVersion;
            Admitted = admitted;
            Code = code;
            FactSourceId = factSourceId;
            FactSourceKind = factSourceKind;
            SubjectCharacterId = subjectCharacterId;
            SubjectRole = subjectRole;
            RecipientCharacterId = recipientCharacterId;
            RecipientRole = recipientRole;
            Visibility = visibility;
        }

        public int SchemaVersion { get; }
        public bool Admitted { get; }
        public string Code { get; }
        public string FactSourceId { get; }
        public PromptFactSourceKind FactSourceKind { get; }
        public Guid SubjectCharacterId { get; }
        public ConversationParticipantRole SubjectRole { get; }
        public Guid RecipientCharacterId { get; }
        public ConversationParticipantRole RecipientRole { get; }
        public PromptFactVisibility Visibility { get; }

        public static AgentJournalRoleFactAccessDecision From(RoleFactAccessDecision decision)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            return new AgentJournalRoleFactAccessDecision(
                decision.Admitted,
                decision.Code,
                decision.FactSourceId,
                decision.FactSourceKind,
                decision.SubjectCharacterId,
                decision.SubjectRole,
                decision.RecipientCharacterId,
                decision.RecipientRole,
                decision.Visibility);
        }
    }


    public sealed class AgentJournalRoleFactPolicyCorrelation
    {
        public AgentJournalRoleFactPolicyCorrelation(
            string gameRunId,
            string agentSessionId,
            string requestId,
            string turnId,
            string? branchId = null)
        {
            GameRunId = gameRunId;
            AgentSessionId = agentSessionId;
            RequestId = requestId;
            TurnId = turnId;
            BranchId = branchId;
        }

        public string GameRunId { get; }
        public string AgentSessionId { get; }
        public string RequestId { get; }
        public string TurnId { get; }
        public string? BranchId { get; }
    }

    /// <summary>Durable text-free rejection provenance emitted before provider invocation.</summary>
    public sealed class AgentJournalRoleFactPolicyDecisionRecord
    {
        public const int CurrentSchemaVersion = 1;

        public AgentJournalRoleFactPolicyDecisionRecord(
            AgentJournalRoleFactPolicyCorrelation correlation,
            string operationKind,
            string factSourceId,
            PromptFactSourceKind factSourceKind,
            Guid ownerCharacterId,
            ConversationParticipantRole ownerRole,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility,
            string decisionCode,
            int schemaVersion = CurrentSchemaVersion)
        {
            SchemaVersion = schemaVersion;
            Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            OperationKind = operationKind;
            FactSourceId = factSourceId;
            FactSourceKind = factSourceKind;
            OwnerCharacterId = ownerCharacterId;
            OwnerRole = ownerRole;
            RecipientCharacterId = recipientCharacterId;
            RecipientRole = recipientRole;
            Visibility = visibility;
            DecisionCode = decisionCode;
        }

        public int SchemaVersion { get; }
        public AgentJournalRoleFactPolicyCorrelation Correlation { get; }
        public string OperationKind { get; }
        public string FactSourceId { get; }
        public PromptFactSourceKind FactSourceKind { get; }
        public Guid OwnerCharacterId { get; }
        public ConversationParticipantRole OwnerRole { get; }
        public Guid RecipientCharacterId { get; }
        public ConversationParticipantRole RecipientRole { get; }
        public PromptFactVisibility Visibility { get; }
        public string DecisionCode { get; }

        public static AgentJournalRoleFactPolicyDecisionRecord Rejected(
            RoleFactAccessDecision decision,
            GameRunAgentJournalContext context,
            string operationKind,
            int turn)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new AgentJournalRoleFactPolicyDecisionRecord(
                new AgentJournalRoleFactPolicyCorrelation(
                    context.GameRunId,
                    context.AgentSessionId,
                    context.RequestId ?? "request-unavailable",
                    "turn-" + turn.ToString(CultureInfo.InvariantCulture),
                    context.BranchId),
                operationKind,
                decision.FactSourceId,
                decision.FactSourceKind,
                decision.SubjectCharacterId,
                decision.SubjectRole,
                decision.RecipientCharacterId,
                decision.RecipientRole,
                decision.Visibility,
                decision.Code);
        }
    }

    public sealed class LlmInvocationRecord
    {
        public LlmInvocationRecord(
            AgentJournalCorrelationIds correlation,
            string modelId,
            string phase,
            IReadOnlyList<AgentJournalInputDocument> inputDocuments,
            string? createdAtUtc = null,
            IReadOnlyList<AgentJournalRoleFactAccessDecision>? roleFactAccessDecisions = null)
        {
            Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            ModelId = modelId;
            Phase = phase;
            InputDocuments = inputDocuments ?? throw new ArgumentNullException(nameof(inputDocuments));
            CreatedAtUtc = createdAtUtc;
            RoleFactAccessDecisions = roleFactAccessDecisions;
        }

        public AgentJournalCorrelationIds Correlation { get; }
        public string ModelId { get; }
        public string Phase { get; }
        public IReadOnlyList<AgentJournalInputDocument> InputDocuments { get; }
        public string? CreatedAtUtc { get; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<AgentJournalRoleFactAccessDecision>? RoleFactAccessDecisions { get; }
    }

    public sealed class LlmResultRecord
    {
        public LlmResultRecord(
            AgentJournalCorrelationIds correlation,
            AgentJournalTerminalStatus terminalStatus,
            string? outputText,
            AgentJournalUsage? usage,
            string? validationCode = null,
            string? errorCode = null,
            string? completedAtUtc = null,
            AgentJournalUsageStatus usageStatus = AgentJournalUsageStatus.Unknown,
            string? usageStatusReason = null,
            string? providerId = null,
            string? modelId = null,
            string? requestedProviderId = null,
            string? requestedModelId = null,
            long? observedStartedAtUnixMilliseconds = null,
            long? observedCompletedAtUnixMilliseconds = null,
            long? observedDurationMilliseconds = null,
            int? effectiveInputTokens = null,
            int? effectiveOutputTokens = null,
            int? effectiveTotalTokens = null,
            string? telemetryDiscrepancyCode = null)
        {
            Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            TerminalStatus = terminalStatus;
            OutputText = outputText;
            Usage = usage;
            UsageStatus = usageStatus;
            UsageStatusReason = usageStatusReason;
            ProviderId = providerId;
            ModelId = modelId;
            RequestedProviderId = requestedProviderId;
            RequestedModelId = requestedModelId;
            ObservedStartedAtUnixMilliseconds = observedStartedAtUnixMilliseconds;
            ObservedCompletedAtUnixMilliseconds = observedCompletedAtUnixMilliseconds;
            ObservedDurationMilliseconds = observedDurationMilliseconds;
            EffectiveInputTokens = effectiveInputTokens;
            EffectiveOutputTokens = effectiveOutputTokens;
            EffectiveTotalTokens = effectiveTotalTokens;
            TelemetryDiscrepancyCode = telemetryDiscrepancyCode;
            ValidationCode = validationCode;
            ErrorCode = errorCode;
            CompletedAtUtc = completedAtUtc;
        }

        public AgentJournalCorrelationIds Correlation { get; }
        public AgentJournalTerminalStatus TerminalStatus { get; }
        public string? OutputText { get; }
        public AgentJournalUsage? Usage { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public AgentJournalUsageStatus UsageStatus { get; }

        public string? UsageStatusReason { get; }
        public string? ProviderId { get; }
        public string? ModelId { get; }
        public string? RequestedProviderId { get; }
        public string? RequestedModelId { get; }
        public long? ObservedStartedAtUnixMilliseconds { get; }
        public long? ObservedCompletedAtUnixMilliseconds { get; }
        public long? ObservedDurationMilliseconds { get; }
        public int? EffectiveInputTokens { get; }
        public int? EffectiveOutputTokens { get; }
        public int? EffectiveTotalTokens { get; }
        public string? TelemetryDiscrepancyCode { get; }

        public string? ValidationCode { get; }
        public string? ErrorCode { get; }
        public string? CompletedAtUtc { get; }
    }

    public sealed class MessageLinkRecord
    {
        public MessageLinkRecord(
            string semanticEntryId,
            string invocationId,
            string agentSessionId,
            string? turnId = null,
            string? branchId = null)
        {
            SemanticEntryId = semanticEntryId;
            InvocationId = invocationId;
            AgentSessionId = agentSessionId;
            TurnId = turnId;
            BranchId = branchId;
        }

        public string SemanticEntryId { get; }
        public string InvocationId { get; }
        public string AgentSessionId { get; }
        public string? TurnId { get; }
        public string? BranchId { get; }
    }
}

using System;
using System.Collections.Generic;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public sealed class AgentJournalCorrelationIds
    {
        public AgentJournalCorrelationIds(
            string gameRunId,
            string agentSessionId,
            string invocationId,
            string operationId,
            int attemptOrdinal,
            string? attemptId = null,
            string? requestId = null,
            string? turnId = null,
            string? branchId = null)
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
        }

        public string GameRunId { get; }
        public string AgentSessionId { get; }
        public string InvocationId { get; }
        public string OperationId { get; }
        public int AttemptOrdinal { get; }
        public string? AttemptId { get; }
        public string? RequestId { get; }
        public string? TurnId { get; }
        public string? BranchId { get; }
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
        public AgentJournalUsage(int? inputTokens = null, int? outputTokens = null, int? totalTokens = null)
        {
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            TotalTokens = totalTokens;
        }

        public int? InputTokens { get; }
        public int? OutputTokens { get; }
        public int? TotalTokens { get; }
    }

    public sealed class LlmInvocationRecord
    {
        public LlmInvocationRecord(
            AgentJournalCorrelationIds correlation,
            string modelId,
            string phase,
            IReadOnlyList<AgentJournalInputDocument> inputDocuments,
            string? createdAtUtc = null)
        {
            Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            ModelId = modelId;
            Phase = phase;
            InputDocuments = inputDocuments ?? throw new ArgumentNullException(nameof(inputDocuments));
            CreatedAtUtc = createdAtUtc;
        }

        public AgentJournalCorrelationIds Correlation { get; }
        public string ModelId { get; }
        public string Phase { get; }
        public IReadOnlyList<AgentJournalInputDocument> InputDocuments { get; }
        public string? CreatedAtUtc { get; }
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
            string? completedAtUtc = null)
        {
            Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
            TerminalStatus = terminalStatus;
            OutputText = outputText;
            Usage = usage;
            ValidationCode = validationCode;
            ErrorCode = errorCode;
            CompletedAtUtc = completedAtUtc;
        }

        public AgentJournalCorrelationIds Correlation { get; }
        public AgentJournalTerminalStatus TerminalStatus { get; }
        public string? OutputText { get; }
        public AgentJournalUsage? Usage { get; }
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

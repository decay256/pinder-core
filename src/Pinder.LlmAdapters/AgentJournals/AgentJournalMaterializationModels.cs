using System;
using System.Collections.Generic;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters.AgentJournals
{
    public enum AgentJournalMaterializationStatus
    {
        Materialized,
        UnsupportedFormat,
        MalformedPayload,
        InvalidSnapshot,
        Failed,
    }

    public enum AgentJournalEntryKind
    {
        Message,
        CustomEntry,
        CustomMessage,
        Compaction,
        BranchSummary,
        Label,
        ModelChange,
        ActiveToolsChange,
        ThinkingLevelChange,
        SessionInfo,
        Leaf,
        Unknown,
    }

    public sealed class AgentJournalMaterializationResult
    {
        public AgentJournalMaterializationResult(
            AgentJournalMaterializationStatus status,
            string snapshotFormat,
            NormalizedAgentJournal? journal,
            IReadOnlyList<AgentJournalMaterializationNotice> notices)
        {
            Status = status;
            SnapshotFormat = snapshotFormat ?? string.Empty;
            Journal = journal;
            Notices = notices ?? throw new ArgumentNullException(nameof(notices));
        }

        public AgentJournalMaterializationStatus Status { get; }
        public string SnapshotFormat { get; }
        public NormalizedAgentJournal? Journal { get; }
        public IReadOnlyList<AgentJournalMaterializationNotice> Notices { get; }
        public bool IsMaterialized => Status == AgentJournalMaterializationStatus.Materialized && Journal != null;

        public static AgentJournalMaterializationResult Materialized(
            string snapshotFormat,
            NormalizedAgentJournal journal,
            IReadOnlyList<AgentJournalMaterializationNotice> notices)
            => new AgentJournalMaterializationResult(
                AgentJournalMaterializationStatus.Materialized,
                snapshotFormat,
                journal,
                notices);

        public static AgentJournalMaterializationResult UnsupportedFormat(
            string snapshotFormat,
            string message)
            => Error(AgentJournalMaterializationStatus.UnsupportedFormat, snapshotFormat, "unsupported_format", message);

        public static AgentJournalMaterializationResult MalformedPayload(
            string snapshotFormat,
            string message)
            => Error(AgentJournalMaterializationStatus.MalformedPayload, snapshotFormat, "malformed_payload", message);

        public static AgentJournalMaterializationResult InvalidSnapshot(
            string snapshotFormat,
            string code,
            string message)
            => Error(AgentJournalMaterializationStatus.InvalidSnapshot, snapshotFormat, code, message);

        public static AgentJournalMaterializationResult Failed(
            string snapshotFormat,
            string message)
            => Error(AgentJournalMaterializationStatus.Failed, snapshotFormat, "materialization_failed", message);

        private static AgentJournalMaterializationResult Error(
            AgentJournalMaterializationStatus status,
            string snapshotFormat,
            string code,
            string message)
            => new AgentJournalMaterializationResult(
                status,
                snapshotFormat,
                null,
                new[] { new AgentJournalMaterializationNotice(code, message) });
    }

    public sealed class AgentJournalMaterializationNotice
    {
        public AgentJournalMaterializationNotice(
            string code,
            string message,
            string? entryId = null,
            string? customType = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            EntryId = entryId;
            CustomType = customType;
        }

        public string Code { get; }
        public string Message { get; }
        public string? EntryId { get; }
        public string? CustomType { get; }
    }

    public sealed class NormalizedAgentJournal
    {
        public NormalizedAgentJournal(
            string snapshotFormat,
            string agentSessionId,
            string? createdAtUtc,
            string? activeLeafEntryId,
            IReadOnlyList<string> activePathEntryIds,
            IReadOnlyList<NormalizedAgentJournalEntry> entries,
            IReadOnlyList<NormalizedAgentJournalBranch> branches)
        {
            SnapshotFormat = snapshotFormat ?? throw new ArgumentNullException(nameof(snapshotFormat));
            AgentSessionId = agentSessionId ?? throw new ArgumentNullException(nameof(agentSessionId));
            CreatedAtUtc = createdAtUtc;
            ActiveLeafEntryId = activeLeafEntryId;
            ActivePathEntryIds = activePathEntryIds ?? throw new ArgumentNullException(nameof(activePathEntryIds));
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            Branches = branches ?? throw new ArgumentNullException(nameof(branches));
        }

        public string SnapshotFormat { get; }
        public string AgentSessionId { get; }
        public string? CreatedAtUtc { get; }
        public string? ActiveLeafEntryId { get; }
        public IReadOnlyList<string> ActivePathEntryIds { get; }
        public IReadOnlyList<NormalizedAgentJournalEntry> Entries { get; }
        public IReadOnlyList<NormalizedAgentJournalBranch> Branches { get; }
    }

    public sealed class NormalizedAgentJournalEntry
    {
        public NormalizedAgentJournalEntry(
            int ordinal,
            string entryId,
            string? parentEntryId,
            string piType,
            AgentJournalEntryKind kind,
            string? timestampUtc,
            IReadOnlyList<string> childEntryIds,
            bool isActiveLeaf,
            bool isOnActivePath,
            NormalizedAgentJournalSemanticMessage? semanticMessage,
            NormalizedAgentJournalCustomEntry? customEntry,
            string? lifecycleLabel)
        {
            Ordinal = ordinal;
            EntryId = entryId ?? throw new ArgumentNullException(nameof(entryId));
            ParentEntryId = parentEntryId;
            PiType = piType ?? string.Empty;
            Kind = kind;
            TimestampUtc = timestampUtc;
            ChildEntryIds = childEntryIds ?? throw new ArgumentNullException(nameof(childEntryIds));
            IsActiveLeaf = isActiveLeaf;
            IsOnActivePath = isOnActivePath;
            SemanticMessage = semanticMessage;
            CustomEntry = customEntry;
            LifecycleLabel = lifecycleLabel;
        }

        public int Ordinal { get; }
        public string EntryId { get; }
        public string? ParentEntryId { get; }
        public string PiType { get; }
        public AgentJournalEntryKind Kind { get; }
        public string? TimestampUtc { get; }
        public IReadOnlyList<string> ChildEntryIds { get; }
        public bool IsActiveLeaf { get; }
        public bool IsOnActivePath { get; }
        public NormalizedAgentJournalSemanticMessage? SemanticMessage { get; }
        public NormalizedAgentJournalCustomEntry? CustomEntry { get; }

        /// <summary>
        /// Null unless a supported typed Pinder entry supplies lifecycle meaning.
        /// Structural branch facts alone are intentionally not promoted to lifecycle labels.
        /// </summary>
        public string? LifecycleLabel { get; }
    }

    public sealed class NormalizedAgentJournalSemanticMessage
    {
        public NormalizedAgentJournalSemanticMessage(string role, string text, long timestampUnixMs)
        {
            Role = role ?? throw new ArgumentNullException(nameof(role));
            Text = text ?? string.Empty;
            TimestampUnixMs = timestampUnixMs;
        }

        public string Role { get; }
        public string Text { get; }
        public long TimestampUnixMs { get; }
    }

    public sealed class NormalizedAgentJournalCustomEntry
    {
        public NormalizedAgentJournalCustomEntry(
            string customType,
            AgentJournalCompatibilityResult compatibility,
            LlmInvocationRecord? llmInvocation,
            LlmResultRecord? llmResult,
            MessageLinkRecord? messageLink)
        {
            CustomType = customType ?? string.Empty;
            Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
            LlmInvocation = llmInvocation;
            LlmResult = llmResult;
            MessageLink = messageLink;
        }

        public string CustomType { get; }
        public AgentJournalCompatibilityResult Compatibility { get; }
        public LlmInvocationRecord? LlmInvocation { get; }
        public LlmResultRecord? LlmResult { get; }
        public MessageLinkRecord? MessageLink { get; }
    }

    public sealed class NormalizedAgentJournalBranch
    {
        public NormalizedAgentJournalBranch(
            string? parentEntryId,
            IReadOnlyList<string> childEntryIds,
            IReadOnlyList<string> activeChildEntryIds)
        {
            ParentEntryId = parentEntryId;
            ChildEntryIds = childEntryIds ?? throw new ArgumentNullException(nameof(childEntryIds));
            ActiveChildEntryIds = activeChildEntryIds ?? throw new ArgumentNullException(nameof(activeChildEntryIds));
        }

        public string? ParentEntryId { get; }
        public IReadOnlyList<string> ChildEntryIds { get; }
        public IReadOnlyList<string> ActiveChildEntryIds { get; }
    }
}

using System.Collections.Generic;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public static class AgentJournalTestRecords
    {
        public static AgentJournalCorrelationIds Correlation(
            string invocationId = "invocation-001",
            int attemptOrdinal = 1)
            => new AgentJournalCorrelationIds(
                "game-run-001",
                "agent-session-datee",
                invocationId,
                "operation-dialogue-options",
                attemptOrdinal,
                attemptId: "attempt-001",
                requestId: "request-001",
                turnId: "turn-001",
                branchId: "branch-main");

        public static LlmInvocationRecord Invocation(
            string invocationId = "invocation-001",
            int attemptOrdinal = 1,
            IReadOnlyList<AgentJournalInputDocument>? documents = null)
            => new LlmInvocationRecord(
                Correlation(invocationId, attemptOrdinal),
                "test-model",
                "dialogue_options",
                documents ?? new[]
                {
                    Document("doc.system", "system text", Range("doc.system", 0, 11)),
                    Document("doc.user", "user text", Range("doc.user", 0, 9)),
                },
                "2026-08-15T22:30:00Z");

        public static LlmResultRecord Result()
            => new LlmResultRecord(
                Correlation(),
                AgentJournalTerminalStatus.Succeeded,
                "assistant text",
                new AgentJournalUsage(10, 3, 13),
                validationCode: "accepted",
                completedAtUtc: "2026-08-15T22:30:01Z");

        public static MessageLinkRecord MessageLink()
            => new MessageLinkRecord(
                "semantic-entry-001",
                "invocation-001",
                "agent-session-datee",
                turnId: "turn-001",
                branchId: "branch-main");

        public static AgentJournalInputDocument Document(string documentId, string text, params AgentJournalProvenanceRange[] ranges)
            => new AgentJournalInputDocument(documentId, AgentJournalInputRole.System, text, ranges);

        public static AgentJournalProvenanceRange Range(string documentId, int start, int end)
            => new AgentJournalProvenanceRange(
                documentId,
                start,
                end,
                AgentJournalRangeKind.Configured,
                AgentJournalRedactionClass.SafeMetadata,
                new AgentJournalSourceIdentity(
                    AgentJournalSourceKind.Configuration,
                    "prompt.catalog",
                    "dialogue.system",
                    revision: "rev-001",
                    contentHash: "sha256:abcdef",
                    editorTargetId: "prompt-catalog-dialogue-system"));
    }
}


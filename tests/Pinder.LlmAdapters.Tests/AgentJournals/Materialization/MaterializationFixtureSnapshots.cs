using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Pi.AI;
using Pi.Agent.Core;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Materialization
{
    internal static class MaterializationFixtureSnapshots
    {
        public static LlmConversationSessionSnapshot SupportedBranchedSnapshot()
            => Supported("supported-branched.snapshot.json");

        public static LlmConversationSessionSnapshot EmptySnapshot()
            => Supported("empty.snapshot.json");

        public static LlmConversationSessionSnapshot InvalidParentageSnapshot()
            => Supported("invalid-parentage.snapshot.json");

        public static LlmConversationSessionSnapshot InvalidKnownEntrySnapshot()
            => Supported("invalid-known-entry.snapshot.json");

        public static LlmConversationSessionSnapshot CycleSnapshot()
            => Supported("cycle.snapshot.json");

        public static LlmConversationSessionSnapshot ChildBeforeParentSnapshot()
            => Supported("child-before-parent.snapshot.json");

        public static LlmConversationSessionSnapshot SelfParentSnapshot()
            => Supported("self-parent.snapshot.json");

        public static LlmConversationSessionSnapshot DuplicateIdsSnapshot()
            => Supported("duplicate-ids.snapshot.json");

        public static LlmConversationSessionSnapshot AmbiguousRootsSnapshot()
            => Supported("ambiguous-roots.snapshot.json");

        public static SessionSnapshot BuildSupportedBranchedSessionSnapshot()
            => Snapshot(
                "agent-session-fixture",
                new SessionTreeEntry[]
                {
                    UserEntry("entry-user-root", null, 1, "player asks a question"),
                    AssistantEntry("entry-main-assistant", "entry-user-root", 2, "main answer"),
                    InvocationEntry("entry-invocation", "entry-main-assistant", 3),
                    ResultEntry("entry-result", "entry-invocation", 4),
                    MessageLinkEntry("entry-link", "entry-result", 5),
                    AssistantEntry("entry-alt-assistant", "entry-user-root", 6, "alternate answer"),
                    UnknownPinderEntry("entry-alt-unknown", "entry-alt-assistant", 7),
                });

        public static SessionSnapshot BuildEmptySessionSnapshot()
            => Snapshot("empty-session-fixture", Array.Empty<SessionTreeEntry>());

        public static SessionSnapshot BuildInvalidParentageSessionSnapshot()
            => Snapshot(
                "invalid-parentage-fixture",
                new SessionTreeEntry[]
                {
                    UserEntry("entry-orphan", "missing-parent", 1, "orphan"),
                });

        public static SessionSnapshot BuildInvalidKnownEntrySessionSnapshot()
            => Snapshot(
                "invalid-known-fixture",
                new SessionTreeEntry[]
                {
                    new CustomEntry(
                        "entry-invalid-known",
                        null!,
                        Timestamp(1),
                        AgentJournalSchemaNames.LlmInvocationV1,
                        JsonNode.Parse(@"{""correlation"":{""game_run_id"":""game"",""agent_session_id"":""session"",""invocation_id"":""inv"",""operation_id"":""op"",""attempt_ordinal"":1,""attempt_id"":""attempt""},""phase"":""phase"",""input_documents"":[]}")),
                });

        public static string SerializeSnapshot(SessionSnapshot snapshot)
            => SessionJsonCodec.SerializeSnapshot(snapshot);

        private static LlmConversationSessionSnapshot Supported(string fileName)
            => new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                MaterializationFixtureFiles.ReadSnapshot(fileName));

        private static SessionSnapshot Snapshot(string sessionId, IReadOnlyList<SessionTreeEntry> entries)
            => new SessionSnapshot
            {
                Metadata = new SessionMetadata
                {
                    Id = sessionId,
                    CreatedAt = "2026-08-15T22:30:00Z",
                },
                Entries = entries,
            };

        private static MessageEntry UserEntry(string id, string? parentId, int timestamp, string text)
            => new MessageEntry(
                id,
                parentId!,
                Timestamp(timestamp),
                AgentMessage.FromMessage(new UserMessage(text, timestamp)));

        private static MessageEntry AssistantEntry(string id, string? parentId, int timestamp, string text)
            => new MessageEntry(
                id,
                parentId!,
                Timestamp(timestamp),
                AgentMessage.FromMessage(new AssistantMessage(
                    new IAssistantMessageContent[] { new TextContent(text) },
                    new Api("pinder-fixture"),
                    new ProviderId("pinder"),
                    "fixture-model",
                    Usage.Zero,
                    StopReason.Stop,
                    timestamp)));

        private static CustomEntry InvocationEntry(string id, string parentId, int timestamp)
        {
            CustomEntry entry = new PiAgentJournalEntryCodec().Encode(AgentJournalAdapterTestRecords.Invocation());
            entry.Id = id;
            entry.ParentId = parentId;
            entry.Timestamp = Timestamp(timestamp);
            return entry;
        }

        private static CustomEntry ResultEntry(string id, string parentId, int timestamp)
        {
            CustomEntry entry = new PiAgentJournalEntryCodec().Encode(AgentJournalAdapterTestRecords.Result());
            entry.Id = id;
            entry.ParentId = parentId;
            entry.Timestamp = Timestamp(timestamp);
            return entry;
        }

        private static CustomEntry MessageLinkEntry(string id, string parentId, int timestamp)
        {
            CustomEntry entry = new PiAgentJournalEntryCodec().Encode(AgentJournalAdapterTestRecords.MessageLink());
            entry.Id = id;
            entry.ParentId = parentId;
            entry.Timestamp = Timestamp(timestamp);
            return entry;
        }

        private static CustomEntry UnknownPinderEntry(string id, string parentId, int timestamp)
            => new CustomEntry(
                id,
                parentId,
                Timestamp(timestamp),
                "pinder.future-lifecycle.v9",
                JsonNode.Parse(@"{""future_field"":""kept"",""lifecycle"":""adopted-looking-but-unsupported""}"));

        private static string Timestamp(int seconds)
            => "2026-08-15T22:30:0" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "Z";
    }
}

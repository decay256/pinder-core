using System;
using System.Linq;
using System.Threading.Tasks;
using Pi.Agent.Core;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Recording
{
    public sealed class PiAgentJournalProjectionSinkTests
    {
        [Fact]
        public async Task RecorderProjection_AppendsJournalCustomEntriesWithoutProviderContext()
        {
            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                snapshot: null,
                Array.Empty<ConversationMessage>(),
                "datee");
            var projector = new PiAgentJournalProjectionSink(session.Session);
            var recorder = new AgentJournalRecorder(Context(projector));

            AgentJournalAttempt attempt = await recorder.StartAsync();
            await attempt.CompleteAcceptedAsync("assistant text", new AgentJournalUsage(2, 3, 5), "semantic-entry-001");

            var entries = await session.Session.GetEntriesAsync();
            var customEntries = entries.OfType<CustomEntry>().ToArray();
            Assert.Equal(
                new[]
                {
                    AgentJournalSchemaNames.LlmInvocationV1,
                    AgentJournalSchemaNames.LlmResultV1,
                    AgentJournalSchemaNames.MessageLinkV1,
                },
                customEntries.Select(entry => entry.CustomType).ToArray());

            var codec = new PiAgentJournalEntryCodec();
            Assert.Equal("invocation-001", Assert.IsType<LlmInvocationRecord>(codec.Decode(customEntries[0]).Record).Correlation.InvocationId);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, Assert.IsType<LlmResultRecord>(codec.Decode(customEntries[1]).Record).TerminalStatus);
            Assert.Equal("semantic-entry-001", Assert.IsType<MessageLinkRecord>(codec.Decode(customEntries[2]).Record).SemanticEntryId);

            var context = await session.Session.BuildContextAsync(new SessionContextBuildOptions
            {
                EntryProjectors = PiAgentJournalRegistry.CreateZeroContextProjectors(),
            });
            Assert.Empty(context.Messages);
        }

        [Fact]
        public async Task NullAgentSession_SkipsPiProjectionButStillAllowsHostSink()
        {
            var host = new RecordingHostSink();
            var recorder = new AgentJournalRecorder(Context(new PiAgentJournalProjectionSink(null), host));

            await (await recorder.StartAsync()).CompleteProviderFailedAsync("provider_failed");

            Assert.Equal(2, host.Count);
        }

        private static AgentJournalRecorderContext Context(
            IAgentJournalProjectionSink projector,
            IAgentJournalSink? hostSink = null)
            => new AgentJournalRecorderContext(
                new AgentJournalCorrelationIds(
                    "game-run-001",
                    "agent-session-datee",
                    "invocation-001",
                    "operation-dialogue-options",
                    1,
                    attemptId: "attempt-001",
                    requestId: "request-001",
                    turnId: "turn-001",
                    branchId: "branch-main"),
                "test-model",
                "dialogue_options",
                new[] { Document("doc.user", "hello") })
            {
                PiProjectionSink = projector,
                HostSink = hostSink,
                Clock = () => new DateTimeOffset(2026, 8, 15, 22, 30, 0, TimeSpan.Zero),
            };

        private static AgentJournalInputDocument Document(string id, string text)
            => new AgentJournalInputDocument(
                id,
                AgentJournalInputRole.User,
                text,
                new[]
                {
                    new AgentJournalProvenanceRange(
                        id,
                        0,
                        text.Length,
                        AgentJournalRangeKind.RuntimeGenerated,
                        AgentJournalRedactionClass.SafeMetadata,
                        new AgentJournalSourceIdentity(
                            AgentJournalSourceKind.RuntimeGenerated,
                            "runtime.prompt",
                            "dialogue.user")),
                });

        private sealed class RecordingHostSink : IAgentJournalSink
        {
            public int Count { get; private set; }

            public Task PersistAsync(AgentJournalSinkRecord record, System.Threading.CancellationToken cancellationToken)
            {
                Count++;
                return Task.CompletedTask;
            }
        }
    }
}

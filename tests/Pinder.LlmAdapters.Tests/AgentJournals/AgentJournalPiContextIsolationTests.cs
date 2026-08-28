using Xunit;
using System.Linq;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters.Tests.AgentJournals
{
    public sealed class AgentJournalPiContextIsolationTests
    {
        [Fact]
        public void DiagnosticEntries_ContributeZeroMessagesThroughPiSessionContextBuilder()
        {
            var codec = new PiAgentJournalEntryCodec();
            var entries = new SessionTreeEntry[]
            {
                WithEnvelope(codec.Encode(AgentJournalAdapterTestRecords.Invocation()), "journal-invocation"),
                WithEnvelope(codec.Encode(AgentJournalAdapterTestRecords.Result()), "journal-result"),
                WithEnvelope(codec.Encode(AgentJournalAdapterTestRecords.MessageLink()), "journal-link"),
                WithEnvelope(codec.Encode(PolicyDecision()), "journal-policy-decision"),
            };
            var options = new SessionContextBuildOptions
            {
                EntryProjectors = PiAgentJournalRegistry.CreateZeroContextProjectors(),
            };

            var context = SessionContextBuilder.BuildSessionContext(entries, options);

            Assert.Equal(5, options.EntryProjectors.Count);
            Assert.Empty(context.Messages);
            Assert.DoesNotContain("assistant text", string.Join("", context.Messages.Select(message => message.ToString())));
        }

        private static AgentJournalRoleFactPolicyDecisionRecord PolicyDecision()
            => new AgentJournalRoleFactPolicyDecisionRecord(
                new AgentJournalRoleFactPolicyCorrelation(
                    "game-run-001",
                    "agent-session-datee",
                    "request-001",
                    "turn-001",
                    "branch-main"),
                "dialogue_options",
                "character:bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb:cognitive-subtext:turn-1",
                Pinder.Core.Conversation.PromptFactSourceKind.CognitiveSubtext,
                System.Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Pinder.Core.Conversation.ConversationParticipantRole.Datee,
                System.Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Pinder.Core.Conversation.ConversationParticipantRole.PlayerAvatar,
                Pinder.Core.Conversation.PromptFactVisibility.PrivateToSubject,
                "denied.private_to_subject");

        private static CustomEntry WithEnvelope(CustomEntry entry, string id)
        {
            entry.Id = id;
            entry.Timestamp = "2026-08-15T22:30:00Z";
            return entry;
        }
    }
}

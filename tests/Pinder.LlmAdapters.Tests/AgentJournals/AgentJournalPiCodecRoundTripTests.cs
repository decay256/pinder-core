using Xunit;
using System.IO;
using System.Text.Json.Nodes;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters.Tests.AgentJournals
{
    public sealed class AgentJournalPiCodecRoundTripTests
    {
        private readonly PiAgentJournalEntryCodec _codec = new PiAgentJournalEntryCodec();

        [Fact]
        public void Invocation_RoundTripsThroughPiCustomEntryAndMatchesFixture()
        {
            var entry = _codec.Encode(AgentJournalAdapterTestRecords.Invocation());

            string json = CanonicalDataJson(entry);
            Assert.Equal(ReadFixture("llm-invocation.v1.json"), json);

            AddPiEnvelope(entry, "entry-invocation");
            var serialized = SessionJsonCodec.SerializeEntry(entry);
            var decodedEntry = Assert.IsType<CustomEntry>(SessionJsonCodec.DeserializeEntry(serialized));
            var decoded = _codec.Decode(decodedEntry);

            var record = Assert.IsType<LlmInvocationRecord>(decoded.Record);
            Assert.Equal("invocation-001", record.Correlation.InvocationId);
            Assert.Equal("system text", record.InputDocuments[0].Text);
        }

        [Fact]
        public void Result_RoundTripsThroughPiCustomEntryAndMatchesFixture()
        {
            var entry = _codec.Encode(AgentJournalAdapterTestRecords.Result());

            Assert.Equal(ReadFixture("llm-result.v1.json"), CanonicalDataJson(entry));

            AddPiEnvelope(entry, "entry-result");
            var decodedEntry = Assert.IsType<CustomEntry>(SessionJsonCodec.DeserializeEntry(SessionJsonCodec.SerializeEntry(entry)));
            var decoded = _codec.Decode(decodedEntry);

            var record = Assert.IsType<LlmResultRecord>(decoded.Record);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, record.TerminalStatus);
            Assert.Equal(13, record.Usage!.TotalTokens);
        }

        [Fact]
        public void MessageLink_RoundTripsThroughPiCustomEntryAndMatchesFixture()
        {
            var entry = _codec.Encode(AgentJournalAdapterTestRecords.MessageLink());

            Assert.Equal(ReadFixture("message-link.v1.json"), CanonicalDataJson(entry));

            AddPiEnvelope(entry, "entry-link");
            var decodedEntry = Assert.IsType<CustomEntry>(SessionJsonCodec.DeserializeEntry(SessionJsonCodec.SerializeEntry(entry)));
            var decoded = _codec.Decode(decodedEntry);

            var record = Assert.IsType<MessageLinkRecord>(decoded.Record);
            Assert.Equal("semantic-entry-001", record.SemanticEntryId);
        }

        private static void AddPiEnvelope(CustomEntry entry, string id)
        {
            entry.Id = id;
            entry.ParentId = "parent-entry";
            entry.Timestamp = "2026-08-15T22:30:00Z";
        }

        private static string CanonicalDataJson(CustomEntry entry)
            => ((JsonNode)entry.Data).ToJsonString();

        private static string ReadFixture(string name)
            => File.ReadAllText(Path.Combine("Fixtures", "AgentJournals", name)).Trim();
    }
}


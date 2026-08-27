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
        public void Result_RoundTripsCompleteCacheUsage()
        {
            var entry = _codec.Encode(new LlmResultRecord(
                AgentJournalAdapterTestRecords.Correlation(),
                AgentJournalTerminalStatus.Succeeded,
                "assistant text",
                new AgentJournalUsage(10, 3, 13, cacheCreationInputTokens: 2, cacheReadInputTokens: 4),
                validationCode: "accepted",
                completedAtUtc: "2026-08-15T22:30:01Z",
                usageStatus: AgentJournalUsageStatus.Complete));

            string json = CanonicalDataJson(entry);
            Assert.Contains("\"cache_creation_input_tokens\":2", json);
            Assert.Contains("\"cache_read_input_tokens\":4", json);
            Assert.Contains("\"usage_status\":\"complete\"", json);

            AddPiEnvelope(entry, "entry-result-cache");
            var decodedEntry = Assert.IsType<CustomEntry>(SessionJsonCodec.DeserializeEntry(SessionJsonCodec.SerializeEntry(entry)));
            var decoded = _codec.Decode(decodedEntry);
            var record = Assert.IsType<LlmResultRecord>(decoded.Record);
            Assert.Equal(2, record.Usage!.CacheCreationInputTokens);
            Assert.Equal(4, record.Usage.CacheReadInputTokens);
        }

        [Fact]
        public void Result_DecodesOldRecordsWithoutCacheUsageFields()
        {
            var data = JsonNode.Parse(@"{""correlation"":{""game_run_id"":""game-run-001"",""agent_session_id"":""agent-session-datee"",""invocation_id"":""invocation-001"",""operation_id"":""operation-dialogue-options"",""attempt_ordinal"":1,""attempt_id"":""attempt-001"",""request_id"":""request-001"",""turn_id"":""turn-001"",""branch_id"":""branch-main""},""terminal_status"":""succeeded"",""output_text"":""assistant text"",""usage"":{""input_tokens"":10,""output_tokens"":3,""total_tokens"":13},""validation_code"":""accepted"",""completed_at_utc"":""2026-08-15T22:30:01Z""}")!;
            var entry = new CustomEntry(null, null, null, AgentJournalSchemaNames.LlmResultV1, data);

            var decoded = _codec.Decode(entry);

            var record = Assert.IsType<LlmResultRecord>(decoded.Record);
            Assert.Equal(10, record.Usage!.InputTokens);
            Assert.Equal(3, record.Usage.OutputTokens);
            Assert.Null(record.Usage.CacheCreationInputTokens);
            Assert.Null(record.Usage.CacheReadInputTokens);
            Assert.Equal(AgentJournalUsageStatus.Unknown, record.UsageStatus);
            Assert.Null(record.ProviderId);
            Assert.Null(record.ModelId);
            Assert.Null(record.RequestedProviderId);
            Assert.Null(record.RequestedModelId);
            Assert.Null(record.ObservedStartedAtUnixMilliseconds);
            Assert.Null(record.ObservedCompletedAtUnixMilliseconds);
            Assert.Null(record.ObservedDurationMilliseconds);
            Assert.Null(record.EffectiveInputTokens);
            Assert.Null(record.EffectiveOutputTokens);
            Assert.Null(record.EffectiveTotalTokens);
            Assert.Null(record.TelemetryDiscrepancyCode);
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


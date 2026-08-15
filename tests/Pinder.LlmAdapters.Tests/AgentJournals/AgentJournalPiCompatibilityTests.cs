using Xunit;
using Newtonsoft.Json.Linq;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters.Tests.AgentJournals
{
    public sealed class AgentJournalPiCompatibilityTests
    {
        [Fact]
        public void UnknownPinderVersion_IsPreservedAsBoundedOpaqueJsonWithWarning()
        {
            var codec = new PiAgentJournalEntryCodec();
            var entry = new CustomEntry(
                "custom-1",
                null,
                "2026-08-15T22:30:00Z",
                "pinder.llm-invocation.v2",
                JObject.Parse(@"{""future_field"":""future-value""}"));

            var result = codec.Decode(entry);

            Assert.Null(result.Record);
            Assert.Equal(AgentJournalCompatibilityKind.UnknownPinderVersion, result.Compatibility.Kind);
            Assert.Contains("future_field", result.Compatibility.OpaqueJson);
            Assert.Contains("Unknown Pinder", result.Compatibility.Warning);
        }

        [Fact]
        public void UnknownPinderVersion_TruncatesLargeOpaqueJson()
        {
            var codec = new PiAgentJournalEntryCodec();
            var entry = new CustomEntry(
                "custom-1",
                null,
                null,
                "pinder.future.v9",
                JObject.Parse(@"{""blob"":""" + new string('x', PiAgentJournalEntryCodec.MaxOpaqueJsonBytes * 2) + @"""}"));

            var result = codec.Decode(entry);

            Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Compatibility.OpaqueJson!) <= PiAgentJournalEntryCodec.MaxOpaqueJsonBytes);
        }

        [Fact]
        public void NonPinderCustomEntry_IsNotClaimed()
        {
            var codec = new PiAgentJournalEntryCodec();
            var entry = new CustomEntry("custom-1", null, null, "other.custom.v1", JObject.Parse(@"{""x"":1}"));

            var result = codec.Decode(entry);

            Assert.Equal(AgentJournalCompatibilityKind.NonPinderCustomEntry, result.Compatibility.Kind);
            Assert.Null(result.Compatibility.OpaqueJson);
        }
    }
}


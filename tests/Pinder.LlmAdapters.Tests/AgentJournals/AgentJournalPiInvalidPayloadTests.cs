using System.Linq;
using Newtonsoft.Json.Linq;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals
{
    public sealed class AgentJournalPiInvalidPayloadTests
    {
        [Theory]
        [InlineData(AgentJournalSchemaNames.LlmInvocationV1, @"{""correlation"":{""game_run_id"":""game"",""agent_session_id"":""session"",""invocation_id"":""inv"",""operation_id"":""op"",""attempt_ordinal"":1,""attempt_id"":""attempt""},""phase"":""phase"",""input_documents"":[]}", AgentJournalValidator.MissingId)]
        [InlineData(AgentJournalSchemaNames.LlmResultV1, @"{""correlation"":{""game_run_id"":""game"",""agent_session_id"":""session"",""invocation_id"":""inv"",""operation_id"":""op"",""attempt_ordinal"":1,""attempt_id"":""attempt""},""terminal_status"":""succeeded""}", AgentJournalValidator.InvalidStatusTransition)]
        [InlineData(AgentJournalSchemaNames.MessageLinkV1, @"{""semantic_entry_id"":""entry"",""invocation_id"":""inv""}", AgentJournalValidator.MissingId)]
        [InlineData(AgentJournalSchemaNames.RoleFactPolicyDecisionV1, @"{""schema_version"":1,""correlation"":{""game_run_id"":""game"",""agent_session_id"":""session"",""turn_id"":""turn-1""}}", AgentJournalValidator.MissingId)]
        public void EveryKnownV1Payload_IsValidatedBeforeKnown(
            string customType,
            string json,
            string expectedError)
        {
            var codec = new PiAgentJournalEntryCodec();
            var entry = new CustomEntry("custom-1", null, null, customType, JObject.Parse(json));

            var result = codec.Decode(entry);

            Assert.Null(result.Record);
            Assert.Equal(AgentJournalCompatibilityKind.Invalid, result.Compatibility.Kind);
            Assert.Contains(result.Compatibility.Errors, error => error.Code == expectedError);
            Assert.Contains(expectedError, result.Compatibility.Warning);
            Assert.NotNull(result.Compatibility.OpaqueJson);
        }

        [Fact]
        public void InvalidKnownInvocationWithRangeAndSourceFalsifiers_IsRejected()
        {
            const string json = @"{""correlation"":{""game_run_id"":""game"",""agent_session_id"":""session"",""invocation_id"":""inv"",""operation_id"":""op"",""attempt_ordinal"":1,""attempt_id"":""attempt""},""model_id"":""model"",""phase"":""phase"",""input_documents"":[{""document_id"":""doc"",""role"":""system"",""text"":""abc"",""ranges"":[{""document_id"":""other"",""start_utf16"":-1,""end_utf16"":99,""range_kind"":""configured"",""redaction_class"":""safe_metadata"",""source"":{""kind"":""configuration"",""source_id"":""/etc/passwd"",""key_path"":""prompt.key"",""editor_target_id"":""https://example.invalid/secret""}}]}]}";
            var entry = new CustomEntry("custom-1", null, null, AgentJournalSchemaNames.LlmInvocationV1, JObject.Parse(json));

            var result = new PiAgentJournalEntryCodec().Decode(entry);

            Assert.Equal(AgentJournalCompatibilityKind.Invalid, result.Compatibility.Kind);
            Assert.Null(result.Record);
            Assert.Contains(result.Compatibility.Errors, error => error.Code == AgentJournalValidator.RangeDocumentMismatch);
            Assert.Contains(result.Compatibility.Errors, error => error.Code == AgentJournalValidator.OutOfBoundsRange);
            Assert.Equal(2, result.Compatibility.Errors.Count(error => error.Code == AgentJournalValidator.ForbiddenSourceLink));
        }

        [Fact]
        public void MalformedKnownJson_ReturnsDeterministicInvalidResult()
        {
            var entry = new CustomEntry("custom-1", null, null, AgentJournalSchemaNames.LlmInvocationV1, "{");

            var result = new PiAgentJournalEntryCodec().Decode(entry);

            Assert.Equal(AgentJournalCompatibilityKind.Invalid, result.Compatibility.Kind);
            Assert.Null(result.Record);
            Assert.Single(result.Compatibility.Errors);
            Assert.Equal("invalid_json", result.Compatibility.Errors[0].Code);
            Assert.Equal("$", result.Compatibility.Errors[0].Path);
            Assert.Equal("Invalid known Pinder custom-entry payload: invalid_json@$.", result.Compatibility.Warning);
        }

        [Fact]
        public void IntegerEnumInKnownJson_IsRejectedInsteadOfActivated()
        {
            string json = AgentJournalJson.Serialize(AgentJournalAdapterTestRecords.Result()).Replace("\"succeeded\"", "999");
            var entry = new CustomEntry("custom-1", null, null, AgentJournalSchemaNames.LlmResultV1, JObject.Parse(json));

            var result = new PiAgentJournalEntryCodec().Decode(entry);

            Assert.Equal(AgentJournalCompatibilityKind.Invalid, result.Compatibility.Kind);
            Assert.Equal("invalid_json", result.Compatibility.Errors.Single().Code);
        }
    }
}

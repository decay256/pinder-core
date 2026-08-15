using System.Linq;
using Newtonsoft.Json.Linq;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals
{
    public sealed class AgentJournalPiUtf16BoundaryTests
    {
        [Fact]
        public void KnownInvocationSplittingSurrogatePair_DecodesAsInvalid()
        {
            const string json = @"{""correlation"":{""game_run_id"":""game"",""agent_session_id"":""session"",""invocation_id"":""inv"",""operation_id"":""op"",""attempt_ordinal"":1,""attempt_id"":""attempt""},""model_id"":""model"",""phase"":""phase"",""input_documents"":[{""document_id"":""doc"",""role"":""system"",""text"":""\ud83d\ude00"",""ranges"":[{""document_id"":""doc"",""start_utf16"":0,""end_utf16"":1,""range_kind"":""configured"",""redaction_class"":""safe_metadata"",""source"":{""kind"":""configuration"",""source_id"":""prompt.catalog"",""key_path"":""prompt.key""}},{""document_id"":""doc"",""start_utf16"":1,""end_utf16"":2,""range_kind"":""configured"",""redaction_class"":""safe_metadata"",""source"":{""kind"":""configuration"",""source_id"":""prompt.catalog"",""key_path"":""prompt.key""}}]}]}";
            var entry = new CustomEntry(
                "custom-1",
                null,
                null,
                AgentJournalSchemaNames.LlmInvocationV1,
                JObject.Parse(json));

            PiAgentJournalDecodeResult result = new PiAgentJournalEntryCodec().Decode(entry);

            Assert.Equal(AgentJournalCompatibilityKind.Invalid, result.Compatibility.Kind);
            Assert.Null(result.Record);
            Assert.Equal(
                2,
                result.Compatibility.Errors.Count(error => error.Code == AgentJournalValidator.SurrogateSplitRange));
        }
    }
}

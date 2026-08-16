using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Materialization
{
    public sealed class AgentJournalMaterializerCompatibilityTests
    {
        [Fact]
        public async Task UnsupportedFormat_ReturnsTypedCompatibilityWithoutLegacyImport()
        {
            var snapshot = new LlmConversationSessionSnapshot(
                "legacy.conversation-history.v1",
                MaterializationFixtureFiles.ReadSnapshot("unsupported.payload.json"));

            AgentJournalMaterializationResult result = await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(AgentJournalMaterializationStatus.UnsupportedFormat, result.Status);
            Assert.Null(result.Journal);
            Assert.Contains("Only PiAgentSessionV1", result.Notices.Single().Message);
            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("unsupported.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Fact]
        public async Task MalformedPayload_ReturnsTypedErrorWithoutPartialJournal()
        {
            var snapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                MaterializationFixtureFiles.ReadSnapshot("malformed.snapshot.json"));

            AgentJournalMaterializationResult result = await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(AgentJournalMaterializationStatus.MalformedPayload, result.Status);
            Assert.Null(result.Journal);
        }

        [Fact]
        public async Task EmptyValidSnapshot_MaterializesEmptyJournal()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.EmptySnapshot());

            Assert.Equal(AgentJournalMaterializationStatus.Materialized, result.Status);
            Assert.NotNull(result.Journal);
            Assert.Empty(result.Journal!.Entries);
            Assert.Null(result.Journal.ActiveLeafEntryId);
            Assert.Empty(result.Journal.ActivePathEntryIds);
            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("empty.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Fact]
        public async Task InvalidParentage_ReturnsTypedErrorWithoutPartialJournal()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.InvalidParentageSnapshot());

            Assert.Equal(AgentJournalMaterializationStatus.InvalidSnapshot, result.Status);
            Assert.Null(result.Journal);
            Assert.Equal("invalid_parentage", result.Notices.Single().Code);
            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("invalid-parentage.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Fact]
        public async Task InvalidKnownPinderEntry_StaysVisibleAsInvalidCompatibility()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.InvalidKnownEntrySnapshot());

            NormalizedAgentJournalEntry entry = result.Journal!.Entries.Single(e => e.EntryId == "entry-invalid-known");

            Assert.Equal(AgentJournalMaterializationStatus.Materialized, result.Status);
            Assert.NotNull(entry.CustomEntry);
            Assert.Null(entry.CustomEntry!.LlmInvocation);
            Assert.Equal(
                Pinder.Core.Diagnostics.AgentJournals.AgentJournalCompatibilityKind.Invalid,
                entry.CustomEntry.Compatibility.Kind);
            Assert.NotNull(entry.CustomEntry.Compatibility.OpaqueJson);
            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("invalid-known-entry.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Theory]
        [InlineData("cycle.snapshot.json", "cyclic_parentage")]
        [InlineData("self-parent.snapshot.json", "self_parentage")]
        [InlineData("ambiguous-roots.snapshot.json", "ambiguous_roots")]
        public async Task InvalidTreeShapes_ReturnTypedErrorsWithoutPartialJournal(
            string fixtureName,
            string expectedCode)
        {
            var snapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                MaterializationFixtureFiles.ReadSnapshot(fixtureName));

            AgentJournalMaterializationResult result = await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(AgentJournalMaterializationStatus.InvalidSnapshot, result.Status);
            Assert.Null(result.Journal);
            Assert.Equal(expectedCode, result.Notices.Single().Code);
        }

        [Fact]
        public async Task DuplicateIds_RejectedByPiCodecAsTypedMalformedErrorWithoutPartialJournal()
        {
            var snapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                MaterializationFixtureFiles.ReadSnapshot("duplicate-ids.snapshot.json"));

            AgentJournalMaterializationResult result = await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(AgentJournalMaterializationStatus.MalformedPayload, result.Status);
            Assert.Null(result.Journal);
            Assert.Equal("malformed_payload", result.Notices.Single().Code);
        }
    }
}

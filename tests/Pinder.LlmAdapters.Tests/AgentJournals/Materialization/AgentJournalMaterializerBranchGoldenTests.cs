using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Materialization
{
    public sealed class AgentJournalMaterializerBranchGoldenTests
    {
        [Fact]
        public async Task SupportedBranchedSnapshot_ProducesDeterministicJournalWithParentageBranchesAndActivePath()
        {
            LlmConversationSessionSnapshot snapshot = MaterializationFixtureSnapshots.SupportedBranchedSnapshot();

            AgentJournalMaterializationResult result = await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(AgentJournalMaterializationStatus.Materialized, result.Status);
            Assert.NotNull(result.Journal);
            Assert.Equal("agent-session-fixture", result.Journal!.AgentSessionId);
            Assert.Equal("entry-alt-unknown", result.Journal.ActiveLeafEntryId);
            Assert.Equal(
                new[] { "entry-user-root", "entry-alt-assistant", "entry-alt-unknown" },
                result.Journal.ActivePathEntryIds.ToArray());
            Assert.Equal(
                new[] { "entry-main-assistant", "entry-alt-assistant" },
                result.Journal.Branches.Single().ChildEntryIds.ToArray());
            Assert.Equal(new[] { "entry-alt-assistant" }, result.Journal.Branches.Single().ActiveChildEntryIds.ToArray());

            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("supported-branched.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Fact]
        public async Task KnownPinderEntries_DecodeToTypedReadModelRecords()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.SupportedBranchedSnapshot());

            NormalizedAgentJournalEntry invocationEntry = result.Journal!.Entries.Single(entry => entry.EntryId == "entry-invocation");
            NormalizedAgentJournalEntry resultEntry = result.Journal!.Entries.Single(entry => entry.EntryId == "entry-result");
            NormalizedAgentJournalEntry linkEntry = result.Journal!.Entries.Single(entry => entry.EntryId == "entry-link");

            Assert.NotNull(invocationEntry.CustomEntry!.LlmInvocation);
            Assert.Equal("invocation-001", invocationEntry.CustomEntry.LlmInvocation!.Correlation.InvocationId);
            Assert.NotNull(resultEntry.CustomEntry!.LlmResult);
            Assert.Equal("assistant text", resultEntry.CustomEntry.LlmResult!.OutputText);
            Assert.NotNull(linkEntry.CustomEntry!.MessageLink);
            Assert.Equal("semantic-entry-001", linkEntry.CustomEntry.MessageLink!.SemanticEntryId);
        }

        [Fact]
        public async Task UnknownPinderEntry_RemainsVisibleAsBoundedOpaqueJsonWithWarning()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.SupportedBranchedSnapshot());

            NormalizedAgentJournalEntry unknown = result.Journal!.Entries.Single(entry => entry.EntryId == "entry-alt-unknown");

            Assert.Contains(result.Notices, notice =>
                notice.EntryId == "entry-alt-unknown"
                && notice.CustomType == "pinder.future-lifecycle.v9");
            Assert.NotNull(unknown.CustomEntry);
            Assert.Contains("future_field", unknown.CustomEntry!.Compatibility.OpaqueJson);
        }

        [Fact]
        public async Task LifecycleLabels_AreNotInferredFromBranchShape()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.SupportedBranchedSnapshot());

            Assert.Contains(result.Journal!.Branches, branch => branch.ChildEntryIds.Count == 2);
            Assert.All(result.Journal.Entries, entry => Assert.Null(entry.LifecycleLabel));
            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("lifecycle-noninference.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Fact]
        public async Task Materialization_IsDeterministicAcrossRepeatedRuns()
        {
            LlmConversationSessionSnapshot snapshot = MaterializationFixtureSnapshots.SupportedBranchedSnapshot();

            string first = AgentJournalMaterializerJson.Serialize(await new AgentJournalMaterializer().MaterializeAsync(snapshot));
            string second = AgentJournalMaterializerJson.Serialize(await new AgentJournalMaterializer().MaterializeAsync(snapshot));

            Assert.Equal(first, second);
        }

        [Fact]
        public async Task ChildBeforeParentSerialization_NormalizesTreeAndActivePathRootToLeaf()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.ChildBeforeParentSnapshot());

            Assert.Equal(AgentJournalMaterializationStatus.Materialized, result.Status);
            Assert.Equal(
                new[] { "root", "child", "leaf", "sibling-early", "sibling-late" },
                result.Journal!.Entries.Select(entry => entry.EntryId).ToArray());
            Assert.Equal(new[] { "root", "child", "leaf" }, result.Journal.ActivePathEntryIds.ToArray());
            Assert.Equal(
                MaterializationFixtureFiles.ReadNormalized("child-before-parent.normalized.json"),
                AgentJournalMaterializerJson.Serialize(result));
        }

        [Fact]
        public async Task EquivalentTreeSerializationOrders_ProduceIdenticalNormalizedOrdering()
        {
            LlmConversationSessionSnapshot shuffled = MaterializationFixtureSnapshots.ChildBeforeParentSnapshot();
            var parentFirst = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                MaterializationFixtureFiles.ReadSnapshot("parent-first-equivalent.snapshot.json"));

            AgentJournalMaterializationResult shuffledResult = await new AgentJournalMaterializer().MaterializeAsync(shuffled);
            AgentJournalMaterializationResult parentFirstResult = await new AgentJournalMaterializer().MaterializeAsync(parentFirst);

            Assert.Equal(
                shuffledResult.Journal!.Entries.Select(entry => entry.EntryId),
                parentFirstResult.Journal!.Entries.Select(entry => entry.EntryId));
            Assert.Equal(shuffledResult.Journal.ActivePathEntryIds, parentFirstResult.Journal.ActivePathEntryIds);
        }
    }
}

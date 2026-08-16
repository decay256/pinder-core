using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Pi.Agent.Core;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Materialization
{
    public sealed class AgentJournalMaterializerSideEffectTests
    {
        [Fact]
        public async Task Materialization_LeavesInputPayloadByteForByteUnchanged()
        {
            LlmConversationSessionSnapshot snapshot = MaterializationFixtureSnapshots.SupportedBranchedSnapshot();
            string before = snapshot.Payload;

            await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(before, snapshot.Payload);
        }

        [Fact]
        public async Task Materialization_UsesIsolatedRestoreAndDoesNotWriteANewSnapshot()
        {
            LlmConversationSessionSnapshot snapshot = MaterializationFixtureSnapshots.SupportedBranchedSnapshot();
            AgentJournalMaterializationResult first = await new AgentJournalMaterializer().MaterializeAsync(snapshot);
            AgentJournalMaterializationResult second = await new AgentJournalMaterializer().MaterializeAsync(snapshot);

            Assert.Equal(
                AgentJournalMaterializerJson.Serialize(first),
                AgentJournalMaterializerJson.Serialize(second));
            Assert.DoesNotContain(first.Journal!.Entries, entry => entry.PiType == "session");
        }

        [Fact]
        public async Task NoProviderRuntimeGuard_MaterializesWithoutAnyTransportOrModel()
        {
            AgentJournalMaterializationResult result = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.SupportedBranchedSnapshot());

            Assert.True(result.IsMaterialized);
            Assert.Contains(result.Journal!.Entries, entry => entry.SemanticMessage != null);
        }

        [Fact]
        public void NoProviderStaticGuard_MaterializerDoesNotReferenceProviderOrContextBuilderAPIs()
        {
            string source = File.ReadAllText(Path.Combine(
                MaterializationFixtureFiles.RepositoryRoot,
                "src",
                "Pinder.LlmAdapters",
                "AgentJournals",
                "AgentJournalMaterializer.cs"));

            Assert.DoesNotContain("PiLlmTransport", source);
            Assert.DoesNotContain("PiProviderTransportFactory", source);
            Assert.DoesNotContain("BuildContextAsync", source);
            Assert.DoesNotContain("AgentHarness", source);
        }

        [Fact]
        public void PublicApi_DoesNotAcceptActiveSessionOrProviderParameters()
        {
            MethodInfo method = typeof(AgentJournalMaterializer).GetMethod(nameof(AgentJournalMaterializer.MaterializeAsync))!;
            ParameterInfo parameter = Assert.Single(method.GetParameters());

            Assert.Equal(typeof(LlmConversationSessionSnapshot), parameter.ParameterType);
            Assert.DoesNotContain("Transport", string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)));
            Assert.DoesNotContain("ISession", string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName)));
        }

        [Fact]
        public async Task RestoredStoreIsDisposedAfterErrorsAndSubsequentMaterializationStillWorks()
        {
            AgentJournalMaterializationResult invalid = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.InvalidParentageSnapshot());
            AgentJournalMaterializationResult valid = await new AgentJournalMaterializer()
                .MaterializeAsync(MaterializationFixtureSnapshots.EmptySnapshot());

            Assert.Equal(AgentJournalMaterializationStatus.InvalidSnapshot, invalid.Status);
            Assert.Equal(AgentJournalMaterializationStatus.Materialized, valid.Status);
        }

        [Fact]
        public void SnapshotFixturePayloads_ArePiCodecReadableWhereExpected()
        {
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("supported-branched.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("empty.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("invalid-parentage.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("invalid-known-entry.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("cycle.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("child-before-parent.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("parent-first-equivalent.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("self-parent.snapshot.json"));
            SessionJsonCodec.DeserializeSnapshot(MaterializationFixtureFiles.ReadSnapshot("ambiguous-roots.snapshot.json"));
        }
    }
}

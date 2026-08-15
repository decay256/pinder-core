using System.IO;
using System.Text.Json;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.TestCommon;
using Pinder.Core.Text;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class PromptTraceSourceIdentityResolverTests
    {
        [Fact]
        public void StructuralYamlFixture_ResolvesPathWithoutChangingTextOrOffsets()
        {
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "AgentJournals", "structural-prompt-trace.json")));
            JsonElement root = fixture.RootElement;
            string text = root.GetProperty("text").GetString()!;
            string sourceFile = root.GetProperty("source_file").GetString()!;
            string key = root.GetProperty("key").GetString()!;
            string expectedSourceId = root.GetProperty("expected_source_id").GetString()!;
            int start = root.GetProperty("start_utf16").GetInt32();
            int end = root.GetProperty("end_utf16").GetInt32();
            string structuralYaml = File.ReadAllText(Path.Combine(
                TestRepoLocator.FindRepoSubdir("data", "prompts"),
                "structural.yaml"));
            Assert.Contains("RULES", structuralYaml);
            Assert.Contains("IDENTITY", structuralYaml);

            var trace = new PromptTraceResult(
                text,
                new[] { new AnnotatedSpan(start, end, sourceFile, key) });
            var resolver = PromptTraceSourceIdentityTestResolver.Map(sourceFile, expectedSourceId);

            AgentJournalInputDocument document = trace.ToAgentJournalInputDocument(
                "doc.system",
                AgentJournalInputRole.System,
                resolver);

            Assert.Equal(text, document.Text);
            AgentJournalProvenanceRange range = Assert.Single(document.Ranges);
            Assert.Equal(start, range.StartUtf16);
            Assert.Equal(end, range.EndUtf16);
            Assert.Equal(expectedSourceId, range.Source.SourceId);
            string serialized = AgentJournalJson.Serialize(document);
            Assert.DoesNotContain(sourceFile, serialized);
            Assert.Contains(expectedSourceId, serialized);
        }

        [Fact]
        public void UnknownSource_FailsWithoutLeakingOrInventingAnIdentifier()
        {
            const string sourceFile = "data/prompts/unknown.yaml";
            var trace = new PromptTraceResult(
                "text",
                new[] { new AnnotatedSpan(0, 4, sourceFile, "prompt.key") });

            var error = Assert.Throws<PromptTraceSourceIdentityException>(() =>
                trace.ToAgentJournalInputDocument(
                    "doc.system",
                    AgentJournalInputRole.System,
                    PromptTraceSourceIdentityTestResolver.Empty));

            Assert.Equal(PromptTraceSourceIdentityException.UnmappedSourceIdentity, error.Code);
            Assert.DoesNotContain(sourceFile, error.Message);
        }
    }
}

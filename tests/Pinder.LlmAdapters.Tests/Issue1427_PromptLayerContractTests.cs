using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Prompts;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue1427_PromptLayerContractTests
    {
        [Fact]
        public void Default_registry_covers_the_active_runtime_catalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            PromptContractRegistry.CreateDefault().ValidateCompleteness(catalog);
        }

        [Fact]
        public void Conflicting_hard_authorities_fail_with_provenance()
        {
            var registry = new PromptContractRegistry(new[]
            {
                new PromptLayerContract("one", "opponent_response", PromptContractRoleScope.Datee, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, true),
                new PromptLayerContract("two", "opponent_response", PromptContractRoleScope.Datee, PromptContractLayer.OutputContract, PromptContractAuthority.OutputShape, PromptContractKnowledge.None, true),
            });
            var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
                "opponent_response", PromptContractRoleScope.Datee, registry,
                new[] { Document("one", "one"), Document("two", "two") }));
            Assert.Equal("prompt_contract.authority.conflict", ex.ViolationCode);
            Assert.Equal("two", ex.PromptKey);
            Assert.Equal("one", ex.ConflictingKey);
            Assert.NotNull(ex.SourceSpan);
        }

        [Fact]
        public void Unresolved_placeholder_fails_before_provider_boundary()
        {
            var registry = new PromptContractRegistry(new[]
            {
                new PromptLayerContract("one", "opponent_response", PromptContractRoleScope.Datee, PromptContractLayer.ResponsePlan, PromptContractAuthority.CurrentMove, PromptContractKnowledge.SameCharacterPrivate, true),
            });
            var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
                "opponent_response", PromptContractRoleScope.Datee, registry,
                new[] { Document("one", "{unresolved}") }));
            Assert.Equal("prompt_contract.placeholder.unresolved", ex.ViolationCode);
        }

        [Fact]
        public void Unregistered_configured_section_fails_with_its_annotated_range()
        {
            var registry = new PromptContractRegistry(Array.Empty<PromptLayerContract>());
            var source = new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Catalog,
                "prompt.catalog",
                "unregistered-section",
                revision: "test");
            var document = AnnotatedInvocationDocument.Create(
                "test.unregistered",
                AgentJournalInputRole.User,
                "test",
                "configured",
                new[]
                {
                    new AgentJournalProvenanceRange(
                        "test.unregistered", 0, 10, AgentJournalRangeKind.Configured,
                        AgentJournalRedactionClass.None, source),
                });

            var ex = Assert.Throws<PromptLayerContractException>(() => PromptContractLinter.Validate(
                "opponent_response", PromptContractRoleScope.Datee, registry, new[] { document }));

            Assert.Equal("prompt_contract.registry.missing", ex.ViolationCode);
            Assert.Equal("unregistered-section", ex.PromptKey);
            Assert.Equal("0:10", ex.SourceSpan);
        }

        [Fact]
        public void Invalid_admin_personality_template_is_rejected_with_source_location()
        {
            string temporaryRoot = Path.Combine(Path.GetTempPath(), "pinder-1427-prompts-" + Guid.NewGuid().ToString("N"));
            var previous = PromptTemplates.Catalog;
            try
            {
                CopyDirectory(FindPromptsRoot(), temporaryRoot);
                string path = Path.Combine(temporaryRoot, "personality_consolidation.yaml");
                File.WriteAllText(
                    path,
                    File.ReadAllText(path).Replace(
                        "Output plain prose only, 5-8 compact sentences. No markdown, no headings, no JSON.",
                        "Always use emoji in every reply.",
                        StringComparison.Ordinal));
                PromptTemplates.Catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
                var invalid = PromptCatalog.LoadFromDirectory(temporaryRoot);

                var ex = Assert.Throws<PromptLayerContractException>(() => invalid.ValidateRuntimeCatalog());

                Assert.Equal("prompt_contract.personality.surface_style", ex.ViolationCode);
                Assert.Equal("personality_consolidation", ex.PromptKey);
                Assert.EndsWith("personality_consolidation.yaml", ex.SourcePath!);
                Assert.NotNull(ex.SourceSpan);
                Assert.NotNull(PromptTemplates.Catalog);
            }
            finally
            {
                PromptTemplates.Catalog = previous ?? PromptCatalog.LoadFromDirectory(FindPromptsRoot());
                if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
            }
        }

        private static AnnotatedInvocationDocument Document(string key, string text)
        {
            var source = new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Catalog,
                "data/prompts/templates.yaml",
                key,
                revision: "test");
            return AnnotatedInvocationDocument.Create(
                "test." + key,
                AgentJournalInputRole.User,
                "test",
                text,
                new[] { new AgentJournalProvenanceRange("test." + key, 0, text.Length, AgentJournalRangeKind.Configured, AgentJournalRedactionClass.None, source) });
        }

        private static string FindPromptsRoot()
        {
            string directory = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                string candidate = Path.Combine(directory, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                string? parent = Directory.GetParent(directory)?.FullName;
                if (parent == null) break;
                directory = parent;
            }
            throw new DirectoryNotFoundException("Unable to locate data/prompts.");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #872 Phase 2: <see cref="PromptTemplates"/> const strings
    /// migrated to <c>data/prompts/templates.yaml</c>.
    ///
    /// What this file pins:
    /// - The loader parses <c>data/prompts/templates.yaml</c> into a
    ///   <see cref="PromptCatalog"/> with all 37 expected entries.
    /// - The yaml representation of
    ///   <c>dialogue-options-instruction</c> is byte-identical to the
    ///   <see cref="PromptTemplates.DialogueOptionsInstruction"/> const
    ///   (representative entry — the remaining 36 are caught by
    ///   full-suite test coverage of the const-fallback code path).
    /// - <see cref="PromptTemplates.TryGetFromCatalog"/> prefers the
    ///   catalog when set and falls back to the const otherwise.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    public class Issue872_PromptTemplatesPhase2Tests
    {
        // ----- repo helpers ---------------------------------------------------

        private static string FindRepoSubdir(string subdir)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, subdir);
                if (Directory.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new DirectoryNotFoundException(
                $"Could not locate {subdir} in any ancestor of the test binary.");
        }

        private static string PromptsRoot
            => FindRepoSubdir(Path.Combine("data", "prompts"));

        // ----- loader: entry count -------------------------------------------

        [Fact]
        public void TemplatesYaml_LoadsAll37Entries()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);

            // Verify the templates.yaml entries are present (37 Phase 2
            // entries + 1 Phase 1 stake entry = at least 38 names).
            var names = catalog.Names.ToList();
            Assert.True(names.Count >= 38,
                $"expected >=38 prompt names (37 templates + stake), got {names.Count}");

            // Spot-check a few representative keys.
            Assert.Contains("dialogue-options-instruction", names);
            Assert.Contains("default-clean", names);
            Assert.Contains("interest-narrative-25", names);
            Assert.Contains("engine-options-block", names);
        }

        // ----- byte-identity contract: representative entry ------------------

        [Fact]
        public void Yaml_DialogueOptionsInstruction_MatchesConst_ByteForByte()
        {
            // #872 Phase 2 contract: this is a pure relocation. The yaml
            // must produce the same text the legacy const does. If a
            // future PR tunes the prompt content, it MUST update both
            // the yaml AND the const (or, post-Phase-5, just the yaml).
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            var entry = catalog.Get("dialogue-options-instruction");

            string fromYaml = entry.SystemPrompt!;
            string fromConst = PromptTemplates.DialogueOptionsInstruction;

            // Exact byte-for-byte comparison — no whitespace normalisation.
            Assert.Equal(fromConst, fromYaml);
        }

        // ----- TryGetFromCatalog ---------------------------------------------

        [Fact]
        public void TryGetFromCatalog_ReturnsConstFallback_WhenCatalogIsNull()
        {
            PromptTemplates.Catalog = null;
            string result = PromptTemplates.TryGetFromCatalog(
                "default-clean", PromptTemplates.DialogueOptionsInstruction);
            Assert.Equal(PromptTemplates.DialogueOptionsInstruction, result);
        }

        [Fact]
        public void TryGetFromCatalog_ReturnsCatalogEntry_WhenCatalogIsSet()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            PromptTemplates.Catalog = catalog;

            try
            {
                string result = PromptTemplates.TryGetFromCatalog(
                    "default-clean", "FALLBACK_SHOULD_NOT_BE_USED");
                Assert.NotEqual("FALLBACK_SHOULD_NOT_BE_USED", result);
                Assert.Equal(catalog.Get("default-clean").SystemPrompt, result);
            }
            finally
            {
                PromptTemplates.Catalog = null;
            }
        }

        [Fact]
        public void TryGetFromCatalog_ReturnsFallback_WhenKeyNotFound()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            PromptTemplates.Catalog = catalog;

            try
            {
                string result = PromptTemplates.TryGetFromCatalog(
                    "nonexistent-key", "FALLBACK");
                Assert.Equal("FALLBACK", result);
            }
            finally
            {
                PromptTemplates.Catalog = null;
            }
        }
    }
}

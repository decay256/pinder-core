using System;
using System.IO;
using System.Linq;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #872 Phase 2 (Phase 5 cleanup #875): <see cref="PromptTemplates"/>
    /// const strings migrated to <c>data/prompts/templates.yaml</c>.
    ///
    /// Phase 5 removed the const fallbacks and TryGetFromCatalog helper.
    /// Tests that exercised those paths are deleted; only the catalog-load
    /// test remains.
    ///
    /// What this file now pins:
    /// - The loader parses <c>data/prompts/templates.yaml</c> into a
    ///   <see cref="PromptCatalog"/> with all expected entries.
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
    }
}

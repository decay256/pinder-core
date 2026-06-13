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
        public void TemplatesYaml_LoadsAll35Entries()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);

            // #1126: two dead creative-delivery templates were removed
            // (engine-delivery-block, failure-delivery-instruction), dropping
            // templates.yaml from 37 to 35 Phase 2 entries. With the Phase 1
            // stake entry that is at least 36 names.
            var names = catalog.Names.ToList();
            Assert.True(names.Count >= 36,
                $"expected >=36 prompt names (35 templates + stake), got {names.Count}");

            // Spot-check a few representative keys.
            Assert.Contains("dialogue-options-instruction", names);
            Assert.Contains("default-clean", names);
            Assert.Contains("interest-narrative-25", names);
            Assert.Contains("engine-options-block", names);
        }
    }
}

using System;
using System.Linq;
using Pinder.Core.TestCommon;
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
        private static string PromptsRoot
            => TestRepoLocator.FindRepoSubdir("data", "prompts");

        // ----- loader: entry count -------------------------------------------

        [Fact]
        public void TemplatesYaml_LoadsCurrentEntries()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);

            // #1126: two dead creative-delivery templates were removed
            // (engine-delivery-block, failure-delivery-instruction), dropping
            // templates.yaml from 37 to 35 Phase 2 entries. With the Phase 1
            // stake entry that was at least 36 names. Finding 3 later moved
            // 18 live gameplay-generation directives into templates.yaml.
            var names = catalog.Names.ToList();
            Assert.True(names.Count >= 54,
                $"expected >=54 prompt names (53 templates + stake), got {names.Count}");

            // Spot-check a few representative keys.
            Assert.Contains("dialogue-options-instruction", names);
            Assert.Contains("default-clean", names);
            Assert.Contains("interest-narrative-25", names);
            Assert.Contains("engine-options-block", names);
            Assert.Contains("cold-opener-rule", names);
            Assert.Contains("player-transition-directive", names);
            Assert.Contains("resistance-skeptical-interest", names);
            Assert.Contains("sim_agent_icon_weakness", names);
        }
    }
}

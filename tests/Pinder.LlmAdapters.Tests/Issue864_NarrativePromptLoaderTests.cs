using Pinder.Tools.NarrativeHarness;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #864: the editable narrative-testbed prompt
    /// (data/prompts/narrative.yaml) is loadable through the harness data
    /// locator and surfaces a non-empty multi-line default. Additive — nothing
    /// consumes the loader yet, so this pins only that the file + loader resolve.
    /// </summary>
    public class Issue864_NarrativePromptLoaderTests
    {
        [Fact]
        public void Load_returns_non_empty_default_prompt()
        {
            string prompt = NarrativePromptLoader.Load();

            Assert.False(string.IsNullOrWhiteSpace(prompt));
            // Multi-line block.
            Assert.Contains("\n", prompt);
            // Stable substrings migrated from the IngestionArcStrategy prose.
            Assert.Contains("Opportunistic confession arc", prompt);
            Assert.Contains("Never dump the list", prompt);
        }
    }
}

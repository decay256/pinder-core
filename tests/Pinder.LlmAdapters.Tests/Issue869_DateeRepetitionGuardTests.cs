using System;
using System.IO;
using System.Linq;
using Xunit;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #869: porting the WORD & PATTERN REPETITION block + self-check
    /// from <c>dialogue-options-instruction</c> to
    /// <c>datee-response-instruction</c>. These tests pin the prompt
    /// contract so future yaml edits can't silently regress the parity.
    /// </summary>
    public class Issue869_DateeRepetitionGuardTests
    {
        // Walks up from the test binary's BaseDirectory looking for
        // data/prompts so the catalog can be loaded in test runs.
        private static string FindPromptsRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException(
                "Could not locate data/prompts in any ancestor of the test binary.");
        }

        private static string LoadDateeResponsePrompt()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            var entry = catalog.Get("datee-response-instruction");
            return entry.SystemPrompt ?? string.Empty;
        }

        [Fact]
        public void DateeResponseInstruction_ContainsRepetitionGuard()
        {
            var prompt = LoadDateeResponsePrompt();

            Assert.Contains("WORD & PATTERN REPETITION", prompt);
            Assert.Contains("fresh move", prompt);
            // The datee path checks the datee's OWN previous messages,
            // not the full conversation above (which is the player-side framing).
            Assert.Contains("your own previous messages", prompt);
        }

        [Fact]
        public void DateeResponseInstruction_ContainsSelfCheck()
        {
            var prompt = LoadDateeResponsePrompt();

            Assert.Contains("Before sending: verify", prompt);
            Assert.Contains("rewrite", prompt);
        }

        [Fact]
        public void DateeResponseInstruction_RepetitionGuard_ListsCommonFillers()
        {
            // Pin the specific fillers called out in the refined ticket
            // so reviewers can confirm the wording survives future edits.
            var prompt = LoadDateeResponsePrompt();

            Assert.Contains("honestly", prompt);
            Assert.Contains("literally", prompt);
            Assert.Contains("okay but", prompt);
            Assert.Contains("interesting that", prompt);
        }
    }
}

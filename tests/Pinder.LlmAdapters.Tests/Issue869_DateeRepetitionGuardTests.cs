using System;
using System.IO;
using System.Linq;
using Xunit;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #869/#1423: DATEE repetition guard wording
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

            Assert.Contains("DRAMATIC MOMENTUM", prompt);
            Assert.Contains("new conversational move", prompt);
            // The datee path checks the datee's OWN previous messages,
            // not the full conversation above (which is the player-side framing).
            Assert.Contains("previous DATEE messages", prompt);
        }

        [Fact]
        public void DateeResponseInstruction_RemovesOldAlwaysOnSelfCheck()
        {
            var prompt = LoadDateeResponsePrompt();

            Assert.DoesNotContain("Before sending: verify", prompt);
            Assert.DoesNotContain("WORD & PATTERN REPETITION", prompt);
        }

        [Fact]
        public void DateeResponseInstruction_DramaticMomentumKeepsRepetitionPressure()
        {
            // Pin the specific fillers called out in the refined ticket
            // so reviewers can confirm the wording survives future edits.
            var prompt = LoadDateeResponsePrompt();

            Assert.Contains("Never repeat yourself", prompt);
            Assert.Contains("new conversational move", prompt);
            Assert.Contains("opening", prompt);
            Assert.Contains("cadence", prompt);
        }
    }
}

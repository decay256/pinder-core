using System;
using System.IO;
using Xunit;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #870: the opponent LLM receives the player's full assembled
    /// system prompt as authorial context. Without a CONTEXT BOUNDARY guard
    /// the LLM can leak that authorial context into the in-character voice
    /// (the opponent "knowing" stake-only facts the player never typed).
    /// These tests pin the prompt contract.
    /// </summary>
    public class Issue870_OpponentVoiceIsolationTests
    {
        // Walks up from the test binary's BaseDirectory looking for
        // data/prompts so the catalog can be loaded in test runs.
        // Mirrors the pattern in Issue869_OpponentRepetitionGuardTests and
        // Issue872_PromptTemplatesPhase2Tests.
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

        private static string LoadOpponentResponsePrompt()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            var entry = catalog.Get("opponent-response-instruction");
            return entry.SystemPrompt ?? string.Empty;
        }

        [Fact]
        public void OpponentResponseInstruction_ContainsContextBoundary()
        {
            var prompt = LoadOpponentResponsePrompt();
            Assert.Contains("CONTEXT BOUNDARY", prompt);
        }

        [Fact]
        public void OpponentResponseInstruction_ContextBoundary_NamesPsychologicalStake()
        {
            var prompt = LoadOpponentResponsePrompt();
            Assert.Contains("psychological stake", prompt);
            Assert.Contains("shadow state", prompt);
        }
    }
}

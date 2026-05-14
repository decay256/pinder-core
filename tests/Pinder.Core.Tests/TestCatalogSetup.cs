using System;
using System.IO;
using System.Runtime.CompilerServices;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.LlmAdapters;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Module initializer that wires the prompt catalog delegates before
    /// any test runs. Phase 5 (#875) removed the C# const-string fallbacks;
    /// tests must have the yaml-sourced catalog wired.
    /// </summary>
    internal static class TestCatalogSetup
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            // CoreTestWiring may have already run; don't overwrite.
            if (PromptTemplates.Catalog != null)
                return;
            var promptsDir = ResolvePromptsDirectory();
            if (promptsDir == null)
            {
                Console.Error.WriteLine(
                    "[TestCatalogSetup] Could not resolve data/prompts directory. " +
                    "Tests that depend on prompt content will fail with KeyNotFoundException.");
                return;
            }

            try
            {
                var catalog = PromptCatalog.LoadFromDirectory(promptsDir);

                // Wire PromptTemplates
                PromptTemplates.Catalog = catalog;

                // Wire PromptBuilder.StructuralFragmentLookup
                PromptBuilder.StructuralFragmentLookup =
                    key => catalog.TryGet(key)?.SystemPrompt;

                // Wire ArchetypeCatalog.BehaviorResolver.
                // Unknown names (e.g. test-only archetypes like "The Pun Troll")
                // get a placeholder so tests don't crash. Production wiring does
                // NOT include this fallback — unknown archetypes are a hard error.
                ArchetypeCatalog.BehaviorResolver = name =>
                    catalog.TryGet(name)?.SystemPrompt
                    ?? $"Follow {name} behavioral pattern.";

                Console.Error.WriteLine(
                    $"[TestCatalogSetup] Prompt catalog wired from {promptsDir}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[TestCatalogSetup] Failed to load prompt catalog: {ex.Message}");
            }
        }

        private static string? ResolvePromptsDirectory()
        {
            // Walk up from the test assembly location to find the pinder-core root.
            // Test assembly is in e.g. tests/Pinder.Core.Tests/bin/Debug/net8.0/
            var baseDir = AppContext.BaseDirectory;

            // Try a few paths relative to the test assembly.
            var candidates = new[]
            {
                Path.Combine(baseDir, "data", "prompts"),
                Path.Combine(baseDir, "..", "..", "..", "data", "prompts"),
                Path.Combine(baseDir, "..", "..", "..", "..", "data", "prompts"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "data", "prompts"),
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full))
                    return full;
            }

            return null;
        }
    }
}

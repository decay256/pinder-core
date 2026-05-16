using System.IO;
using System.Runtime.CompilerServices;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Wires the prompt catalog once at assembly load, before any test runs.
    ///
    /// After Phase 5 of #871 (#875), there are no C# const fallbacks.
    /// Every test that exercises LLM prompt building or archetype behavior
    /// needs the catalog wired. This runs once per assembly load, so all
    /// tests start with a valid catalog state.
    ///
    /// Individual tests that test error-handling (null resolver, missing
    /// keys) save/restore the static state explicitly.
    /// </summary>
    internal static class CoreTestWiring
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate))
                {
                    var catalog = PromptCatalog.LoadFromDirectory(candidate);
                    PromptTemplates.Catalog = catalog;
                    PromptBuilder.StructuralFragmentLookup =
                        key => catalog.TryGet(key)?.SystemPrompt;
                    ArchetypeYamlLoader.LoadFromPromptCatalog(catalog);

                    // #907: Also load the conflict matrix so integration tests
                    // that exercise the production path get conflict resolution.
                    var dataRoot = Path.GetDirectoryName(candidate); // data/
                    if (dataRoot != null)
                    {
                        var conflictsPath = Path.Combine(dataRoot, "persona", "texting-style-conflicts.yaml");
                        if (File.Exists(conflictsPath))
                            TextingStyleAggregator.ConflictCatalog =
                                TextingStyleConflicts.LoadFrom(File.ReadAllText(conflictsPath));
                    }
                    return;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
        }
    }
}

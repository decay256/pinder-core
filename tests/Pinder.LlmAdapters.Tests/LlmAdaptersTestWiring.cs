using System.IO;
using System.Runtime.CompilerServices;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Wires the prompt catalog once at assembly load, before any test runs.
    ///
    /// After Phase 5 of #871 (#875), there are no C# const fallbacks.
    /// Every test that exercises LLM prompt building needs the catalog wired.
    /// </summary>
    internal static class LlmAdaptersTestWiring
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
                    return;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
        }
    }
}

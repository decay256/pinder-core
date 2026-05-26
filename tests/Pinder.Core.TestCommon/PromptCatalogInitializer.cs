using System;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;

namespace Pinder.Core.TestCommon
{
    public static class PromptCatalogInitializer
    {
        public static void Initialize()
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
                        {
                            TextingStyleAggregator.ConflictCatalog =
                                TextingStyleConflicts.LoadFrom(File.ReadAllText(conflictsPath));
                        }
                    }
                    return;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            Console.WriteLine("[PromptCatalogInitializer] WARNING: Did not find prompts directory!");
        }
    }
}

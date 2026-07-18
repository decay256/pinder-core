using System;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
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
                    PromptBuilder.StructuralFragmentLookupEx = key =>
                    {
                        var entry = catalog.TryGet(key);
                        if (entry == null) return null;
                        return new StructuralPromptResult(entry.SystemPrompt, entry.SourceFile);
                    };
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
                                TextingStyleConflictYamlLoader.LoadFrom(File.ReadAllText(conflictsPath));
                        }

                        var gameDefinitionPath = Path.Combine(dataRoot, "game-definition.yaml");
                        if (File.Exists(gameDefinitionPath))
                        {
                            DefaultRuleResolver.Instance =
                                GameDefinition.LoadFrom(File.ReadAllText(gameDefinitionPath));
                        }
                    }
                    return;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new InvalidOperationException("[PromptCatalogInitializer] ERROR: Did not find prompts directory!");
        }
    }
}

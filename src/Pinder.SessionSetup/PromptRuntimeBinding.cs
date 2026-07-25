using System;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Immutable prompt-runtime binding built from the existing yaml loaders.
    /// Hosts may capture this object for one operation or session without
    /// depending on mutable static prompt globals.
    /// </summary>
    public sealed class PromptRuntimeBinding
    {
        public PromptRuntimeBinding(
            PromptCatalog catalog,
            Func<string, string?> structuralFragmentLookup,
            Func<string, StructuralPromptResult?> structuralFragmentLookupEx,
            Func<string, string?> archetypeBehaviorResolver,
            TextingStyleConflicts textingStyleConflicts,
            string promptsRoot)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            StructuralFragmentLookup = structuralFragmentLookup ?? throw new ArgumentNullException(nameof(structuralFragmentLookup));
            StructuralFragmentLookupEx = structuralFragmentLookupEx ?? throw new ArgumentNullException(nameof(structuralFragmentLookupEx));
            ArchetypeBehaviorResolver = archetypeBehaviorResolver ?? throw new ArgumentNullException(nameof(archetypeBehaviorResolver));
            TextingStyleConflicts = textingStyleConflicts ?? throw new ArgumentNullException(nameof(textingStyleConflicts));
            PromptsRoot = promptsRoot ?? throw new ArgumentNullException(nameof(promptsRoot));
        }

        public PromptCatalog Catalog { get; }
        public Func<string, string?> StructuralFragmentLookup { get; }
        public Func<string, StructuralPromptResult?> StructuralFragmentLookupEx { get; }
        public Func<string, string?> ArchetypeBehaviorResolver { get; }
        public TextingStyleConflicts TextingStyleConflicts { get; }
        public string PromptsRoot { get; }

        public static PromptRuntimeBinding Build(string promptsRoot, TextWriter? diagnosticSink = null)
        {
            if (promptsRoot is null)
                throw new ArgumentNullException(nameof(promptsRoot));

            var resolved = ResolvePromptsRoot(promptsRoot);
            var catalog = PromptCatalog.LoadFromDirectory(resolved);
            catalog.ValidateRuntimeCatalog();

            Func<string, string?> structuralLookup = key =>
                catalog.TryGet(key)?.SystemPrompt;

            Func<string, StructuralPromptResult?> structuralLookupEx = key =>
            {
                var entry = catalog.TryGet(key);
                return entry == null
                    ? null
                    : new StructuralPromptResult(entry.SystemPrompt, entry.SourceFile);
            };

            Func<string, string?> archetypeResolver = name =>
                catalog.TryGet(name)?.SystemPrompt;

            return new PromptRuntimeBinding(
                catalog,
                structuralLookup,
                structuralLookupEx,
                archetypeResolver,
                LoadTextingStyleConflicts(resolved, diagnosticSink),
                resolved);
        }

        public void PublishCompatibilityStatics(TextWriter? diagnosticSink = null)
        {
            PromptTemplates.Catalog = Catalog;
            PromptBuilder.StructuralFragmentLookup = StructuralFragmentLookup;
            PromptBuilder.StructuralFragmentLookupEx = StructuralFragmentLookupEx;
            ArchetypeCatalog.BehaviorResolver = ArchetypeBehaviorResolver;
            TextingStyleAggregator.ConflictCatalog = TextingStyleConflicts;

            diagnosticSink?.WriteLine(
                $"[INFO] PromptWiring: loaded {Catalog.Names.Count()} keys from {PromptsRoot}");
        }

        private static string ResolvePromptsRoot(string promptsRoot)
        {
            string resolved = promptsRoot;
            if (!Directory.Exists(resolved))
            {
                var dir = promptsRoot;
                for (int i = 0; i < 10; i++)
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (parent == null || parent == dir) break;
                    dir = parent;
                    var candidate = Path.Combine(dir, "data", "prompts");
                    if (Directory.Exists(candidate))
                    {
                        resolved = candidate;
                        break;
                    }
                }
            }

            if (!Directory.Exists(resolved))
            {
                throw new DirectoryNotFoundException(
                    $"PromptWiring: prompts root not found: {promptsRoot} (searched ancestors)");
            }

            return resolved;
        }

        private static TextingStyleConflicts LoadTextingStyleConflicts(
            string resolvedPromptsRoot,
            TextWriter? diagnosticSink)
        {
            var dataRoot = Path.GetDirectoryName(resolvedPromptsRoot);
            if (dataRoot == null)
                return TextingStyleConflicts.Empty;

            var conflictsPath = Path.Combine(dataRoot, "persona", "texting-style-conflicts.yaml");
            if (!File.Exists(conflictsPath))
            {
                diagnosticSink?.WriteLine(
                    $"[WARN] PromptWiring: texting-style-conflicts.yaml not found at {conflictsPath} " +
                    "- conflict resolution disabled");
                return TextingStyleConflicts.Empty;
            }

            var conflicts = TextingStyleConflictYamlLoader.LoadFrom(File.ReadAllText(conflictsPath));
            diagnosticSink?.WriteLine(
                $"[INFO] PromptWiring: loaded {conflicts.Entries.Count} conflict entries from {conflictsPath}");
            return conflicts;
        }
    }
}

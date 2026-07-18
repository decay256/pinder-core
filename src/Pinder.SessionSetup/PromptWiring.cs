using System;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Bootstraps the prompt yaml infrastructure at startup.
    ///
    /// <para>
    /// Three static delegates must be wired before any LLM prompt is built:
    /// </para>
    ///
    /// <list type="number">
    /// <item><c>PromptTemplates.Catalog</c> — the unified PromptCatalog
    /// loaded from <c>data/prompts/</c>.</item>
    /// <item><c>PromptBuilder.StructuralFragmentLookup</c> — delegates
    /// structural prompt section headers to the catalog.</item>
    /// <item><c>ArchetypeCatalog.BehaviorResolver</c> — delegates archetype
    /// behavior text to the catalog (via ArchetypeYamlLoader).</item>
    /// </list>
    ///
    /// <para>
    /// This class is the single source of truth for startup wiring.
    /// Production consumers (Pinder.GameApi) and test harnesses
    /// (session-runner) call <see cref="Wire"/> once at program start
    /// before creating any session or character profile. After Phase 5
    /// of #871, there are no C# const fallbacks — a missing catalog is a
    /// fatal error at first access.
    /// </para>
    /// </summary>
    public static class PromptWiring
    {
        /// <summary>
        /// One-shot: loads the unified PromptCatalog from
        /// <paramref name="promptsRoot"/> and wires all three static
        /// delegates. Safe to call more than once (subsequent calls are
        /// no-ops if the catalog was already assembled).
        /// </summary>
        /// <param name="promptsRoot">
        /// Path to <c>data/prompts</c>. Accepts an absolute or
        /// repo-relative path. Use <c>Path.Combine(AppContext.BaseDirectory,
        /// "data/prompts")</c> in production; use <c>FindRepoRoot()</c> or
        /// a fixed relative path in the session-runner.
        /// </param>
        /// <param name="errorSink">
        /// Receives any non-fatal diagnostics (directory-not-found, parse
        /// warnings). May be null.
        /// </param>
        public static void Wire(string promptsRoot, TextWriter? errorSink = null)
        {
            if (promptsRoot is null)
                throw new ArgumentNullException(nameof(promptsRoot));

            // Idempotent: don't reload if already wired.
            if (PromptTemplates.Catalog != null)
                return;

            // Resolve prompts root: try the given path, then search ancestor
            // directories (covers scenarios where the working directory is a
            // subdirectory like the test bin/ output).
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
                var msg = $"PromptWiring: prompts root not found: {promptsRoot} (searched ancestors)";
                errorSink?.WriteLine($"[WARN] {msg}");
                throw new DirectoryNotFoundException(msg);
            }

            var catalog = PromptCatalog.LoadFromDirectory(resolved);
            PromptTemplates.Catalog = catalog;

            // Wire PromptBuilder's structural fragment lookup.
            PromptBuilder.StructuralFragmentLookup = key =>
                catalog.TryGet(key)?.SystemPrompt;

            PromptBuilder.StructuralFragmentLookupEx = key =>
            {
                var entry = catalog.TryGet(key);
                if (entry == null) return null;
                return new StructuralPromptResult(entry.SystemPrompt, entry.SourceFile);
            };

            // Wire ArchetypeCatalog's behavior resolver.
            ArchetypeYamlLoader.LoadFromPromptCatalog(catalog);

            // #907: Load the texting-style conflict matrix so the production
            // aggregator resolves conflicts at character-load time.
            // data/persona sits alongside data/prompts under the same data/ root.
            var dataRoot = Path.GetDirectoryName(resolved);
            if (dataRoot != null)
            {
                var conflictsPath = Path.Combine(dataRoot, "persona", "texting-style-conflicts.yaml");
                if (File.Exists(conflictsPath))
                {
                    TextingStyleAggregator.ConflictCatalog =
                        TextingStyleConflictYamlLoader.LoadFrom(File.ReadAllText(conflictsPath));
                    errorSink?.WriteLine(
                        $"[INFO] PromptWiring: loaded {TextingStyleAggregator.ConflictCatalog.Entries.Count} " +
                        $"conflict entries from {conflictsPath}");
                }
                else
                {
                    errorSink?.WriteLine(
                        $"[WARN] PromptWiring: texting-style-conflicts.yaml not found at {conflictsPath} " +
                        "— conflict resolution disabled");
                }
            }

            errorSink?.WriteLine(
                $"[INFO] PromptWiring: loaded {catalog.Names.Count()} keys from {promptsRoot}");
        }
    }
}

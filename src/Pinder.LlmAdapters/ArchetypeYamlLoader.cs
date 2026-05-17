using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using YamlDotNet.RepresentationModel;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Loads archetype behavior text from yaml and registers it with
    /// <see cref="ArchetypeCatalog"/> so the LLM directive
    /// ("ACTIVE ARCHETYPE: ...") is sourced from the canonical yaml rather
    /// than from hand-copied literals in <see cref="ArchetypeCatalog"/>'s
    /// static initialiser.
    ///
    /// <para>
    /// Two loading paths are supported:
    /// </para>
    ///
    /// <para>
    /// <b>Legacy (<see cref="LoadFromYaml"/>):</b> parses
    /// <c>archetypes-enriched.yaml</c> — a flat list of section blocks where
    /// each archetype is a block with <c>type: archetype_definition</c>,
    /// a <c>title</c> field, and a <c>behavior</c> field. Still in active
    /// use by <c>Pinder.GameApi/Program.cs</c> as defence-in-depth for the
    /// Issue #372 hot-fix path; will be retired once that caller migrates
    /// to <see cref="LoadFromPromptCatalog"/>-only wiring (tracked in the
    /// #883 follow-up ticket).
    /// </para>
    ///
    /// <para>
    /// <b>Current (<see cref="LoadFromPromptCatalog"/>):</b> consumes a
    /// <see cref="PromptCatalog"/> loaded from
    /// <c>data/prompts/archetypes.yaml</c> (Issue #873 Phase 4). Each prompt
    /// entry's key is the archetype name (e.g. <c>"The Hey Opener"</c>) and
    /// its <c>system_prompt</c> is the behavioural instruction text. This
    /// consolidates the inline const strings from
    /// <see cref="ArchetypeCatalog"/>'s static initialiser into the same
    /// <c>PromptCatalog</c>-format yaml family used by Phases 1–3 of #871.
    /// </para>
    ///
    /// <para>
    /// The hardcoded behaviour strings in <see cref="ArchetypeCatalog"/>'s
    /// static initialiser remain as a degraded-mode fallback. If the yaml
    /// file is missing, fails to parse, or lacks an entry for a given
    /// archetype, the catalog falls back to the hand-copied literal.
    /// </para>
    /// </summary>
    public static class ArchetypeYamlLoader
    {
        /// <summary>
        /// Result of a load operation. Useful for logging at startup.
        /// </summary>
        public sealed class LoadResult
        {
            /// <summary>Number of (title, behavior) pairs registered with the catalog.</summary>
            public int Registered { get; }

            /// <summary>Names of archetype blocks skipped because they had no <c>behavior</c> text.</summary>
            public IReadOnlyList<string> SkippedMissingBehavior { get; }

            /// <summary>Error message when the YAML failed to parse, or null on success.</summary>
            public string? Error { get; }

            public LoadResult(int registered, IReadOnlyList<string> skippedMissingBehavior, string? error)
            {
                Registered = registered;
                SkippedMissingBehavior = skippedMissingBehavior ?? Array.Empty<string>();
                Error = error;
            }
        }

        /// <summary>
        /// Parse the supplied YAML text and register every archetype's
        /// behaviour with <see cref="ArchetypeCatalog.RegisterBehavior"/>.
        /// On any parse failure returns a <see cref="LoadResult"/> with
        /// <c>Error</c> populated and <c>Registered = 0</c>; the catalog is
        /// not modified in that case (callers should log the error so the
        /// degraded fallback is visible).
        /// </summary>
        /// <param name="yamlContent">Full text of <c>archetypes-enriched.yaml</c>.</param>
        /// <returns>Load summary.</returns>
        public static LoadResult LoadFromYaml(string yamlContent)
        {
            if (string.IsNullOrWhiteSpace(yamlContent))
                return new LoadResult(0, Array.Empty<string>(), "yaml content was empty");

            int registered = 0;
            var skipped = new List<string>();

            try
            {
                var stream = new YamlStream();
                using (var reader = new System.IO.StringReader(yamlContent))
                    stream.Load(reader);

                if (stream.Documents.Count == 0)
                    return new LoadResult(0, skipped, "yaml had no documents");

                if (!(stream.Documents[0].RootNode is YamlSequenceNode root))
                    return new LoadResult(0, skipped, "yaml root was not a sequence");

                foreach (var node in root.Children)
                {
                    if (!(node is YamlMappingNode block)) continue;

                    string? type = GetScalar(block, "type");
                    if (!string.Equals(type, "archetype_definition", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? title = GetScalar(block, "title");
                    string? behavior = GetScalar(block, "behavior");

                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    if (string.IsNullOrWhiteSpace(behavior))
                    {
                        skipped.Add(title!);
                        continue;
                    }

                    ArchetypeCatalog.RegisterBehavior(title!, behavior!);
                    registered++;
                }

                return new LoadResult(registered, skipped, null);
            }
            catch (Exception ex)
            {
                return new LoadResult(0, skipped, ex.Message);
            }
        }

        /// <summary>
        /// Wire <see cref="ArchetypeCatalog.BehaviorResolver"/> so
        /// <see cref="ArchetypeCatalog.GetBehavior"/> prefers the yaml
        /// catalog over the embedded const strings.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Issue #873 Phase 4: the catalog is loaded from
        /// <c>data/prompts/archetypes.yaml</c> by
        /// <c>PromptCatalog.LoadFromDirectory("data/prompts")</c> at startup.
        /// Each prompt key is the archetype name (e.g. <c>"The Hey Opener"</c>)
        /// and its <c>system_prompt</c> is the behavior text. Entries that are
        /// already in the catalog (from other yaml files like
        /// <c>templates.yaml</c>) are silently skipped since they won't match
        /// any archetype name.
        /// </para>
        /// <para>
        /// The resolver delegate is the single source of truth — there is no
        /// longer a bulk <c>RegisterBehavior</c> loop because the resolver
        /// provides the same outcome with fewer mutation points. Callers that
        /// need to reset state after testing should save and restore
        /// <see cref="ArchetypeCatalog.BehaviorResolver"/>.
        /// </para>
        /// </remarks>
        /// <param name="catalog">A fully-loaded <see cref="PromptCatalog"/>.</param>
        public static void LoadFromPromptCatalog(PromptCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));

            // Wire the resolver so GetBehavior prefers the yaml catalog
            // (Issue #873 Phase 4 — delegate pattern crosses the assembly
            // boundary between Pinder.Core and Pinder.LlmAdapters).
            ArchetypeCatalog.BehaviorResolver = name =>
                catalog.TryGet(name)?.SystemPrompt;
        }

        private static string? GetScalar(YamlMappingNode mapping, string key)
        {
            if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode scalar)
                return scalar.Value;
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using YamlDotNet.RepresentationModel;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Loads <c>archetypes-enriched.yaml</c> at startup and registers each
    /// archetype's <c>behavior</c> string with <see cref="ArchetypeCatalog"/>
    /// so the LLM directive ("ACTIVE ARCHETYPE: ...") is sourced from the
    /// canonical YAML rather than from hand-copied literals in
    /// <see cref="ArchetypeCatalog"/>'s static initialiser (#372).
    ///
    /// <para>
    /// The YAML structure is a flat list of section blocks; each archetype is
    /// a block with <c>type: archetype_definition</c>, a <c>title</c> field
    /// (e.g. <c>"The Peacock"</c>), and a <c>behavior</c> field (the multi-line
    /// behavioural-instruction text).
    /// </para>
    ///
    /// <para>
    /// The hardcoded behaviour strings in <see cref="ArchetypeCatalog"/>'s
    /// static initialiser remain as a degraded-mode fallback. If the YAML file
    /// is missing, fails to parse, or lacks an entry for a given archetype,
    /// the catalog falls back to the hand-copied literal (still better than
    /// the bare placeholder <c>"Follow {Name} behavioral pattern."</c>).
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

        private static string? GetScalar(YamlMappingNode mapping, string key)
        {
            if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode scalar)
                return scalar.Value;
            return null;
        }
    }
}

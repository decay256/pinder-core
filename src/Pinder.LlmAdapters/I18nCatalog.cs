using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;
using Pinder.Core.I18n;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Engine-side i18n catalog — loads
    /// <c>data/i18n/&lt;locale&gt;/*.yaml</c> into a typed in-memory
    /// representation that mirrors the frontend's generated dict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sprint: i18n string extraction (pinder-web issue #436 /
    /// Phase 1.5). The simulator reads this so snapshot output can
    /// embed human-readable interpretation strings (see <see cref="VariantPicker"/>
    /// for the picker that produces the same index web-side).
    /// </para>
    /// <para>
    /// Loader semantics:
    /// <list type="bullet">
    ///   <item>Reads <c>events.yaml</c> for variant data
    ///     (<c>events: { kind: { title, summary_variants[] } }</c>).</item>
    ///   <item>Reads every other yaml in the locale directory for
    ///     flat strings (<c>strings: { key: value }</c>).</item>
    ///   <item>Validates <c>schema_version: 1</c> at the top of every
    ///     file.</item>
    ///   <item>Fails loud on duplicate keys across files (mirrors the
    ///     frontend build-script's contract).</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class I18nCatalog
    {
        public string Locale { get; }
        public IReadOnlyDictionary<string, string> Strings { get; }
        public IReadOnlyDictionary<string, EventEntry> Events { get; }

        private I18nCatalog(
            string locale,
            IReadOnlyDictionary<string, string> strings,
            IReadOnlyDictionary<string, EventEntry> events)
        {
            Locale = locale;
            Strings = strings;
            Events = events;
        }

        /// <summary>
        /// Look up a flat string by its full
        /// <c>surface.element_descriptor</c> key. Throws when the key
        /// is not present — symmetric to the frontend's compile-time-
        /// checked <c>StringKey</c> union: a missing key on the engine
        /// side is a bug, not a fallback opportunity.
        /// </summary>
        public string T(string key)
        {
            if (!Strings.TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException(
                    $"i18n: missing string key '{key}' for locale '{Locale}'");
            }
            return value;
        }

        /// <summary>
        /// Pick the deterministic variant for an event at a turn —
        /// thin wrapper that combines the catalog lookup with the
        /// shared <see cref="VariantPicker"/>.
        /// </summary>
        public string TVariant(string eventKind, int turnNumber)
        {
            if (!Events.TryGetValue(eventKind, out var entry))
            {
                throw new KeyNotFoundException(
                    $"i18n: missing event kind '{eventKind}' for locale '{Locale}'");
            }
            int idx = VariantPicker.PickIndex(eventKind, turnNumber, entry.SummaryVariants.Count);
            return entry.SummaryVariants[idx];
        }

        /// <summary>
        /// Load the full catalog from
        /// <paramref name="i18nRoot"/><c>/&lt;locale&gt;/</c>. Returns
        /// a frozen catalog — callers should cache the instance.
        /// </summary>
        /// <param name="i18nRoot">
        /// Repo-relative or absolute path to <c>data/i18n</c>.
        /// </param>
        /// <param name="locale">
        /// Locale subdirectory name. Default <c>"en"</c>.
        /// </param>
        public static I18nCatalog LoadFromDirectory(string i18nRoot, string locale = "en")
        {
            if (i18nRoot is null) throw new ArgumentNullException(nameof(i18nRoot));
            if (locale is null) throw new ArgumentNullException(nameof(locale));

            string localeDir = Path.Combine(i18nRoot, locale);
            if (!Directory.Exists(localeDir))
            {
                throw new DirectoryNotFoundException(
                    $"i18n: locale directory not found: {localeDir}");
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            var events = new Dictionary<string, EventEntry>(StringComparer.Ordinal);
            var keyOrigin = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var path in Directory.EnumerateFiles(localeDir, "*.yaml"))
            {
                LoadFile(path, strings, events, keyOrigin);
            }

            return new I18nCatalog(locale, strings, events);
        }

        private static void LoadFile(
            string path,
            IDictionary<string, string> strings,
            IDictionary<string, EventEntry> events,
            IDictionary<string, string> keyOrigin)
        {
            var stream = new YamlStream();
            using (var reader = File.OpenText(path))
            {
                stream.Load(reader);
            }
            if (stream.Documents.Count == 0)
            {
                throw new InvalidDataException($"i18n: empty yaml: {path}");
            }
            var root = stream.Documents[0].RootNode as YamlMappingNode
                ?? throw new InvalidDataException(
                    $"i18n: top-level must be a mapping: {path}");

            // Schema version gate.
            int schemaVersion = ParseInt(root, "schema_version", path);
            if (schemaVersion != 1)
            {
                throw new InvalidDataException(
                    $"i18n: {path} must declare schema_version: 1 (got {schemaVersion})");
            }

            // Either `strings:` (flat) or `events:` (variant); never both.
            var hasStrings = TryGetMapping(root, "strings", out var stringsNode);
            var hasEvents = TryGetMapping(root, "events", out var eventsNode);

            if (hasStrings && hasEvents)
            {
                throw new InvalidDataException(
                    $"i18n: {path} contains both 'strings' and 'events'; pick one");
            }

            if (hasStrings)
            {
                foreach (var kv in stringsNode!.Children)
                {
                    var key = (kv.Key as YamlScalarNode)?.Value
                        ?? throw new InvalidDataException(
                            $"i18n: {path} non-scalar key in strings");
                    var value = (kv.Value as YamlScalarNode)?.Value
                        ?? throw new InvalidDataException(
                            $"i18n: {path} non-scalar value for key '{key}'");
                    if (keyOrigin.TryGetValue(key, out var origin))
                    {
                        throw new InvalidDataException(
                            $"i18n: duplicate key '{key}' in {path} (also defined in {origin})");
                    }
                    keyOrigin[key] = path;
                    strings[key] = value;
                }
            }
            else if (hasEvents)
            {
                foreach (var kv in eventsNode!.Children)
                {
                    var kind = (kv.Key as YamlScalarNode)?.Value
                        ?? throw new InvalidDataException(
                            $"i18n: {path} non-scalar event kind");
                    var body = kv.Value as YamlMappingNode
                        ?? throw new InvalidDataException(
                            $"i18n: {path} event '{kind}' must be a mapping");
                    string title = ParseString(body, "title", path, $"event '{kind}'");
                    var variantsNode = TryGetSequence(body, "summary_variants", out var seq)
                        ? seq
                        : null;
                    if (variantsNode is null || variantsNode.Children.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"i18n: {path} event '{kind}' must have a non-empty summary_variants list");
                    }
                    var variants = new List<string>(variantsNode.Children.Count);
                    foreach (var vn in variantsNode.Children)
                    {
                        var v = (vn as YamlScalarNode)?.Value
                            ?? throw new InvalidDataException(
                                $"i18n: {path} event '{kind}' has a non-string variant");
                        variants.Add(v);
                    }
                    events[kind] = new EventEntry(title, variants);
                }
            }
            // Files with neither block (just header) are tolerated —
            // they reserve a surface for a later phase to populate.
        }

        private static int ParseInt(YamlMappingNode node, string key, string path)
        {
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out var v))
            {
                throw new InvalidDataException($"i18n: {path} missing required key '{key}'");
            }
            var s = (v as YamlScalarNode)?.Value;
            if (!int.TryParse(s, out var i))
            {
                throw new InvalidDataException($"i18n: {path} key '{key}' must be an int (got '{s}')");
            }
            return i;
        }

        private static string ParseString(YamlMappingNode node, string key, string path, string ctx)
        {
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out var v))
            {
                throw new InvalidDataException($"i18n: {path} {ctx} missing string '{key}'");
            }
            return (v as YamlScalarNode)?.Value
                ?? throw new InvalidDataException(
                    $"i18n: {path} {ctx} key '{key}' must be a string");
        }

        private static bool TryGetMapping(YamlMappingNode parent, string key, out YamlMappingNode? mapping)
        {
            if (parent.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlMappingNode m)
            {
                mapping = m;
                return true;
            }
            mapping = null;
            return false;
        }

        private static bool TryGetSequence(YamlMappingNode parent, string key, out YamlSequenceNode? seq)
        {
            if (parent.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlSequenceNode s)
            {
                seq = s;
                return true;
            }
            seq = null;
            return false;
        }
    }

    /// <summary>One <c>events:</c> entry — title + variant list.</summary>
    public sealed class EventEntry
    {
        public string Title { get; }
        public IReadOnlyList<string> SummaryVariants { get; }

        public EventEntry(string title, IReadOnlyList<string> summaryVariants)
        {
            Title = title;
            SummaryVariants = summaryVariants;
        }
    }
}

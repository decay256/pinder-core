using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Engine-side prompt catalog \u2014 loads
    /// <c>data/prompts/*.yaml</c> into a typed in-memory representation
    /// and exposes per-call-site lookups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #843: lift the LLM prompt-template content from
    /// <c>const string</c> values in C# into yaml files under
    /// <c>data/prompts/</c>. Each call-site reads from this catalog at
    /// runtime instead of from a const, so the admin editor (which
    /// already round-trips yaml files in <c>pinder-core/data/</c>) can
    /// edit prompts without a code-change-and-redeploy cycle.
    /// </para>
    /// <para>
    /// File format (per file, one or more named prompts):
    /// <code>
    /// schema_version: 1
    /// prompts:
    ///   stake:
    ///     system_prompt: "..."
    ///     user_template: "...\n{character_profile}"
    /// </code>
    /// </para>
    /// <para>
    /// Substitution is <c>{token}</c>-style (NOT Scriban). The pre-locked
    /// Phase 1 decision per the issue's parent: match the existing yaml
    /// round-trip pattern used by ruamel in <c>pinder-backend</c>; defer
    /// hot-reload to V2; process restart is acceptable for V1.
    /// </para>
    /// <para>
    /// The catalog is loaded at startup and frozen. Multiple files in the
    /// directory are merged into a single keyed dictionary; duplicate
    /// prompt keys across files raise <see cref="InvalidDataException"/>
    /// at load time (mirrors <see cref="I18nCatalog"/>'s contract).
    /// </para>
    /// <para>
    /// Phase 1 of the migration ships this loader with <c>stake.yaml</c>
    /// only. <see cref="LlmStakeGenerator"/> consults the catalog when
    /// one is supplied; otherwise it falls back to the embedded const
    /// strings so the rest of the codebase keeps working without DI
    /// changes. Phase 5 removes the const fallbacks once every call-site
    /// is migrated.
    /// </para>
    /// </remarks>
    public sealed class PromptCatalog
    {
        private readonly IReadOnlyDictionary<string, PromptEntry> _entries;

        private PromptCatalog(IReadOnlyDictionary<string, PromptEntry> entries)
        {
            _entries = entries;
        }

        /// <summary>
        /// Look up a prompt by name (e.g. <c>"stake"</c>). Returns null
        /// when the key is not present \u2014 callers that have a const
        /// fallback (Phase 1-4) check <c>!= null</c> and fall back; the
        /// Phase 5 grep gate catches any const-strings still in the
        /// codebase.
        /// </summary>
        public PromptEntry? TryGet(string name)
        {
            return _entries.TryGetValue(name, out var entry) ? entry : null;
        }

        /// <summary>
        /// Look up a prompt by name; throw when the key is not present.
        /// Use this on call-sites that have already been migrated and
        /// have no fallback.
        /// </summary>
        public PromptEntry Get(string name)
        {
            return _entries.TryGetValue(name, out var entry)
                ? entry
                : throw new KeyNotFoundException(
                    $"prompt-catalog: missing prompt key '{name}'");
        }

        /// <summary>
        /// Names of every prompt the catalog loaded. Useful for
        /// diagnostics and tests asserting the migration's completeness.
        /// </summary>
        public IEnumerable<string> Names => _entries.Keys;

        /// <summary>
        /// Load the full catalog from <paramref name="promptsRoot"/>,
        /// scanning every <c>*.yaml</c> file in the directory.
        /// </summary>
        /// <param name="promptsRoot">
        /// Repo-relative or absolute path to <c>data/prompts</c>.
        /// </param>
        public static PromptCatalog LoadFromDirectory(string promptsRoot)
        {
            if (promptsRoot is null) throw new ArgumentNullException(nameof(promptsRoot));
            if (!Directory.Exists(promptsRoot))
            {
                throw new DirectoryNotFoundException(
                    $"prompt-catalog: directory not found: {promptsRoot}");
            }

            var entries = new Dictionary<string, PromptEntry>(StringComparer.Ordinal);
            var origin = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var path in Directory.EnumerateFiles(promptsRoot, "*.yaml"))
            {
                LoadFile(path, entries, origin);
            }

            return new PromptCatalog(entries);
        }

        /// <summary>
        /// Substitute <c>{token}</c> placeholders in
        /// <paramref name="template"/> using <paramref name="values"/>.
        /// Tokens that are not present in the dictionary raise
        /// <see cref="KeyNotFoundException"/>; surface in test assertions
        /// rather than silently leaving an unrendered token in the
        /// outgoing prompt.
        /// </summary>
        public static string Substitute(
            string template,
            IReadOnlyDictionary<string, string> values)
        {
            if (template is null) throw new ArgumentNullException(nameof(template));
            if (values is null) throw new ArgumentNullException(nameof(values));

            // Walk the template once. Recognise {name} sequences where
            // name is /[a-zA-Z_][a-zA-Z0-9_]*/. Anything else (e.g. a
            // literal `{` followed by non-alphanumeric) passes through
            // verbatim so JSON braces or stray-brace prose in the
            // template body don't accidentally trip the substituter.
            var sb = new System.Text.StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{' && i + 1 < template.Length)
                {
                    int end = template.IndexOf('}', i + 1);
                    if (end > i + 1)
                    {
                        string token = template.Substring(i + 1, end - i - 1);
                        if (IsTokenName(token))
                        {
                            if (!values.TryGetValue(token, out var v))
                            {
                                throw new KeyNotFoundException(
                                    $"prompt-catalog: template references token '{{{token}}}' " +
                                    "but no value was supplied at call-site.");
                            }
                            sb.Append(v);
                            i = end + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsTokenName(string s)
        {
            if (s.Length == 0) return false;
            char c0 = s[0];
            if (!(char.IsLetter(c0) || c0 == '_')) return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Loader internals
        // ------------------------------------------------------------------

        private static void LoadFile(
            string path,
            IDictionary<string, PromptEntry> entries,
            IDictionary<string, string> origin)
        {
            var stream = new YamlStream();
            using (var reader = File.OpenText(path))
            {
                stream.Load(reader);
            }
            if (stream.Documents.Count == 0)
            {
                throw new InvalidDataException(
                    $"prompt-catalog: empty yaml: {path}");
            }
            var root = stream.Documents[0].RootNode as YamlMappingNode
                ?? throw new InvalidDataException(
                    $"prompt-catalog: top-level must be a mapping: {path}");

            int schemaVersion = ParseInt(root, "schema_version", path);
            if (schemaVersion != 1)
            {
                throw new InvalidDataException(
                    $"prompt-catalog: {path} must declare schema_version: 1 (got {schemaVersion})");
            }

            if (!TryGetMapping(root, "prompts", out var promptsNode) || promptsNode is null)
            {
                // Files with no `prompts:` block are tolerated \u2014 reserves
                // a surface for a later phase to populate.
                return;
            }

            foreach (var kv in promptsNode.Children)
            {
                var name = (kv.Key as YamlScalarNode)?.Value
                    ?? throw new InvalidDataException(
                        $"prompt-catalog: {path} non-scalar prompt key");
                var body = kv.Value as YamlMappingNode
                    ?? throw new InvalidDataException(
                        $"prompt-catalog: {path} prompt '{name}' must be a mapping");

                string? systemPrompt = TryParseString(body, "system_prompt");
                string? userTemplate = TryParseString(body, "user_template");

                if (systemPrompt == null && userTemplate == null)
                {
                    throw new InvalidDataException(
                        $"prompt-catalog: {path} prompt '{name}' must declare " +
                        "at least one of system_prompt / user_template");
                }

                if (origin.TryGetValue(name, out var prior))
                {
                    throw new InvalidDataException(
                        $"prompt-catalog: duplicate prompt key '{name}' in {path} " +
                        $"(also defined in {prior})");
                }

                origin[name] = path;
                entries[name] = new PromptEntry(
                    systemPrompt: systemPrompt,
                    userTemplate: userTemplate);
            }
        }

        private static int ParseInt(YamlMappingNode node, string key, string path)
        {
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out var v))
            {
                throw new InvalidDataException(
                    $"prompt-catalog: {path} missing required key '{key}'");
            }
            var s = (v as YamlScalarNode)?.Value;
            if (!int.TryParse(s, out var i))
            {
                throw new InvalidDataException(
                    $"prompt-catalog: {path} key '{key}' must be an int (got '{s}')");
            }
            return i;
        }

        private static string? TryParseString(YamlMappingNode node, string key)
        {
            if (node.Children.TryGetValue(new YamlScalarNode(key), out var v)
                && v is YamlScalarNode scalar)
            {
                return scalar.Value;
            }
            return null;
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
    }

    /// <summary>
    /// One entry in the <see cref="PromptCatalog"/>: the system prompt
    /// (if any) and the user-message template (if any). Either may be
    /// null for prompts that are system-only or user-only.
    /// </summary>
    public sealed class PromptEntry
    {
        public string? SystemPrompt { get; }
        public string? UserTemplate { get; }

        public PromptEntry(string? systemPrompt, string? userTemplate)
        {
            SystemPrompt = systemPrompt;
            UserTemplate = userTemplate;
        }
    }
}

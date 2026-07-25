using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
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

        private static readonly string[] RuntimeSystemPromptKeys =
        {
            "dialogue-options-instruction",
            "datee-response-instruction",
            "interest-beat-instruction",
            "interest-beat-above15",
            "interest-beat-below8",
            "interest-beat-date-secured",
            "interest-beat-unmatched",
            "interest-beat-generic",
            "pivot-directive",
            "cold-opener-rule",
            "stake-coverage-summary",
            "stake-coverage-untouched-directive",
            "stake-coverage-all-referenced-directive",
            "player-transition-directive",
            "datee-transition-directive",
            "cognitive-subtext-directive",
            "stateful-previous-context-heading",
            "stateful-current-turn-heading",
            "engine-state-hfi-line",
            "engine-state-tor-line",
            "engine-state-cognitive-subtext-line",
            "engine-state-transition-target-line",
            "engine-state-transition-style-line",
            "conversation-history-heading",
            "conversation-history-empty",
            "dialogue-options-structured-json-instruction",
            "shadow-state-heading",
            "datee-shadow-state-heading",
            "shadow-taint-madness",
            "shadow-taint-despair",
            "shadow-taint-denial",
            "shadow-taint-fixation",
            "shadow-taint-dread",
            "shadow-taint-overthinking",
            "datee-reaction-fumble",
            "datee-reaction-misfire",
            "datee-reaction-trope-trap",
            "datee-reaction-catastrophe",
            "datee-reaction-legendary",
            "datee-horniness-reaction-below-threshold",
            "datee-horniness-reaction-high-interest",
            "datee-horniness-tier-intensity-fumble",
            "datee-horniness-tier-intensity-misfire",
            "datee-horniness-tier-intensity-trope-trap",
            "datee-horniness-tier-intensity-catastrophe",
            "interest-narrative-unmatched",
            "interest-narrative-bored",
            "interest-narrative-lukewarm",
            "interest-narrative-interested",
            "interest-narrative-very-into-it",
            "interest-narrative-almost-there",
            "interest-narrative-date-secured",
            "resistance-unmatched",
            "resistance-bored",
            "resistance-lukewarm",
            "resistance-interested",
            "resistance-very-into-it",
            "resistance-almost-there",
            "resistance-date-secured",
            "engine-options-block",
            "engine-datee-block",
        };

        private static readonly string[] RuntimeCompletePromptKeys =
        {
            "backstory",
            "dramatic_arc",
            "outfit",
            "stake",
            "backstory_consolidation",
            "bio",
            "personality_consolidation",
            "diagnosis",
            "character_generate",
        };

        private static readonly RuntimeTokenContract[] RuntimeTokenContracts =
        {
            SystemTokens("dialogue-options-instruction", "options_count", "player_name", "available_stats", "options_list"),
            SystemTokens("datee-response-instruction", "resistance_block", "length_hint"),
            SystemTokens("interest-beat-instruction", "datee_name", "interest_before", "interest_after", "threshold_instruction"),
            SystemTokens("interest-beat-above15", "datee_name"),
            SystemTokens("interest-beat-below8", "datee_name"),
            SystemTokens("interest-beat-date-secured", "datee_name"),
            SystemTokens("interest-beat-unmatched", "datee_name"),
            SystemTokens("interest-beat-generic", "datee_name"),
            SystemTokens("stake-coverage-summary", "referenced_count", "untouched_count"),
            SystemTokens("player-transition-directive", "player_name", "stem_text", "transition_style"),
            SystemTokens("datee-transition-directive", "stem_text", "transition_style"),
            SystemTokens("cognitive-subtext-directive", "cognitive_subtext"),
            SystemTokens("engine-state-hfi-line", "player_hfi", "datee_hfi"),
            SystemTokens("engine-state-tor-line", "player_tor", "datee_tor"),
            SystemTokens("engine-state-cognitive-subtext-line", "cognitive_subtext"),
            SystemTokens("engine-state-transition-target-line", "transition_target", "transition_scope"),
            SystemTokens("engine-state-transition-style-line", "transition_scope", "transition_style"),
            SystemTokens("dialogue-options-structured-json-instruction", "options_count", "available_stats"),
            SystemTokens("engine-options-block", "turn", "player_name", "game_state", "hfi_line", "tor_line",
                "cognitive_subtext_line", "transition_target_line", "transition_style_line", "options_count", "options_format_list"),
            SystemTokens("engine-datee-block", "datee_name", "interest", "interest_narrative",
                "cognitive_subtext_line", "transition_target_line", "transition_style_line"),
            UserTokens("backstory", "characterName", "genderIdentity", "bio", "consolidated_backstory", "consolidated_personality"),
            UserTokens("dramatic_arc", "playerName", "playerStake", "playerBio", "dateeName", "dateeStake", "dateeBio"),
            UserTokens("outfit", "playerName", "playerItems", "dateeName", "dateeItems"),
            UserTokens("stake", "character_profile"),
            UserTokens("backstory_consolidation", "game_system_prompt", "characterName", "genderIdentity",
                "bio", "stats", "backstory_fragments", "texting_style"),
            UserTokens("bio", "characterName", "genderIdentity", "backstory", "stakes", "diagnosis"),
            UserTokens("personality_consolidation", "game_system_prompt", "characterName", "genderIdentity",
                "bio", "stats", "personality_fragments", "texting_style"),
            UserTokens("diagnosis", "backstory", "stakes"),
            SystemTokens("character_generate", "items_catalogue", "anatomy_parameters"),
            UserTokens("character_generate", "existing_library", "smart_initialization"),
        };

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
        /// Resolve an explicitly supplied catalog or the globally wired prompt
        /// catalog, throwing the standard startup wiring error when neither
        /// exists.
        /// </summary>
        public static PromptCatalog ResolveCatalogOrThrow(PromptCatalog? catalog)
        {
            return catalog ?? PromptTemplates.Catalog
                ?? throw new InvalidOperationException(
                    "PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");
        }

        /// <summary>
        /// Look up a prompt entry and require the fields needed by setup LLM
        /// generators.
        /// </summary>
        public PromptEntry RequireCompleteEntry(string key, string missingKeyMessage)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (missingKeyMessage is null) throw new ArgumentNullException(nameof(missingKeyMessage));

            var entry = TryGet(key)
                ?? throw new InvalidOperationException(missingKeyMessage);
            if (string.IsNullOrWhiteSpace(entry.SystemPrompt))
                throw new InvalidOperationException($"prompt-catalog: key '{key}' has no system_prompt. Check the yaml file.");
            if (string.IsNullOrWhiteSpace(entry.UserTemplate))
                throw new InvalidOperationException($"prompt-catalog: key '{key}' has no user_template. Check the yaml file.");
            if (!entry.Temperature.HasValue)
                throw new InvalidOperationException($"prompt-catalog: key '{key}' has no temperature. Check the yaml file.");
            if (!entry.MaxTokens.HasValue)
                throw new InvalidOperationException($"prompt-catalog: key '{key}' has no max_tokens. Check the yaml file.");

            return entry;
        }

        /// <summary>
        /// Validates every prompt contract required by production gameplay,
        /// generation, synthesis, and character-card compilation.
        /// </summary>
        public void ValidateRuntimeCatalog()
        {
            foreach (string key in RuntimeSystemPromptKeys)
            {
                RequireField(key, useSystemPrompt: true);
            }

            foreach (string key in RuntimeCompletePromptKeys)
            {
                RequireCompleteEntry(
                    key,
                    $"prompt-catalog: missing required runtime prompt key '{key}'. The yaml file is incomplete or missing.");
            }

            foreach (var contract in RuntimeTokenContracts)
            {
                string template = contract.UseSystemPrompt
                    ? RequireField(contract.Key, useSystemPrompt: true).SystemPrompt!
                    : RequireField(contract.Key, useSystemPrompt: false).UserTemplate!;

                foreach (string token in contract.Tokens)
                {
                    string placeholder = "{" + token + "}";
                    if (template.IndexOf(placeholder, StringComparison.Ordinal) < 0)
                    {
                        string field = contract.UseSystemPrompt ? "system_prompt" : "user_template";
                        throw new InvalidOperationException(
                            $"prompt-catalog: key '{contract.Key}' {field} must include required token '{placeholder}'.");
                    }
                }
            }

            ValidateDiagnosisPromptContract(
                RequireField("diagnosis", useSystemPrompt: true).SystemPrompt!);

            PromptBuilder.ValidateStructuralPromptContracts(
                key => TryGet(key)?.SystemPrompt,
                key =>
                {
                    var entry = TryGet(key);
                    return entry == null
                        ? null
                        : new StructuralPromptResult(entry.SystemPrompt, entry.SourceFile);
                });

            EmotionalReactionPromptCatalog.ValidateRuntimeCatalog(this);
        }

        private static void ValidateDiagnosisPromptContract(string systemPrompt)
        {
            foreach (string field in TherapistDiagnosisContract.RequiredFields)
            {
                string jsonKey = "\"" + field + "\"";
                if (systemPrompt.IndexOf(jsonKey, StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        $"prompt-catalog: key 'diagnosis' system_prompt must include required JSON key '{field}'.");
                }
            }
        }

        private PromptEntry RequireField(string key, bool useSystemPrompt)
        {
            var entry = TryGet(key)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required runtime prompt key '{key}'. The yaml file is incomplete or missing.");
            string? value = useSystemPrompt ? entry.SystemPrompt : entry.UserTemplate;
            if (string.IsNullOrWhiteSpace(value))
            {
                string field = useSystemPrompt ? "system_prompt" : "user_template";
                throw new InvalidOperationException(
                    $"prompt-catalog: runtime prompt key '{key}' has no {field}. Check the yaml file.");
            }

            return entry;
        }

        private static RuntimeTokenContract SystemTokens(string key, params string[] tokens)
            => new RuntimeTokenContract(key, useSystemPrompt: true, tokens);

        private static RuntimeTokenContract UserTokens(string key, params string[] tokens)
            => new RuntimeTokenContract(key, useSystemPrompt: false, tokens);

        private sealed class RuntimeTokenContract
        {
            public RuntimeTokenContract(string key, bool useSystemPrompt, string[] tokens)
            {
                Key = key;
                UseSystemPrompt = useSystemPrompt;
                Tokens = tokens;
            }

            public string Key { get; }
            public bool UseSystemPrompt { get; }
            public string[] Tokens { get; }
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
                double? temperature = TryParseDouble(body, "temperature");
                int? maxTokens = TryParseIntOptional(body, "max_tokens");

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

                string normalizedPath = path.Replace('\\', '/');
                int idx = normalizedPath.IndexOf("data/prompts", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    normalizedPath = normalizedPath.Substring(idx);
                }

                origin[name] = path;
                entries[name] = new PromptEntry(
                    systemPrompt: systemPrompt,
                    userTemplate: userTemplate,
                    sourceFile: normalizedPath,
                    temperature: temperature,
                    maxTokens: maxTokens);
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

        private static double? TryParseDouble(YamlMappingNode node, string key)
        {
            if (node.Children.TryGetValue(new YamlScalarNode(key), out var v)
                && v is YamlScalarNode scalar)
            {
                if (double.TryParse(scalar.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    return d;
                }
            }
            return null;
        }

        private static int? TryParseIntOptional(YamlMappingNode node, string key)
        {
            if (node.Children.TryGetValue(new YamlScalarNode(key), out var v)
                && v is YamlScalarNode scalar)
            {
                if (int.TryParse(scalar.Value, out var i))
                {
                    return i;
                }
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
        public string? SourceFile { get; }
        public double? Temperature { get; }
        public int? MaxTokens { get; }

        public PromptEntry(string? systemPrompt, string? userTemplate, string? sourceFile = null, double? temperature = null, int? maxTokens = null)
        {
            SystemPrompt = systemPrompt;
            UserTemplate = userTemplate;
            SourceFile = sourceFile;
            Temperature = temperature;
            MaxTokens = maxTokens;
        }
    }
}

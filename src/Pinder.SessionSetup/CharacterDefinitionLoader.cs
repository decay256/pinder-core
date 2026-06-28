using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Loads a v1 character definition JSON file and runs the full
    /// <see cref="CharacterAssembler"/> + <see cref="PromptBuilder"/> pipeline
    /// to produce a <see cref="CharacterProfile"/>.
    ///
    /// v1 contract (issue #814):
    ///   - <c>schema_version: 1</c> is required. Missing or unknown values throw
    ///     <see cref="FormatException"/>.
    ///   - <c>character_id</c> is a required UUIDv4. Malformed values throw.
    ///   - On-disk shape stores allocation only (<c>allocation.spent</c>,
    ///     <c>allocation.unspent_pool</c>, <c>allocation.shadows</c>); item /
    ///     anatomy bonuses are recomputed every load.
    ///
    /// No back-compat with the prototype format.
    /// </summary>
    public static class CharacterDefinitionLoader
    {
        /// <summary>The schema version this loader understands (v2 as of #1175).</summary>
        public const int SupportedSchemaVersion = CharacterDefinition.CurrentSchemaVersion;

        /// <summary>
        /// Load a character definition from a JSON file and assemble it into
        /// a <see cref="CharacterProfile"/> ready for GameSession.
        /// </summary>
        /// <param name="jsonPath">Absolute or relative path to the character definition JSON file.</param>
        /// <param name="itemRepo">An <see cref="IItemRepository"/> loaded from starter-items.json.</param>
        /// <param name="anatomyRepo">An <see cref="IAnatomyRepository"/> loaded from anatomy-parameters.json.</param>
        /// <returns>A fully assembled <see cref="CharacterProfile"/>.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="FormatException">The JSON is malformed or missing required fields.</exception>
        public static CharacterProfile Load(
            string jsonPath,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo,
            bool archetypesEnabled = false)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Character definition file not found: {jsonPath}", jsonPath);

            string json = File.ReadAllText(jsonPath);
            return Parse(json, itemRepo, anatomyRepo, archetypesEnabled);
        }

        /// <summary>
        /// Parse a v1 character definition JSON string into a strongly-typed
        /// <see cref="CharacterDefinition"/>. Pure: no I/O, no assembler call.
        /// </summary>
        /// <exception cref="FormatException">The JSON is malformed or violates the v1 schema.</exception>
        public static CharacterDefinition ParseDefinition(string json)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new FormatException($"Failed to parse character definition: {ex.Message}", ex);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new FormatException("Character definition root must be a JSON object.");

                int schemaVersion = ParseSchemaVersion(root);
                Guid characterId = ParseCharacterId(root);

                string name = GetRequiredString(root, "name");
                string genderIdentity = GetRequiredString(root, "gender_identity");
                string bio = GetOptionalString(root, "bio") ?? string.Empty;
                int level = GetRequiredInt(root, "level");

                if (level < 1 || level > 11)
                    throw new FormatException($"Character level must be between 1 and 11, got: {level}");

                var items = ParseItemIds(root);
                var anatomy = ParseAnatomySelections(root);
                var allocation = ParseAllocation(root);

                // Issue #779: optional permanent psychological stake.
                string? psychologicalStake = null;
                if (root.TryGetProperty("psychological_stake", out var stakeProp) &&
                    stakeProp.ValueKind == JsonValueKind.String)
                {
                    string raw = stakeProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(raw))
                        psychologicalStake = raw.Trim();
                }

                // Issue #820: optional narrative background story.
                string? backgroundStory = null;
                if (root.TryGetProperty("background_story", out var storyProp) &&
                    storyProp.ValueKind == JsonValueKind.String)
                {
                    string raw = storyProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(raw))
                        backgroundStory = raw.Trim();
                }

                // Issue #1259: backstory categories
                IReadOnlyDictionary<string, BackstoryFact>? backstoryCategories = null;
                if (root.TryGetProperty("backstory_categories", out var catsProp) &&
                    catsProp.ValueKind == JsonValueKind.Object)
                {
                    backstoryCategories = ParseBackstoryCategories(catsProp);
                }

                return new CharacterDefinition(
                    schemaVersion,
                    characterId,
                    name,
                    genderIdentity,
                    bio,
                    level,
                    items,
                    anatomy,
                    allocation,
                    psychologicalStake,
                    backgroundStory,
                    backstoryCategories);
            }
        }

        /// <summary>
        /// Parse a v1 character definition JSON string and assemble it into a
        /// <see cref="CharacterProfile"/>. Exposed publicly so callers (e.g.
        /// the GameApi character generator) can validate freshly composed JSON
        /// without first writing it to disk.
        /// </summary>
        public static CharacterProfile Parse(
            string json,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo,
            bool archetypesEnabled = false)
        {
            if (itemRepo == null) throw new ArgumentNullException(nameof(itemRepo));
            if (anatomyRepo == null) throw new ArgumentNullException(nameof(anatomyRepo));

            CharacterDefinition def = ParseDefinition(json);
            return Assemble(def, itemRepo, anatomyRepo, archetypesEnabled);
        }

        /// <summary>
        /// Run a parsed <see cref="CharacterDefinition"/> through the assembly
        /// pipeline to produce a <see cref="CharacterProfile"/>. Bonuses are
        /// derived from items / anatomy at this point and do NOT round-trip
        /// back to the on-disk file.
        /// </summary>
        public static CharacterProfile Assemble(
            CharacterDefinition def,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo,
            bool archetypesEnabled = false)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (itemRepo == null) throw new ArgumentNullException(nameof(itemRepo));
            if (anatomyRepo == null) throw new ArgumentNullException(nameof(anatomyRepo));

            var assembler = new CharacterAssembler(itemRepo, anatomyRepo);
            var fragments = assembler.Assemble(
                def.Items,
                def.Anatomy,
                def.Allocation.Spent,
                def.Allocation.Shadows,
                def.Level,
                archetypesEnabled: archetypesEnabled);

            // #836 placeholder aggregation: use the character UUID as the
            // stable seed so the system prompt and the runtime
            // PlayerTextingStyle agree on which items were picked. Anatomy
            // contributions are silenced in the texting-style channel by
            // the aggregator.
            string textingSeed = def.CharacterId.ToString("D");

            string systemPrompt = PromptBuilder.BuildSystemPrompt(
                def.Name, def.GenderIdentity, def.Bio, fragments, new TrapState(),
                characterIdSeed: textingSeed,
                archetypesEnabled: archetypesEnabled);

            // #907: Use AggregateWithAudit so conflict drops are visible at
            // session-creation time. ConflictCatalog is loaded by PromptWiring.Wire();
            // if Wire() was not called (e.g. some test contexts) it falls back to Empty.
            var aggregationResult = TextingStyleAggregator.AggregateWithAudit(
                fragments.TextingStyleSources,
                textingSeed,
                TextingStyleAggregator.ConflictCatalog ?? TextingStyleConflicts.Empty);
            foreach (var drop in aggregationResult.Drops)
                Console.Error.WriteLine(drop.ToString());
            string textingStyle = aggregationResult.Lines.Count == 0
                ? string.Empty
                : string.Join(" | ", aggregationResult.Lines);

            var itemDisplayNames = new List<string>();
            foreach (var itemId in def.Items)
            {
                var item = itemRepo.GetItem(itemId);
                if (item != null && !string.IsNullOrWhiteSpace(item.DisplayName))
                    itemDisplayNames.Add(item.DisplayName);
            }

            var profile = new CharacterProfile(
                fragments.Stats, systemPrompt, def.Name, fragments.Timing, def.Level,
                bio: def.Bio,
                textingStyleFragment: textingStyle,
                activeArchetype: fragments.ActiveArchetype,
                equippedItemDisplayNames: itemDisplayNames,
                textingStyleSources: fragments.TextingStyleSources,
                // #562: thread gender_identity through to the profile so
                // the DateeVisibleProfile DTO can surface it as a
                // Tinder-card-equivalent field. Was previously read here
                // and only used for the assembled system prompt.
                genderIdentity: def.GenderIdentity,
                // #781: expose the final aggregated texting-style lines
                // the admin-facing character sheet can show the full
                // composed template without re-running the aggregator.
                textingStyleLines: aggregationResult.Lines,
                backstoryCategories: def.BackstoryCategories);

            // Issue #779: propagate the permanent stake from the definition
            // to the profile so setup can read it without an LLM call.
            profile.PsychologicalStake = def.PsychologicalStake;

            return profile;
        }

        // --- v1 schema parsing ------------------------------------------------

        private static int ParseSchemaVersion(JsonElement root)
        {
            if (!root.TryGetProperty("schema_version", out var prop))
            {
                throw new FormatException(
                    $"Character definition missing required field: schema_version (expected {SupportedSchemaVersion}).");
            }
            if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out int version))
            {
                throw new FormatException(
                    $"Character definition schema_version must be an integer (expected {SupportedSchemaVersion}).");
            }
            if (version != SupportedSchemaVersion)
            {
                throw new FormatException(
                    $"Unknown character schema_version: {version} (this loader supports v{SupportedSchemaVersion} only).");
            }
            return version;
        }
        private static Guid ParseCharacterId(JsonElement root)
        {
            if (!root.TryGetProperty("character_id", out var prop) ||
                prop.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Character definition missing required field: character_id");
            }
            string raw = prop.GetString()!;
            if (!Guid.TryParseExact(raw, "D", out var id))
            {
                throw new FormatException($"Character definition character_id is not a valid UUID: '{raw}'");
            }
            return id;
        }

        private static string GetRequiredString(JsonElement root, string fieldName)
        {
            string? val = GetOptionalString(root, fieldName);
            if (val == null) throw new FormatException($"Character definition missing required field: {fieldName}");
            return val;
        }

        private static string? GetOptionalString(JsonElement root, string fieldName)
        {
            if (!root.TryGetProperty(fieldName, out var prop) ||
                prop.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return prop.GetString()!;
        }

        private static int GetRequiredInt(JsonElement root, string fieldName)
        {
            if (!root.TryGetProperty(fieldName, out var prop) ||
                prop.ValueKind != JsonValueKind.Number)
            {
                throw new FormatException($"Character definition missing required field: {fieldName}");
            }
            return prop.GetInt32();
        }

        private static List<string> ParseItemIds(JsonElement root)
        {
            if (!root.TryGetProperty("items", out var prop) ||
                prop.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Character definition missing required field: items");
            }

            var items = new List<string>();
            foreach (var element in prop.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                    items.Add(element.GetString()!);
            }
            return items;
        }

        private static Dictionary<string, float> ParseAnatomySelections(JsonElement root)
        {
            if (!root.TryGetProperty("anatomy", out var prop) ||
                prop.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Character definition missing required field: anatomy");
            }

            var anatomy = new Dictionary<string, float>();
            foreach (var kv in prop.EnumerateObject())
            {
                // v2: float values
                if (kv.Value.ValueKind == JsonValueKind.Number)
                {
                    anatomy[kv.Name] = kv.Value.GetSingle();
                }
                // Tolerate booleans (isCircumcised may appear as JSON bool)
                else if (kv.Value.ValueKind == JsonValueKind.True)
                {
                    anatomy[kv.Name] = 1.0f;
                }
                else if (kv.Value.ValueKind == JsonValueKind.False)
                {
                    anatomy[kv.Name] = 0.0f;
                }
                // Silently skip any other token types (strings, null)
            }
            return anatomy;
        }

        private static AllocationBlock ParseAllocation(JsonElement root)
        {
            if (!root.TryGetProperty("allocation", out var alloc) ||
                alloc.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Character definition missing required field: allocation");
            }

            var spent = ParseSpent(alloc);
            int unspent = 0;
            if (alloc.TryGetProperty("unspent_pool", out var pool))
            {
                if (pool.ValueKind != JsonValueKind.Number || !pool.TryGetInt32(out unspent))
                    throw new FormatException("allocation.unspent_pool must be an integer.");
                if (unspent < 0)
                    throw new FormatException("allocation.unspent_pool must be non-negative.");
            }
            var shadows = ParseShadowsBlock(alloc);

            return new AllocationBlock(spent, unspent, shadows);
        }

        private static Dictionary<StatType, int> ParseSpent(JsonElement alloc)
        {
            if (!alloc.TryGetProperty("spent", out var prop) ||
                prop.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Character definition missing required field: allocation.spent");
            }

            var spent = new Dictionary<StatType, int>();
            foreach (var kv in prop.EnumerateObject())
            {
                if (!TryParseStatType(kv.Name, out var statType))
                    throw new FormatException($"Unknown stat type: {kv.Name}");
                if (kv.Value.ValueKind != JsonValueKind.Number)
                    throw new FormatException($"Build point value for {kv.Name} must be a number");
                spent[statType] = kv.Value.GetInt32();
            }
            return spent;
        }

        private static Dictionary<ShadowStatType, int> ParseShadowsBlock(JsonElement alloc)
        {
            var shadows = new Dictionary<ShadowStatType, int>();
            // Default all to 0
            foreach (ShadowStatType sst in Enum.GetValues(typeof(ShadowStatType)))
                shadows[sst] = 0;

            if (!alloc.TryGetProperty("shadows", out var prop) ||
                prop.ValueKind != JsonValueKind.Object)
            {
                return shadows;
            }

            foreach (var kv in prop.EnumerateObject())
            {
                if (!TryParseShadowStatType(kv.Name, out var shadowType))
                    throw new FormatException($"Unknown shadow stat type: {kv.Name}");
                if (kv.Value.ValueKind != JsonValueKind.Number)
                    throw new FormatException($"Shadow value for {kv.Name} must be a number");
                shadows[shadowType] = kv.Value.GetInt32();
            }
            return shadows;
        }

        private static bool TryParseStatType(string key, out StatType result)
        {
            switch (key.ToLowerInvariant())
            {
                case "charm":          result = StatType.Charm;         return true;
                case "rizz":           result = StatType.Rizz;          return true;
                case "honesty":        result = StatType.Honesty;       return true;
                case "chaos":          result = StatType.Chaos;         return true;
                case "wit":            result = StatType.Wit;           return true;
                case "self_awareness": result = StatType.SelfAwareness; return true;
                default:               result = default;                return false;
            }
        }

        private static bool TryParseShadowStatType(string key, out ShadowStatType result)
        {
            switch (key.ToLowerInvariant())
            {
                case "madness":       result = ShadowStatType.Madness;       return true;
                case "despair":       result = ShadowStatType.Despair;       return true;
                case "horniness":     result = ShadowStatType.Despair;       return true; // legacy alias
                case "denial":        result = ShadowStatType.Denial;        return true;
                case "fixation":      result = ShadowStatType.Fixation;      return true;
                case "dread":         result = ShadowStatType.Dread;         return true;
                case "overthinking":  result = ShadowStatType.Overthinking;  return true;
                default:              result = default;                       return false;
            }
        }

        private static Dictionary<string, BackstoryFact> ParseBackstoryCategories(JsonElement root)
        {
            var categories = new Dictionary<string, BackstoryFact>();
            foreach (var kv in root.EnumerateObject())
            {
                if (kv.Value.ValueKind != JsonValueKind.Object) continue;
                
                var fact = new BackstoryFact();
                
                // Handle both snake_case and PascalCase
                if (kv.Value.TryGetProperty("bio_lie", out var bioLieProp) && bioLieProp.ValueKind == JsonValueKind.String)
                    fact.BioLie = bioLieProp.GetString() ?? string.Empty;
                else if (kv.Value.TryGetProperty("BioLie", out var bioLiePropPascal) && bioLiePropPascal.ValueKind == JsonValueKind.String)
                    fact.BioLie = bioLiePropPascal.GetString() ?? string.Empty;

                if (kv.Value.TryGetProperty("tragic_reality", out var trProp) && trProp.ValueKind == JsonValueKind.String)
                    fact.TragicReality = trProp.GetString() ?? string.Empty;
                else if (kv.Value.TryGetProperty("TragicReality", out var trPropPascal) && trPropPascal.ValueKind == JsonValueKind.String)
                    fact.TragicReality = trPropPascal.GetString() ?? string.Empty;

                categories[kv.Name] = fact;
            }
            return categories;
        }
    }
}

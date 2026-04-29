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
    /// Loads a character definition JSON file and runs the full
    /// CharacterAssembler + PromptBuilder pipeline to produce a CharacterProfile.
    /// </summary>
    public static class CharacterDefinitionLoader
    {
        /// <summary>
        /// Load a character definition from a JSON file and assemble it into
        /// a CharacterProfile ready for GameSession.
        /// </summary>
        /// <param name="jsonPath">Absolute or relative path to the character definition JSON file.</param>
        /// <param name="itemRepo">An IItemRepository loaded from starter-items.json.</param>
        /// <param name="anatomyRepo">An IAnatomyRepository loaded from anatomy-parameters.json.</param>
        /// <returns>A fully assembled CharacterProfile.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="FormatException">The JSON is malformed or missing required fields.</exception>
        public static CharacterProfile Load(
            string jsonPath,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Character definition file not found: {jsonPath}", jsonPath);

            string json = File.ReadAllText(jsonPath);
            return Parse(json, itemRepo, anatomyRepo);
        }

        /// <summary>
        /// Parse a character definition JSON string and assemble it into a CharacterProfile.
        /// Exposed publicly so callers (e.g. the GameApi character generator,
        /// issues #330/#331) can validate freshly composed JSON without first
        /// writing it to disk.
        /// </summary>
        public static CharacterProfile Parse(
            string json,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo)
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

                // Extract required fields
                string name = GetRequiredString(root, "name");
                string genderIdentity = GetRequiredString(root, "gender_identity");
                string bio = GetRequiredString(root, "bio");
                int level = GetRequiredInt(root, "level");

                if (level < 1 || level > 11)
                    throw new FormatException($"Character level must be between 1 and 11, got: {level}");

                // Parse items array
                var items = ParseItemIds(root);

                // Parse anatomy selections
                var anatomy = ParseAnatomySelections(root);

                // Parse build points
                var buildPoints = ParseBuildPoints(root);

                // Parse shadows (optional — defaults to all zeros)
                var shadows = ParseShadows(root);

                // Run assembly pipeline
                var assembler = new CharacterAssembler(itemRepo, anatomyRepo);
                var fragments = assembler.Assemble(items, anatomy, buildPoints, shadows, level);

                // Build system prompt
                string systemPrompt = PromptBuilder.BuildSystemPrompt(
                    name, genderIdentity, bio, fragments, new TrapState());

                // Join texting style fragments for voice reinforcement
                string textingStyle = fragments.TextingStyleFragments.Count > 0
                    ? string.Join(" | ", fragments.TextingStyleFragments)
                    : string.Empty;

                // Collect item display names for visible profile (shown to opposing player at T1)
                var itemDisplayNames = new System.Collections.Generic.List<string>();
                foreach (var itemId in items)
                {
                    var item = itemRepo.GetItem(itemId);
                    if (item != null && !string.IsNullOrWhiteSpace(item.DisplayName))
                        itemDisplayNames.Add(item.DisplayName);
                }

                // Construct CharacterProfile
                return new CharacterProfile(
                    fragments.Stats, systemPrompt, name, fragments.Timing, level,
                    bio: bio,
                    textingStyleFragment: textingStyle,
                    activeArchetype: fragments.ActiveArchetype,
                    equippedItemDisplayNames: itemDisplayNames);
            }
        }

        private static string GetRequiredString(JsonElement root, string fieldName)
        {
            if (!root.TryGetProperty(fieldName, out var prop) ||
                prop.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"Character definition missing required field: {fieldName}");
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

        private static Dictionary<string, string> ParseAnatomySelections(JsonElement root)
        {
            if (!root.TryGetProperty("anatomy", out var prop) ||
                prop.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Character definition missing required field: anatomy");
            }

            var anatomy = new Dictionary<string, string>();
            foreach (var kv in prop.EnumerateObject())
            {
                if (kv.Value.ValueKind == JsonValueKind.String)
                    anatomy[kv.Name] = kv.Value.GetString()!;
            }
            return anatomy;
        }

        private static Dictionary<StatType, int> ParseBuildPoints(JsonElement root)
        {
            if (!root.TryGetProperty("build_points", out var prop) ||
                prop.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Character definition missing required field: build_points");
            }

            var buildPoints = new Dictionary<StatType, int>();
            foreach (var kv in prop.EnumerateObject())
            {
                if (!TryParseStatType(kv.Name, out var statType))
                    throw new FormatException($"Unknown stat type: {kv.Name}");
                if (kv.Value.ValueKind != JsonValueKind.Number)
                    throw new FormatException($"Build point value for {kv.Name} must be a number");
                buildPoints[statType] = kv.Value.GetInt32();
            }
            return buildPoints;
        }

        private static Dictionary<ShadowStatType, int> ParseShadows(JsonElement root)
        {
            var shadows = new Dictionary<ShadowStatType, int>();

            // Default all to 0
            foreach (ShadowStatType sst in Enum.GetValues(typeof(ShadowStatType)))
                shadows[sst] = 0;

            if (!root.TryGetProperty("shadows", out var prop) ||
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
    }
}

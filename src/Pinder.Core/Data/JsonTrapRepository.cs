using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Data
{
    /// <summary>
    /// Parses trap JSON data and exposes trap definitions via ITrapRegistry.
    /// Follows the same pattern as JsonItemRepository: caller passes in the JSON string.
    /// </summary>
    public sealed class JsonTrapRepository : ITrapRegistry
    {
        private static readonly HashSet<string> AllowedProperties =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "id",
                "display_name",
                "summary",
                "stat",
                "effect",
                "effect_value",
                "duration_turns",
                "llm_instruction",
                "clear_method",
                "nat1_bonus"
            };

        private static readonly string[] RequiredProperties =
        {
            "id",
            "stat",
            "effect",
            "effect_value",
            "duration_turns",
            "llm_instruction"
        };

        private readonly Dictionary<StatType, TrapDefinition> _traps =
            new Dictionary<StatType, TrapDefinition>();

        /// <summary>
        /// Load traps from a JSON string. Expects a top-level array of trap objects.
        /// </summary>
        /// <param name="json">Full JSON string — the contents of traps.json.</param>
        public JsonTrapRepository(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            ParseAndLoad(json);
        }

        /// <summary>
        /// Load traps from a primary JSON string plus additional custom trap JSON strings.
        /// </summary>
        /// <param name="json">Primary traps JSON string.</param>
        /// <param name="customJsonFiles">Additional JSON strings for custom traps (each a top-level array).</param>
        public JsonTrapRepository(string json, IEnumerable<string> customJsonFiles)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            ParseAndLoad(json);

            if (customJsonFiles != null)
            {
                foreach (var customJson in customJsonFiles)
                {
                    if (!string.IsNullOrWhiteSpace(customJson))
                        ParseAndLoad(customJson);
                }
            }
        }

        /// <inheritdoc />
        public TrapDefinition? GetTrap(StatType stat)
        {
            _traps.TryGetValue(stat, out var trap);
            return trap;
        }

        /// <inheritdoc />
        public string? GetLlmInstruction(StatType stat)
        {
            _traps.TryGetValue(stat, out var trap);
            return trap?.LlmInstruction;
        }

        /// <summary>Returns all loaded trap definitions.</summary>
        public IEnumerable<TrapDefinition> GetAll() => _traps.Values;

        // -------------------------------------------------------------------

        private void ParseAndLoad(string json)
        {
            var root = JsonParser.Parse(json);
            if (!(root is JsonArray arr))
                throw new FormatException("Expected top-level JSON array for traps.");

            for (int i = 0; i < arr.Items.Count; i++)
            {
                var element = arr.Items[i];
                if (!(element is JsonObject obj))
                    throw new FormatException($"Trap definition at index {i} must be a JSON object.");

                var trap = ParseTrap(obj);
                // Later entries override earlier ones for the same stat
                _traps[trap.Stat] = trap;
            }
        }

        private static TrapDefinition ParseTrap(JsonObject obj)
        {
            ValidateProperties(obj);

            string id = GetRequiredString(obj, "id", "Trap definition");
            if (string.IsNullOrEmpty(id))
                throw new FormatException("Trap definition missing required field 'id'.");

            string statStr = GetRequiredString(obj, "stat", $"Trap '{id}'");
            if (!TryParseStatType(statStr, out StatType stat))
                throw new FormatException($"Trap '{id}': unknown stat '{statStr}'.");

            string effectStr = GetRequiredString(obj, "effect", $"Trap '{id}'");
            if (!TryParseTrapEffect(effectStr, out TrapEffect effect))
                throw new FormatException($"Trap '{id}': unknown effect '{effectStr}'.");

            int effectValue = GetRequiredInt(obj, "effect_value", $"Trap '{id}'");
            if (effectValue < 0)
                throw new FormatException($"Trap '{id}': field 'effect_value' must be greater than or equal to 0.");

            int durationTurns = GetRequiredInt(obj, "duration_turns", $"Trap '{id}'");
            if (durationTurns < 1)
                throw new FormatException($"Trap '{id}': field 'duration_turns' must be greater than or equal to 1.");

            string llmInstruction = GetRequiredString(obj, "llm_instruction", $"Trap '{id}'");
            if (string.IsNullOrEmpty(llmInstruction))
                throw new FormatException($"Trap '{id}': missing required field 'llm_instruction'.");

            string clearMethod = GetOptionalString(obj, "clear_method", $"Trap '{id}'", "");
            string nat1Bonus = GetOptionalString(obj, "nat1_bonus", $"Trap '{id}'", "");

            // #255: optional player-facing copy. Both default to safe values
            // (display_name → id, summary → "") so legacy data files keep
            // loading without changes.
            string displayName = GetOptionalString(obj, "display_name", $"Trap '{id}'", "");
            string summary = GetOptionalString(obj, "summary", $"Trap '{id}'", "");

            return new TrapDefinition(
                id, stat, effect, effectValue, durationTurns,
                llmInstruction, clearMethod, nat1Bonus,
                displayName, summary);
        }

        private static void ValidateProperties(JsonObject obj)
        {
            foreach (var key in obj.Properties.Keys)
            {
                if (!AllowedProperties.Contains(key))
                    throw new FormatException($"Trap definition has unknown field '{key}'.");
            }

            foreach (var key in RequiredProperties)
            {
                if (!obj.Properties.ContainsKey(key))
                    throw new FormatException($"Trap definition missing required field '{key}'.");
            }
        }

        private static string GetRequiredString(JsonObject obj, string key, string context)
        {
            if (!obj.Properties.TryGetValue(key, out var value))
                throw new FormatException($"{context}: missing required field '{key}'.");

            if (value is JsonString jsonString)
                return jsonString.Value;

            throw new FormatException($"{context}: field '{key}' must be a string.");
        }

        private static string GetOptionalString(JsonObject obj, string key, string context, string defaultValue)
        {
            if (!obj.Properties.TryGetValue(key, out var value))
                return defaultValue;

            if (value is JsonString jsonString)
                return jsonString.Value;

            throw new FormatException($"{context}: field '{key}' must be a string.");
        }

        private static int GetRequiredInt(JsonObject obj, string key, string context)
        {
            if (!obj.Properties.TryGetValue(key, out var value))
                throw new FormatException($"{context}: missing required field '{key}'.");

            if (!(value is JsonNumber number))
                throw new FormatException($"{context}: field '{key}' must be an integer.");

            var raw = number.Value;
            if (raw < int.MinValue || raw > int.MaxValue || Math.Truncate(raw) != raw)
                throw new FormatException($"{context}: field '{key}' must be an integer.");

            return (int)raw;
        }

        private static bool TryParseStatType(string key, out StatType st)
        {
            switch (key)
            {
                case "charm":          st = StatType.Charm;         return true;
                case "rizz":           st = StatType.Rizz;          return true;
                case "honesty":        st = StatType.Honesty;       return true;
                case "chaos":          st = StatType.Chaos;         return true;
                case "wit":            st = StatType.Wit;           return true;
                case "self_awareness": st = StatType.SelfAwareness; return true;
                default:               st = default;                return false;
            }
        }

        private static bool TryParseTrapEffect(string key, out TrapEffect effect)
        {
            switch (key)
            {
                case "disadvantage":        effect = TrapEffect.Disadvantage;      return true;
                case "stat_penalty":        effect = TrapEffect.StatPenalty;        return true;
                case "datee_dc_increase": effect = TrapEffect.DateeDCIncrease; return true;
                default:                    effect = default;                       return false;
            }
        }
    }
}

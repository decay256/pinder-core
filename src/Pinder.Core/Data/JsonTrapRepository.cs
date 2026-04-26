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

            foreach (var element in arr.Items)
            {
                if (!(element is JsonObject obj)) continue;
                var trap = ParseTrap(obj);
                // Later entries override earlier ones for the same stat
                _traps[trap.Stat] = trap;
            }
        }

        private static TrapDefinition ParseTrap(JsonObject obj)
        {
            string id = obj.GetString("id");
            if (string.IsNullOrEmpty(id))
                throw new FormatException("Trap definition missing required field 'id'.");

            string statStr = obj.GetString("stat");
            if (!TryParseStatType(statStr, out StatType stat))
                throw new FormatException($"Trap '{id}': unknown stat '{statStr}'.");

            string effectStr = obj.GetString("effect");
            if (!TryParseTrapEffect(effectStr, out TrapEffect effect))
                throw new FormatException($"Trap '{id}': unknown effect '{effectStr}'.");

            int effectValue = obj.GetInt("effect_value");
            int durationTurns = obj.GetInt("duration_turns", 3);

            string llmInstruction = obj.GetString("llm_instruction");
            if (string.IsNullOrEmpty(llmInstruction))
                throw new FormatException($"Trap '{id}': missing required field 'llm_instruction'.");

            string clearMethod = obj.GetString("clear_method", "");
            string nat1Bonus = obj.GetString("nat1_bonus", "");

            // #255: optional player-facing copy. Both default to safe values
            // (display_name → id, summary → "") so legacy data files keep
            // loading without changes.
            string displayName = obj.GetString("display_name", "");
            string summary = obj.GetString("summary", "");

            return new TrapDefinition(
                id, stat, effect, effectValue, durationTurns,
                llmInstruction, clearMethod, nat1Bonus,
                displayName, summary);
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
                case "opponent_dc_increase": effect = TrapEffect.OpponentDCIncrease; return true;
                default:                    effect = default;                       return false;
            }
        }
    }
}

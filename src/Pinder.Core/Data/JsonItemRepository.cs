using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Data
{
    /// <summary>
    /// Parses starter-items.json (or any JSON conforming to the item schema)
    /// and exposes items via IItemRepository.
    /// </summary>
    public sealed class JsonItemRepository : IItemRepository
    {
        private readonly Dictionary<string, ItemDefinition> _items =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

        /// <param name="json">Full JSON string — the contents of starter-items.json.</param>
        public JsonItemRepository(string json)
        {
            var root = JsonParser.Parse(json);
            if (!(root is JsonArray arr))
                throw new FormatException("Expected top-level JSON array for items.");

            foreach (var element in arr.Items)
            {
                if (!(element is JsonObject obj)) continue;
                var item = ParseItem(obj);
                _items[item.ItemId] = item;
            }
        }

        public ItemDefinition? GetItem(string itemId)
        {
            _items.TryGetValue(itemId, out var item);
            return item;
        }

        public IEnumerable<ItemDefinition> GetAll() => _items.Values;

        // -------------------------------------------------------------------

        private static ItemDefinition ParseItem(JsonObject obj)
        {
            string itemId      = obj.GetString("item_id");
            string displayName = obj.GetString("display_name");
            string slot        = obj.GetString("slot");
            string tier        = obj.GetString("tier");

            var statMods = ParseStatModifiers(obj.GetObject("stat_modifiers"));

            string personality  = obj.GetString("personality_fragment");
            string backstory    = obj.GetString("backstory_fragment");
            string texting      = obj.GetString("texting_style_fragment");
            string[] archetypes = ParseStringArray(obj.GetArray("archetype_tendencies"));
            var timing          = ParseTimingModifier(obj.GetObject("response_timing_modifier"));

            return new ItemDefinition(
                itemId, displayName, slot, tier, statMods,
                personality, backstory, texting, archetypes, timing);
        }

        internal static IReadOnlyDictionary<StatType, int> ParseStatModifiers(JsonObject? obj)
        {
            var dict = new Dictionary<StatType, int>();
            if (obj == null) return dict;
            foreach (var kv in obj.Properties)
            {
                if (kv.Value is JsonNumber n && TryParseStatType(kv.Key, out StatType st))
                    dict[st] = n.ToInt();
            }
            return dict;
        }

        internal static bool TryParseStatType(string key, out StatType st)
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

        internal static string[] ParseStringArray(JsonArray? arr)
        {
            if (arr == null) return Array.Empty<string>();
            var list = new List<string>(arr.Items.Count);
            foreach (var item in arr.Items)
                if (item is JsonString s) list.Add(s.Value);
            return list.ToArray();
        }

        internal static TimingModifier ParseTimingModifier(JsonObject? obj)
        {
            if (obj == null) return TimingModifier.Zero;
            int   delayDelta  = obj.GetInt("base_delay_delta_minutes");
            float varianceMult = obj.GetFloat("delay_variance_multiplier", 1.0f);
            float drySpell    = obj.GetFloat("dry_spell_probability_delta");
            string receipt    = obj.GetString("read_receipt", "neutral");
            return new TimingModifier(delayDelta, varianceMult, drySpell, receipt);
        }
    }
}

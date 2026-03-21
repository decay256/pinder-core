using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Data
{
    /// <summary>
    /// Parses anatomy-parameters.json and exposes parameters via IAnatomyRepository.
    /// </summary>
    public sealed class JsonAnatomyRepository : IAnatomyRepository
    {
        private readonly Dictionary<string, AnatomyParameterDefinition> _params =
            new Dictionary<string, AnatomyParameterDefinition>(StringComparer.OrdinalIgnoreCase);

        /// <param name="json">Full JSON string — the contents of anatomy-parameters.json.</param>
        public JsonAnatomyRepository(string json)
        {
            var root = JsonParser.Parse(json);
            if (!(root is JsonArray arr))
                throw new FormatException("Expected top-level JSON array for anatomy parameters.");

            foreach (var element in arr.Items)
            {
                if (!(element is JsonObject obj)) continue;
                var param = ParseParameter(obj);
                _params[param.Id] = param;
            }
        }

        public AnatomyParameterDefinition? GetParameter(string parameterId)
        {
            _params.TryGetValue(parameterId, out var p);
            return p;
        }

        public IEnumerable<AnatomyParameterDefinition> GetAll() => _params.Values;

        // -------------------------------------------------------------------

        private static AnatomyParameterDefinition ParseParameter(JsonObject obj)
        {
            string id   = obj.GetString("id");
            string name = obj.GetString("name");

            var tiersArr = obj.GetArray("tiers");
            var tiers    = new List<AnatomyTierDefinition>();

            if (tiersArr != null)
            {
                foreach (var elem in tiersArr.Items)
                {
                    if (!(elem is JsonObject tierObj)) continue;
                    tiers.Add(ParseTier(id, tierObj));
                }
            }

            return new AnatomyParameterDefinition(id, name, tiers);
        }

        private static AnatomyTierDefinition ParseTier(string parameterId, JsonObject obj)
        {
            string tierId   = obj.GetString("id");
            string tierName = obj.GetString("name");

            // Skin Tone tiers: visual_description only, no modifiers/fragments.
            bool isVisualOnly = obj.HasKey("visual_description") &&
                                !obj.HasKey("personality_fragment");

            if (isVisualOnly)
            {
                string visual = obj.GetString("visual_description");
                return new AnatomyTierDefinition(
                    parameterId, tierId, tierName,
                    new Dictionary<StatType, int>(),
                    null, null, null,
                    Array.Empty<string>(),
                    TimingModifier.Zero,
                    visual);
            }

            var statMods  = JsonItemRepository.ParseStatModifiers(obj.GetObject("stat_modifiers"));
            string personality = obj.GetString("personality_fragment");
            string backstory   = obj.GetString("backstory_fragment");
            string texting     = obj.GetString("texting_style_fragment");
            string[] archetypes = JsonItemRepository.ParseStringArray(obj.GetArray("archetype_tendencies"));
            var timing = JsonItemRepository.ParseTimingModifier(obj.GetObject("response_timing_modifier"));

            return new AnatomyTierDefinition(
                parameterId, tierId, tierName,
                statMods, personality, backstory, texting, archetypes, timing);
        }
    }
}

using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Data
{
    /// <summary>
    /// Parses the scalar-banded anatomy-parameters.json and exposes parameters
    /// via <see cref="IAnatomyRepository"/>.
    ///
    /// As of issue #1175, each parameter record has the shape:
    /// <code>
    /// {
    ///   "id": "trunkLengthBase",
    ///   "name": "Trunk Length Base",
    ///   "bands": [
    ///     {
    ///       "lower": 0.00, "upper": 0.05,
    ///       "personality_fragment": "...",   // optional
    ///       "backstory_fragment": "...",     // optional
    ///       "texting_style_fragment": "...", // optional
    ///       "archetype_tendencies": [...],   // optional
    ///       "response_timing_modifier": {...}, // optional
    ///       "stat_modifiers": {...}          // optional
    ///     },
    ///     ...
    ///   ]
    /// }
    /// </code>
    /// All band fields except <c>lower</c> and <c>upper</c> are optional.
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

            var bandsArr = obj.GetArray("bands");
            var bands = new List<AnatomyBandDefinition>();

            if (bandsArr != null)
            {
                foreach (var elem in bandsArr.Items)
                {
                    if (!(elem is JsonObject bandObj)) continue;
                    bands.Add(ParseBand(bandObj));
                }
            }

            return new AnatomyParameterDefinition(id, name, bands);
        }

        private static AnatomyBandDefinition ParseBand(JsonObject obj)
        {
            float lower = obj.GetFloat("lower", 0f);
            float upper = obj.GetFloat("upper", 1f);

            string? personality = obj.HasKey("personality_fragment")
                ? NullIfEmpty(obj.GetString("personality_fragment"))
                : null;
            string? backstory = obj.HasKey("backstory_fragment")
                ? NullIfEmpty(obj.GetString("backstory_fragment"))
                : null;
            string? texting = obj.HasKey("texting_style_fragment")
                ? NullIfEmpty(obj.GetString("texting_style_fragment"))
                : null;

            string[] archetypes = JsonItemRepository.ParseStringArray(obj.GetArray("archetype_tendencies"));
            TimingModifier timing = JsonItemRepository.ParseTimingModifier(obj.GetObject("response_timing_modifier"));
            IReadOnlyDictionary<StatType, int> statMods = JsonItemRepository.ParseStatModifiers(obj.GetObject("stat_modifiers"));

            return new AnatomyBandDefinition(
                lower, upper,
                personality, backstory, texting,
                archetypes, timing, statMods);
        }

        private static string? NullIfEmpty(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}

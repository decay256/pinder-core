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
    /// <remarks>
    /// As of #551 (admin-content-editor sprint Phase 2a), parameter records
    /// may carry a <c>scale_type</c> field (<c>"numeric"</c> or <c>"categorical"</c>)
    /// plus, for numeric parameters, a <c>numeric_range</c> object with
    /// <c>min</c>/<c>max</c>/<c>unit</c> keys. Tiers within numeric parameters
    /// may carry a <c>numeric_breakpoint</c> integer.
    ///
    /// The loader is fully backwards-compatible: a parameter without a
    /// <c>scale_type</c> field parses as categorical with no numeric range
    /// and tiers with null <c>NumericBreakpoint</c>, matching the file
    /// format that shipped before this change.
    /// </remarks>
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

            // Default: categorical (matches files authored before #551).
            string scaleType = obj.HasKey("scale_type")
                ? obj.GetString("scale_type", AnatomyParameterDefinition.ScaleTypeCategorical)
                : AnatomyParameterDefinition.ScaleTypeCategorical;

            NumericRangeSpec? numericRange = null;
            if (string.Equals(scaleType, AnatomyParameterDefinition.ScaleTypeNumeric,
                              StringComparison.Ordinal))
            {
                var rangeObj = obj.GetObject("numeric_range");
                if (rangeObj != null)
                {
                    numericRange = new NumericRangeSpec(
                        rangeObj.GetInt("min"),
                        rangeObj.GetInt("max"),
                        rangeObj.GetString("unit"));
                }
            }

            var tiersArr = obj.GetArray("tiers");
            var tiers    = new List<AnatomyTierDefinition>();

            if (tiersArr != null)
            {
                bool isNumeric = string.Equals(
                    scaleType,
                    AnatomyParameterDefinition.ScaleTypeNumeric,
                    StringComparison.Ordinal);
                foreach (var elem in tiersArr.Items)
                {
                    if (!(elem is JsonObject tierObj)) continue;
                    tiers.Add(ParseTier(id, tierObj, isNumeric));
                }
            }

            return new AnatomyParameterDefinition(id, name, tiers, scaleType, numericRange);
        }

        private static AnatomyTierDefinition ParseTier(
            string parameterId,
            JsonObject obj,
            bool parentIsNumeric)
        {
            string tierId   = obj.GetString("id");
            string tierName = obj.GetString("name");

            // Numeric breakpoint: only meaningful when the parent parameter
            // is numeric. We still parse the field if present so a malformed
            // file (e.g. categorical param with stray breakpoints) round-trips
            // safely without surfacing an exception; the value is just dropped
            // for non-numeric parents.
            int? numericBreakpoint = null;
            if (parentIsNumeric && obj.HasKey("numeric_breakpoint"))
            {
                numericBreakpoint = obj.GetInt("numeric_breakpoint");
            }

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
                    visual,
                    numericBreakpoint);
            }

            var statMods  = JsonItemRepository.ParseStatModifiers(obj.GetObject("stat_modifiers"));
            string personality = obj.GetString("personality_fragment");
            string backstory   = obj.GetString("backstory_fragment");
            string texting     = obj.GetString("texting_style_fragment");
            string[] archetypes = JsonItemRepository.ParseStringArray(obj.GetArray("archetype_tendencies"));
            var timing = JsonItemRepository.ParseTimingModifier(obj.GetObject("response_timing_modifier"));

            return new AnatomyTierDefinition(
                parameterId, tierId, tierName,
                statMods, personality, backstory, texting, archetypes, timing,
                visualDescription: null,
                numericBreakpoint: numericBreakpoint);
        }
    }
}

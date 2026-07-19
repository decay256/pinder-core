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
    ///   "metadata": {
    ///     "group": "trunk", "section": "length",
    ///     "label_key": "anatomy.trunk_length_base.label",
    ///     "control_type": "slider",
    ///     "normalized_min": 0, "normalized_max": 1,
    ///     "normalized_default": 0.5, "normalized_step": 0.01,
    ///     "display_order": 10
    ///   },
    ///   "bands": [
    ///     {
    ///       "lower": 0.00, "upper": 0.05,
    ///       "summary_text": "...",        // required display-only summary
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
    /// Parameter metadata and all of its fields are required. All band fields
    /// except <c>lower</c>, <c>upper</c>, and <c>summary_text</c> are optional.
    /// </summary>
    public sealed class JsonAnatomyRepository : IAnatomyRepository
    {
        private readonly Dictionary<string, AnatomyParameterDefinition> _params =
            new Dictionary<string, AnatomyParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AnatomyParameterDefinition> _orderedParams =
            new List<AnatomyParameterDefinition>();

        /// <param name="json">Full JSON string — the contents of anatomy-parameters.json.</param>
        public JsonAnatomyRepository(string json)
        {
            var root = JsonParser.Parse(json);
            if (!(root is JsonArray arr))
                throw new FormatException("Expected top-level JSON array for anatomy parameters.");

            var displayOrders = new HashSet<int>();
            for (int i = 0; i < arr.Items.Count; i++)
            {
                var element = arr.Items[i];
                if (!(element is JsonObject obj))
                {
                    throw new FormatException(
                        $"Invalid anatomy parameter entry at index {i}: " +
                        "expected a JSON object.");
                }

                var param = ParseParameter(obj, i);

                if (_params.ContainsKey(param.Id))
                    throw new FormatException(
                        $"Duplicate anatomy parameter id '{param.Id}' at index {i}.");

                if (!displayOrders.Add(param.Metadata!.DisplayOrder))
                    throw new FormatException(
                        $"Duplicate anatomy metadata.display_order '{param.Metadata.DisplayOrder}' " +
                        $"on anatomy parameter '{param.Id}'.");

                _params.Add(param.Id, param);
                _orderedParams.Add(param);
            }

            _orderedParams.Sort((left, right) =>
                left.Metadata!.DisplayOrder.CompareTo(right.Metadata!.DisplayOrder));
        }

        public AnatomyParameterDefinition? GetParameter(string parameterId)
        {
            _params.TryGetValue(parameterId, out var p);
            return p;
        }

        public IEnumerable<AnatomyParameterDefinition> GetAll() => _orderedParams;

        // -------------------------------------------------------------------

        private static AnatomyParameterDefinition ParseParameter(JsonObject obj, int index)
        {
            string id = obj.GetRequiredString("id", $"anatomy parameter at index {index}");
            string name = obj.GetRequiredString("name", $"anatomy parameter '{id}'");
            if (!obj.Properties.TryGetValue("metadata", out var metadataValue) ||
                !(metadataValue is JsonObject metadataObj))
            {
                throw new FormatException(
                    $"Invalid anatomy parameter '{id}' field 'metadata': " +
                    "a metadata object is required.");
            }

            AnatomyParameterMetadata metadata = ParseMetadata(id, metadataObj);

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

            return new AnatomyParameterDefinition(id, name, bands, metadata);
        }

        private static AnatomyParameterMetadata ParseMetadata(string parameterId, JsonObject obj)
        {
            string context = $"anatomy parameter '{parameterId}' metadata";
            string group = obj.GetRequiredString("group", context);
            string section = obj.GetRequiredString("section", context);
            string labelKey = obj.GetRequiredString("label_key", context);
            string controlType = obj.GetRequiredString("control_type", context);
            float normalizedMin = GetRequiredFloat(obj, "normalized_min", context);
            float normalizedMax = GetRequiredFloat(obj, "normalized_max", context);
            float normalizedDefault = GetRequiredFloat(obj, "normalized_default", context);
            float normalizedStep = GetRequiredFloat(obj, "normalized_step", context);
            int displayOrder = GetRequiredInt(obj, "display_order", context);

            ValidateControlType(parameterId, controlType);
            ValidateNormalizedRange(parameterId, normalizedMin, normalizedMax);
            ValidateNormalizedDefault(parameterId, normalizedDefault, normalizedMin, normalizedMax);
            ValidateNormalizedStep(parameterId, normalizedStep, normalizedMin, normalizedMax);
            ValidateDisplayOrder(parameterId, displayOrder);

            return new AnatomyParameterMetadata(
                group,
                section,
                labelKey,
                controlType,
                normalizedMin,
                normalizedMax,
                normalizedDefault,
                normalizedStep,
                displayOrder);
        }

        private static void ValidateControlType(string parameterId, string controlType)
        {
            if (controlType == "slider" || controlType == "toggle")
                return;

            throw MetadataError(parameterId, "control_type",
                "must be one of: slider, toggle.");
        }

        private static void ValidateNormalizedRange(
            string parameterId,
            float normalizedMin,
            float normalizedMax)
        {
            if (normalizedMin < 0f || normalizedMin > 1f)
                throw MetadataError(parameterId, "normalized_min",
                    "must be within [0, 1].");
            if (normalizedMax < 0f || normalizedMax > 1f)
                throw MetadataError(parameterId, "normalized_max",
                    "must be within [0, 1].");
            if (normalizedMin >= normalizedMax)
                throw MetadataError(parameterId, "normalized_max",
                    "must be greater than metadata.normalized_min.");
        }

        private static void ValidateNormalizedDefault(
            string parameterId,
            float normalizedDefault,
            float normalizedMin,
            float normalizedMax)
        {
            if (normalizedDefault < normalizedMin || normalizedDefault > normalizedMax)
                throw MetadataError(parameterId, "normalized_default",
                    "must be within metadata.normalized_min and metadata.normalized_max.");
        }

        private static void ValidateNormalizedStep(
            string parameterId,
            float normalizedStep,
            float normalizedMin,
            float normalizedMax)
        {
            if (normalizedStep <= 0f)
                throw MetadataError(parameterId, "normalized_step",
                    "must be greater than 0.");
            if (normalizedStep > normalizedMax - normalizedMin)
                throw MetadataError(parameterId, "normalized_step",
                    "must be less than or equal to the normalized range.");
        }

        private static void ValidateDisplayOrder(string parameterId, int displayOrder)
        {
            if (displayOrder <= 0)
                throw MetadataError(parameterId, "display_order",
                    "must be greater than 0.");
        }

        private static FormatException MetadataError(
            string parameterId,
            string field,
            string message)
            => new FormatException(
                $"Invalid anatomy parameter '{parameterId}' metadata.{field}: {message}");

        private static float GetRequiredFloat(JsonObject obj, string key, string context)
        {
            if (!obj.Properties.TryGetValue(key, out var value) || !(value is JsonNumber number))
                throw new FormatException($"Missing required number field '{key}' in {context}.");

            return number.ToFloat();
        }

        private static int GetRequiredInt(JsonObject obj, string key, string context)
        {
            if (!obj.Properties.TryGetValue(key, out var value) || !(value is JsonNumber number))
                throw new FormatException($"Missing required number field '{key}' in {context}.");

            double rounded = Math.Round(number.Value);
            if (Math.Abs(number.Value - rounded) > 0.000001d)
                throw new FormatException($"Required integer field '{key}' in {context} must not be fractional.");

            return (int)rounded;
        }

        private static AnatomyBandDefinition ParseBand(JsonObject obj)
        {
            float lower = obj.GetFloat("lower", 0f);
            float upper = obj.GetFloat("upper", 1f);
            string summaryText = obj.GetRequiredString(
                "summary_text",
                $"anatomy band {lower:0.###}-{upper:0.###}");

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
                archetypes, timing, statMods,
                summaryText);
        }

        private static string? NullIfEmpty(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}

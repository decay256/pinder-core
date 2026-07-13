using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters
{
    internal static class DialogueOptionsStructuredContract
    {
        public const string SchemaName = "dialogue_options";
        public const string SchemaVersion = "dialogue_options.v1";

        private const int MinPlayableOptionLength = 4;

        public static StructuredLlmRequest CreateRequest(
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxTokens,
            DialogueContext context,
            int expectedCount)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "phase", LlmPhase.DialogueOptions },
                { "expected_count", expectedCount.ToString() },
                { "turn", context.CurrentTurn.ToString() },
                { "available_stats", string.Join(",", Array.ConvertAll(context.AvailableStats ?? Array.Empty<StatType>(), StatNameNormalizer.ToWireToken)) }
            };

            return new StructuredLlmRequest(
                schemaName: SchemaName,
                schemaVersion: SchemaVersion,
                jsonSchema: BuildJsonSchema(expectedCount, context.AvailableStats),
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                temperature: temperature,
                maxTokens: maxTokens,
                phase: LlmPhase.DialogueOptions,
                metadata: metadata);
        }

        public static DialogueOption[] ParseStrict(
            string? jsonText,
            StatType[]? availableStats,
            int maxDialogueOptions,
            out string? errorCode,
            out string? errorMessage,
            out int parsedCount,
            out int expectedCount)
        {
            errorCode = null;
            errorMessage = null;
            parsedCount = 0;
            expectedCount = availableStats != null ? Math.Min(availableStats.Length, maxDialogueOptions) : maxDialogueOptions;

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                errorCode = "empty_output";
                errorMessage = "LLM dialogue_options structured JSON output is empty or whitespace.";
                return Array.Empty<DialogueOption>();
            }

            JObject root;
            try
            {
                root = JObject.Parse(jsonText);
            }
            catch (JsonException ex)
            {
                errorCode = "invalid_json";
                errorMessage = "LLM dialogue_options structured output is not a strict JSON object: " + ex.Message;
                return Array.Empty<DialogueOption>();
            }

            if (!HasOnlyProperties(root, out var unexpectedRootProperty, "schema_version", "options"))
            {
                errorCode = "unexpected_property";
                errorMessage = $"LLM dialogue_options structured output contains unexpected property '{unexpectedRootProperty}'.";
                return Array.Empty<DialogueOption>();
            }

            string? schemaVersion = root.Value<string>("schema_version");
            if (!string.Equals(schemaVersion, SchemaVersion, StringComparison.Ordinal))
            {
                errorCode = "invalid_schema_version";
                errorMessage = $"LLM dialogue_options structured output must declare schema_version '{SchemaVersion}'.";
                return Array.Empty<DialogueOption>();
            }

            var optionsArray = root["options"] as JArray;
            if (optionsArray == null)
            {
                errorCode = "missing_options";
                errorMessage = "LLM dialogue_options structured output is missing required array property 'options'.";
                return Array.Empty<DialogueOption>();
            }

            parsedCount = optionsArray.Count;
            if (optionsArray.Count != expectedCount)
            {
                errorCode = "option_count_mismatch";
                errorMessage = $"LLM dialogue_options structured output has {optionsArray.Count} options; expected {expectedCount}.";
                return Array.Empty<DialogueOption>();
            }

            var allowedStats = availableStats != null ? new HashSet<StatType>(availableStats) : null;
            var usedStats = new HashSet<StatType>();
            var parsed = new List<DialogueOption>(optionsArray.Count);

            foreach (var token in optionsArray)
            {
                if (!(token is JObject optionObj))
                {
                    errorCode = "invalid_option_shape";
                    errorMessage = "Every dialogue option must be a JSON object.";
                    return Array.Empty<DialogueOption>();
                }

                if (!HasOnlyProperties(optionObj, out var unexpectedOptionProperty, "stat", "text", "callback", "combo"))
                {
                    errorCode = "unexpected_property";
                    errorMessage = $"LLM dialogue_options structured output contains unexpected option property '{unexpectedOptionProperty}'.";
                    return Array.Empty<DialogueOption>();
                }

                if (!TryReadRequiredString(optionObj, "stat", out var rawStat))
                {
                    errorCode = "missing_stat";
                    errorMessage = "Every dialogue option must include a non-empty string stat.";
                    return Array.Empty<DialogueOption>();
                }

                var normalized = StatNameNormalizer.NormalizeStatName(rawStat);
                if (!Enum.TryParse(normalized, ignoreCase: true, out StatType stat))
                {
                    errorCode = "invalid_stat";
                    errorMessage = $"Dialogue option names invalid stat '{rawStat}'.";
                    return Array.Empty<DialogueOption>();
                }

                if (allowedStats != null && !allowedStats.Contains(stat))
                {
                    errorCode = "stat_not_available";
                    errorMessage = $"Dialogue option stat '{rawStat}' is not available this turn.";
                    return Array.Empty<DialogueOption>();
                }

                if (!usedStats.Add(stat))
                {
                    errorCode = "duplicate_stat";
                    errorMessage = $"Dialogue option stat '{rawStat}' appears more than once.";
                    return Array.Empty<DialogueOption>();
                }

                if (!TryReadRequiredString(optionObj, "text", out var text))
                {
                    errorCode = "missing_text";
                    errorMessage = "Every dialogue option must include non-empty string text.";
                    return Array.Empty<DialogueOption>();
                }

                text = MetaPrefixStripper.Strip(text.Trim());
                if (text.Length < MinPlayableOptionLength)
                {
                    errorCode = "option_text_too_short";
                    errorMessage = "Dialogue option text is too short to be playable.";
                    return Array.Empty<DialogueOption>();
                }

                if (!TryParseCallback(optionObj, out var callbackTurn))
                {
                    errorCode = "invalid_callback";
                    errorMessage = "Dialogue option callback must be null, none, turn_N, or an integer.";
                    return Array.Empty<DialogueOption>();
                }

                if (!TryParseOptionalString(optionObj, "combo", out var comboName))
                {
                    errorCode = "invalid_combo";
                    errorMessage = "Dialogue option combo must be null or a string.";
                    return Array.Empty<DialogueOption>();
                }

                parsed.Add(new DialogueOption(stat, text, callbackTurn, comboName));
            }

            return parsed.ToArray();
        }

        private static string BuildJsonSchema(int expectedCount, StatType[]? availableStats)
        {
            var statNames = new JArray();
            foreach (var stat in availableStats ?? (StatType[])Enum.GetValues(typeof(StatType)))
            {
                statNames.Add(StatNameNormalizer.ToWireToken(stat));
            }

            var schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray("schema_version", "options"),
                ["properties"] = new JObject
                {
                    ["schema_version"] = new JObject
                    {
                        ["type"] = "string",
                        ["const"] = SchemaVersion
                    },
                    ["options"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = expectedCount,
                        ["maxItems"] = expectedCount,
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["required"] = new JArray("stat", "text", "callback", "combo"),
                            ["properties"] = new JObject
                            {
                                ["stat"] = new JObject { ["type"] = "string", ["enum"] = statNames },
                                ["text"] = new JObject { ["type"] = "string", ["minLength"] = MinPlayableOptionLength },
                                ["callback"] = new JObject { ["type"] = new JArray("string", "integer", "null") },
                                ["combo"] = new JObject { ["type"] = new JArray("string", "null") }
                            }
                        }
                    }
                }
            };

            return schema.ToString(Formatting.None);
        }

        private static bool HasOnlyProperties(JObject obj, out string? unexpectedProperty, params string[] allowedProperties)
        {
            var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
            foreach (var property in obj.Properties())
            {
                if (!allowed.Contains(property.Name))
                {
                    unexpectedProperty = property.Name;
                    return false;
                }
            }

            unexpectedProperty = null;
            return true;
        }

        private static bool TryReadRequiredString(JObject obj, string propertyName, out string value)
        {
            value = string.Empty;
            if (!obj.TryGetValue(propertyName, out var token) || token.Type != JTokenType.String)
                return false;

            value = token.Value<string>()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryParseOptionalString(JObject obj, string propertyName, out string? value)
        {
            value = null;
            if (!obj.TryGetValue(propertyName, out var token) || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.String)
                return false;

            var raw = token.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(raw) ||
                string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            value = raw;
            return true;
        }

        private static bool TryParseCallback(JObject obj, out int? callbackTurn)
        {
            callbackTurn = null;
            if (!obj.TryGetValue("callback", out var token) || token.Type == JTokenType.Null)
                return true;

            if (token.Type == JTokenType.Integer)
            {
                callbackTurn = token.Value<int>();
                return true;
            }

            if (token.Type != JTokenType.String)
                return false;

            var raw = token.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(raw) ||
                string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (int.TryParse(raw, out int turnNum))
            {
                callbackTurn = turnNum;
                return true;
            }

            if (raw.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(raw.Substring(5), out int turnNum2))
            {
                callbackTurn = turnNum2;
                return true;
            }

            return false;
        }
    }
}

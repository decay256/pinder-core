using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters
{
    internal static class DateePerformanceStructuredContract
    {
        public const string SchemaName = "datee_performance";
        public const string SchemaVersion = "datee_performance.v1";
        public const string ParserName = "DateePerformanceStructuredContract";

        private const string Phase = "datee_response";
        private const string SchemaVersionField = "schema_version";
        private const string MessageField = "message";
        private const string SignalsField = "signals";
        private const string TellField = "tell";
        private const string WeaknessField = "weakness";
        private static readonly string[] RootFields = { SchemaVersionField, MessageField, SignalsField };
        private static readonly string[] SignalsFields = { TellField, WeaknessField };
        private static readonly string[] TellFields = { "stat", "description" };
        private static readonly string[] WeaknessFields = { "defending_stat", "dc_reduction", "description" };
        private static readonly string[] ReservedLegacyMarkers = { "[SIGNALS]", "TELL:", "WEAKNESS:" };

        public static StructuredLlmRequest CreateRequest(
            string systemPrompt,
            string userMessage,
            double temperature,
            int? maxTokens,
            int? currentTurn,
            IReadOnlyDictionary<string, string> metadata)
        {
            var requestMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["phase"] = LlmPhase.OpponentResponse,
                ["schema_name"] = SchemaName,
                ["schema_version"] = SchemaVersion,
                ["allowed_dc_reductions"] = "2,3",
            };
            if (currentTurn.HasValue)
            {
                requestMetadata["turn"] = currentTurn.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            foreach (KeyValuePair<string, string> pair in metadata)
            {
                requestMetadata[pair.Key] = pair.Value;
            }

            return new StructuredLlmRequest(
                schemaName: SchemaName,
                schemaVersion: SchemaVersion,
                jsonSchema: BuildJsonSchema(),
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                temperature: temperature,
                maxTokens: maxTokens,
                phase: LlmPhase.OpponentResponse,
                metadata: requestMetadata);
        }

        public static DateePerformanceStructuredResult ParseStrict(
            string? jsonText,
            int? turnId,
            string? provider,
            string? model)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                throw Contract("empty_output", "LLM datee_response structured JSON output is empty or whitespace.", turnId, provider, model, signalCount: 0);
            }

            JObject root = ParseCompleteObject(jsonText!, turnId, provider, model);

            if (!HasOnlyProperties(root, out string? unexpectedRootProperty, RootFields))
            {
                throw Contract("unexpected_property", "LLM datee_response structured output contains unexpected property '" + unexpectedRootProperty + "'.", turnId, provider, model);
            }

            if (!root.TryGetValue(SchemaVersionField, out JToken? schemaVersionToken)
                || schemaVersionToken.Type != JTokenType.String
                || !string.Equals(schemaVersionToken.Value<string>(), SchemaVersion, StringComparison.Ordinal))
            {
                throw Contract("invalid_schema_version", "LLM datee_response structured output must declare schema_version '" + SchemaVersion + "'.", turnId, provider, model);
            }

            if (!root.TryGetValue(MessageField, out JToken? messageToken) || messageToken.Type != JTokenType.String)
            {
                throw Contract("missing_message", "LLM datee_response structured output is missing required string property 'message'.", turnId, provider, model);
            }

            string message = StripPersonaSelfTags(messageToken.Value<string>());
            if (string.IsNullOrWhiteSpace(message))
            {
                throw Contract("invalid_message", "LLM datee_response structured output message is empty after cleanup.", turnId, provider, model);
            }

            if (ContainsLegacySignalMarker(message))
            {
                throw Contract("legacy_signal_marker", "LLM datee_response message contains reserved legacy signal metadata markers.", turnId, provider, model);
            }

            if (!root.TryGetValue(SignalsField, out JToken? signalsToken) || !(signalsToken is JObject signalsObject))
            {
                throw Contract("missing_signals", "LLM datee_response structured output is missing required object property 'signals'.", turnId, provider, model);
            }

            if (!HasOnlyProperties(signalsObject, out string? unexpectedSignalsProperty, SignalsFields))
            {
                throw Contract("unexpected_property", "LLM datee_response signals object contains unexpected property '" + unexpectedSignalsProperty + "'.", turnId, provider, model);
            }

            if (!signalsObject.TryGetValue(TellField, out JToken? tellToken))
            {
                throw Contract("invalid_tell", "LLM datee_response signals object must include property 'tell' as null or object.", turnId, provider, model);
            }
            if (!signalsObject.TryGetValue(WeaknessField, out JToken? weaknessToken))
            {
                throw Contract("invalid_weakness", "LLM datee_response signals object must include property 'weakness' as null or object.", turnId, provider, model);
            }

            Tell? tell = ParseTell(tellToken, turnId, provider, model);
            WeaknessParseResult weakness = ParseWeakness(weaknessToken, turnId, provider, model);
            int signalCount = (tell == null ? 0 : 1) + (weakness.Window == null ? 0 : 1);
            return new DateePerformanceStructuredResult(
                new DateeResponse(message, tell, weakness.Window),
                weakness.Description,
                signalCount);
        }

        public static IReadOnlyDictionary<string, string> BuildAcceptedJournalMetadata(
            DateePerformanceStructuredResult result,
            StructuredLlmResponse response)
        {
            var metadata = BaseJournalMetadata(response, "accepted", null);
            metadata["tell_present"] = (result.Response.DetectedTell != null).ToString();
            metadata["weakness_present"] = (result.Response.WeaknessWindow != null).ToString();
            if (result.Response.DetectedTell != null)
            {
                metadata["engine_signal_tell_stat"] = StatNameNormalizer.ToWireToken(result.Response.DetectedTell.Stat);
                metadata["engine_signal_tell_description"] = result.Response.DetectedTell.Description;
            }
            if (result.Response.WeaknessWindow != null)
            {
                metadata["engine_signal_weakness_defending_stat"] = StatNameNormalizer.ToWireToken(result.Response.WeaknessWindow.DefendingStat);
                metadata["engine_signal_weakness_dc_reduction"] = result.Response.WeaknessWindow.DcReduction.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(result.WeaknessDescription))
                {
                    metadata["engine_signal_weakness_description"] = result.WeaknessDescription!;
                }
            }
            return metadata;
        }

        public static IReadOnlyDictionary<string, string> BuildRejectedJournalMetadata(
            StructuredLlmResponse? response,
            string reason)
        {
            var metadata = BaseJournalMetadata(response, "rejected", reason);
            metadata["tell_present"] = "unknown";
            metadata["weakness_present"] = "unknown";
            return metadata;
        }

        private static Dictionary<string, string> BaseJournalMetadata(
            StructuredLlmResponse? response,
            string outcome,
            string? reason)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["schema_name"] = SchemaName,
                ["schema_version"] = SchemaVersion,
                ["parser_name"] = ParserName,
                ["validation_outcome"] = outcome,
            };
            if (!string.IsNullOrWhiteSpace(reason))
            {
                metadata["validation_reason"] = reason!;
            }
            if (response != null)
            {
                metadata["validation_mode"] = response.ValidationMode;
                metadata["structured_output_mode"] = response.UsedNativeStructuredOutput ? "native" : "local_validation";
                if (!string.IsNullOrWhiteSpace(response.Provider)) metadata["provider"] = response.Provider!;
                if (!string.IsNullOrWhiteSpace(response.Model)) metadata["model"] = response.Model!;
            }
            return metadata;
        }

        private static JObject ParseCompleteObject(
            string jsonText,
            int? turnId,
            string? provider,
            string? model)
        {
            try
            {
                using (var stringReader = new StringReader(jsonText))
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    JToken token = JToken.ReadFrom(
                        jsonReader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        });

                    while (jsonReader.Read())
                    {
                        throw Contract("invalid_json", "LLM datee_response structured output must be one complete JSON object with no trailing content.", turnId, provider, model);
                    }

                    if (token is JObject obj)
                    {
                        return obj;
                    }

                    throw Contract("invalid_json", "LLM datee_response structured output root must be a JSON object.", turnId, provider, model);
                }
            }
            catch (LlmContractException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw Contract("invalid_json", "LLM datee_response structured output is not strict JSON: " + ex.Message, turnId, provider, model);
            }
        }

        private static Tell? ParseTell(
            JToken token,
            int? turnId,
            string? provider,
            string? model)
        {
            if (token.Type == JTokenType.Null)
            {
                return null;
            }
            if (!(token is JObject obj))
            {
                throw Contract("invalid_tell", "LLM datee_response tell must be null or exactly { stat, description }.", turnId, provider, model);
            }
            if (!HasOnlyProperties(obj, out string? unexpected, TellFields))
            {
                throw Contract("unexpected_property", "LLM datee_response tell contains unexpected property '" + unexpected + "'.", turnId, provider, model);
            }
            if (!TryReadRequiredString(obj, "stat", out string statToken)
                || !TryParseWireStat(statToken, out StatType stat)
                || !TryReadRequiredString(obj, "description", out string description))
            {
                throw Contract("invalid_tell", "LLM datee_response tell must include a valid wire stat and non-empty description.", turnId, provider, model);
            }
            return new Tell(stat, description);
        }

        private static WeaknessParseResult ParseWeakness(
            JToken token,
            int? turnId,
            string? provider,
            string? model)
        {
            if (token.Type == JTokenType.Null)
            {
                return new WeaknessParseResult(null, null);
            }
            if (!(token is JObject obj))
            {
                throw Contract("invalid_weakness", "LLM datee_response weakness must be null or exactly { defending_stat, dc_reduction, description }.", turnId, provider, model);
            }
            if (!HasOnlyProperties(obj, out string? unexpected, WeaknessFields))
            {
                throw Contract("unexpected_property", "LLM datee_response weakness contains unexpected property '" + unexpected + "'.", turnId, provider, model);
            }
            if (!TryReadRequiredString(obj, "defending_stat", out string statToken)
                || !TryParseWireStat(statToken, out StatType stat)
                || !obj.TryGetValue("dc_reduction", out JToken? reductionToken)
                || reductionToken.Type != JTokenType.Integer
                || !TryReadDcReduction(reductionToken, out int reduction)
                || !TryReadRequiredString(obj, "description", out string description))
            {
                throw Contract("invalid_weakness", "LLM datee_response weakness must include valid defending_stat, dc_reduction 2 or 3, and non-empty description.", turnId, provider, model);
            }
            return new WeaknessParseResult(new WeaknessWindow(stat, reduction), description);
        }

        internal static string StripPersonaSelfTags(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            var result = text!;
            result = result.Replace(" /end)", ")").Replace(" /rant)", ")");
            result = result.Replace("/end)", ")").Replace("/rant)", ")");
            result = result.Replace(" /end ", " ").Replace(" /rant ", " ");
            result = StripTrailingTag(result, " /end");
            result = StripTrailingTag(result, " /rant");
            return result.Trim();
        }

        private static string StripTrailingTag(string text, string tag)
        {
            var trimmedEnd = text.TrimEnd();
            if (trimmedEnd.EndsWith(tag, StringComparison.Ordinal))
            {
                return trimmedEnd.Substring(0, trimmedEnd.Length - tag.Length);
            }
            return text;
        }

        private static bool ContainsLegacySignalMarker(string value)
        {
            foreach (string marker in ReservedLegacyMarkers)
            {
                if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadRequiredString(JObject obj, string propertyName, out string value)
        {
            value = string.Empty;
            if (!obj.TryGetValue(propertyName, out JToken? token) || token.Type != JTokenType.String)
            {
                return false;
            }
            value = token.Value<string>()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryReadDcReduction(JToken token, out int reduction)
        {
            reduction = 0;
            try
            {
                reduction = token.Value<int>();
            }
            catch (OverflowException)
            {
                return false;
            }
            return reduction == 2 || reduction == 3;
        }

        private static bool TryParseWireStat(string raw, out StatType stat)
        {
            foreach (StatType candidate in Enum.GetValues(typeof(StatType)))
            {
                if (string.Equals(raw, StatNameNormalizer.ToWireToken(candidate), StringComparison.Ordinal))
                {
                    stat = candidate;
                    return true;
                }
            }
            stat = default;
            return false;
        }

        private static bool HasOnlyProperties(JObject obj, out string? unexpectedProperty, params string[] allowedProperties)
        {
            var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
            foreach (JProperty property in obj.Properties())
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

        private static string BuildJsonSchema()
        {
            var statNames = new JArray();
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                statNames.Add(StatNameNormalizer.ToWireToken(stat));
            }

            var tellSchema = new JObject
            {
                ["type"] = new JArray("object", "null"),
                ["additionalProperties"] = false,
                ["required"] = new JArray("stat", "description"),
                ["properties"] = new JObject
                {
                    ["stat"] = new JObject { ["type"] = "string", ["enum"] = statNames.DeepClone(), ["description"] = "The revealed stat vulnerability." },
                    ["description"] = new JObject { ["type"] = "string", ["minLength"] = 1, ["description"] = "Trusted diagnostics description of what revealed the tell." },
                },
            };
            var weaknessSchema = new JObject
            {
                ["type"] = new JArray("object", "null"),
                ["additionalProperties"] = false,
                ["required"] = new JArray("defending_stat", "dc_reduction", "description"),
                ["properties"] = new JObject
                {
                    ["defending_stat"] = new JObject { ["type"] = "string", ["enum"] = statNames.DeepClone(), ["description"] = "The defending stat whose DC is reduced." },
                    ["dc_reduction"] = new JObject { ["type"] = "integer", ["enum"] = new JArray(2, 3), ["description"] = "Current gameplay reduction value." },
                    ["description"] = new JObject { ["type"] = "string", ["minLength"] = 1, ["description"] = "Trusted diagnostics description of the opening." },
                },
            };

            var schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray(SchemaVersionField, MessageField, SignalsField),
                ["properties"] = new JObject
                {
                    [SchemaVersionField] = new JObject
                    {
                        ["type"] = "string",
                        ["const"] = SchemaVersion,
                        ["description"] = "Contract schema version identifier.",
                    },
                    [MessageField] = new JObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                        ["description"] = "Only the visible in-world DATEE message. Do not include JSON, schema fields, or engine signal markers in this text.",
                    },
                    [SignalsField] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JArray(TellField, WeaknessField),
                        ["properties"] = new JObject
                        {
                            [TellField] = tellSchema,
                            [WeaknessField] = weaknessSchema,
                        },
                    },
                },
            };
            return schema.ToString(Formatting.None);
        }

        private static LlmContractException Contract(
            string reason,
            string message,
            int? turnId,
            string? provider,
            string? model,
            int? signalCount = null)
        {
            return new LlmContractException(
                phase: Phase,
                reason: reason,
                message: message,
                provider: provider,
                model: model,
                parserName: ParserName,
                signalCount: signalCount,
                turnId: turnId);
        }

        private sealed class WeaknessParseResult
        {
            public WeaknessParseResult(WeaknessWindow? window, string? description)
            {
                Window = window;
                Description = description;
            }

            public WeaknessWindow? Window { get; }
            public string? Description { get; }
        }
    }

    internal sealed class DateePerformanceStructuredResult
    {
        public DateePerformanceStructuredResult(DateeResponse response, string? weaknessDescription, int signalCount)
        {
            Response = response ?? throw new ArgumentNullException(nameof(response));
            WeaknessDescription = weaknessDescription;
            SignalCount = signalCount;
        }

        public DateeResponse Response { get; }
        public string? WeaknessDescription { get; }
        public int SignalCount { get; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    internal static class AvatarEmotionalDirectionContract
    {
        public const string SchemaName = "avatar_emotional_direction";
        public const string SchemaVersion = "avatar_emotional_direction.v1";
        public const string ParserName = "AvatarEmotionalDirectionContract";

        public static StructuredLlmRequest CreateRequest(
            string systemPrompt,
            string userMessage,
            double temperature,
            int? maxTokens,
            IReadOnlyDictionary<string, string> metadata,
            IReadOnlyList<string> allowedEmotions)
            => new StructuredLlmRequest(
                SchemaName,
                SchemaVersion,
                BuildJsonSchema(allowedEmotions),
                systemPrompt,
                userMessage,
                temperature,
                maxTokens,
                LlmPhase.AvatarEmotionalDirector,
                metadata);

        public static bool TryParse(
            string? jsonText,
            bool requireCompleteJsonObject,
            IReadOnlyList<string> allowedEmotions,
            out AvatarEmotionalDirection? direction,
            out string errorCode)
        {
            direction = null;
            errorCode = string.Empty;

            string? json = jsonText;
            if (!requireCompleteJsonObject)
            {
                var extraction = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(jsonText);
                if (!extraction.Success)
                {
                    errorCode = "malformed_json";
                    return false;
                }
                json = extraction.Json;
            }

            JObject root;
            try
            {
                using var stringReader = new StringReader(json ?? string.Empty);
                using var jsonReader = new JsonTextReader(stringReader);
                JToken token = JToken.ReadFrom(
                    jsonReader,
                    new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (!(token is JObject obj))
                {
                    errorCode = "root_nonobject";
                    return false;
                }
                root = obj;
            }
            catch (JsonException)
            {
                errorCode = "malformed_json";
                return false;
            }

            string[] expected = { "schema_version", "primary_emotion", "response_posture" };
            if (root.Properties().Any(property => !expected.Contains(property.Name, StringComparer.Ordinal))
                || root.Properties().Count() != expected.Length)
            {
                errorCode = "unexpected_field";
                return false;
            }

            if (!string.Equals(root.Value<string>("schema_version"), SchemaVersion, StringComparison.Ordinal))
            {
                errorCode = "invalid_schema_version";
                return false;
            }

            string requestedEmotion = root.Value<string>("primary_emotion")?.Trim() ?? string.Empty;
            string? canonicalEmotion = allowedEmotions.FirstOrDefault(
                emotion => string.Equals(emotion, requestedEmotion, StringComparison.OrdinalIgnoreCase));
            if (canonicalEmotion == null)
            {
                errorCode = "unsupported_primary_emotion";
                return false;
            }

            string posture = root.Value<string>("response_posture")?.Trim() ?? string.Empty;
            if (posture.Length < 12)
            {
                errorCode = "response_posture_too_short";
                return false;
            }
            if (posture.IndexOf(canonicalEmotion, StringComparison.OrdinalIgnoreCase) < 0)
            {
                errorCode = "response_posture_omits_primary_emotion";
                return false;
            }

            direction = new AvatarEmotionalDirection(canonicalEmotion, posture);
            return true;
        }

        private static string BuildJsonSchema(IReadOnlyList<string> allowedEmotions)
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray("schema_version", "primary_emotion", "response_posture"),
                ["properties"] = new JObject
                {
                    ["schema_version"] = new JObject
                    {
                        ["type"] = "string",
                        ["const"] = SchemaVersion,
                    },
                    ["primary_emotion"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray(allowedEmotions),
                    },
                    ["response_posture"] = new JObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 12,
                    },
                },
            };
            return schema.ToString(Formatting.None);
        }
    }
}

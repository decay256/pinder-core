using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    internal sealed class EmotionalDirectorDirection
    {
        public EmotionalDirectorDirection(
            string primaryEmotion,
            string intensity,
            string underlyingFeeling,
            string interpretation,
            string impulse,
            string restraint,
            string responsePosture)
        {
            PrimaryEmotion = primaryEmotion;
            Intensity = intensity;
            UnderlyingFeeling = underlyingFeeling;
            Interpretation = interpretation;
            Impulse = impulse;
            Restraint = restraint;
            ResponsePosture = responsePosture;
        }

        public string PrimaryEmotion { get; }
        public string Intensity { get; }
        public string UnderlyingFeeling { get; }
        public string Interpretation { get; }
        public string Impulse { get; }
        public string Restraint { get; }
        public string ResponsePosture { get; }
    }

    internal static class EmotionalDirectorContract
    {
        public const string SchemaName = "emotional_director";
        public const string SchemaVersion = "emotional_director.v1";
        public const string ParserName = "EmotionalDirectorContract";

        private const int MinFieldChars = 3;
        private const int MaxFieldChars = 180;

        private static readonly string[] Fields =
        {
            "primary_emotion",
            "intensity",
            "underlying_feeling",
            "interpretation",
            "impulse",
            "restraint",
            "response_posture",
        };

        private static readonly HashSet<string> MechanicalShorthandValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "charm",
                "rizz",
                "honesty",
                "chaos",
                "wit",
                "self-awareness",
                "self_awareness",
                "clean",
                "strong",
                "critical",
                "exceptional",
                "nat20",
                "fumble",
                "misfire",
                "trope-trap",
                "trope_trap",
                "catastrophe",
                "nat1",
                "hostile",
                "cold",
                "guarded",
                "lukewarm",
                "interested",
                "warm",
                "invested",
            };

        private static readonly HashSet<string> MechanicalStatValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "charm",
                "rizz",
                "honesty",
                "chaos",
                "wit",
                "self-awareness",
                "self_awareness",
            };

        private static readonly HashSet<string> MechanicalTierValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "clean",
                "strong",
                "critical",
                "exceptional",
                "nat20",
                "fumble",
                "misfire",
                "trope-trap",
                "trope_trap",
                "catastrophe",
                "nat1",
            };

        private static readonly Regex StatTypeReferenceRegex =
            new Regex(@"\bStatType\s*[.:]\s*[A-Za-z_][A-Za-z0-9_]*\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RollOrDiceMechanicsRegex =
            new Regex(@"\b(?:dice|d20)\b|\b(?:the|this|that)\s+roll\b|\broll(?:ed|s|ing)?\s+(?:poorly|well|high|low|\d+|result|check|outcome|tier)\b|\bdie\s+(?:roll|result|check)\b|\bDC\s*[:=]?\s*\d+\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InterestArithmeticRegex =
            new Regex(@"\binterest(?:\s+(?:score|state|tier))?\s*(?:[:=+\-]\s*\d+|\d+\s*(?:->|=>|[+\-])\s*\d+|from\s+\d+\s+to\s+\d+)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ExplicitMechanicsLanguageRegex =
            new Regex(@"\b(?:game mechanics?|diagnosis)\b|\b(?:modifier|tier|outcome)\s*:",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SymbolicOnlyRegex =
            new Regex(@"^[\W_0-9]+$", RegexOptions.Compiled);

        private static readonly Regex FirstPersonMarkerRegex =
            new Regex(@"(?<![\p{L}\p{N}_])(?:i|i'm|i've|i'll|i'd|me|my|mine|myself|we|we're|we've|we'll|we'd|us|our|ours|ourselves)(?![\p{L}\p{N}_])",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static StructuredLlmRequest CreateRequest(
            string systemPrompt,
            string userMessage,
            double temperature,
            int maxTokens,
            IReadOnlyDictionary<string, string> metadata)
        {
            return new StructuredLlmRequest(
                schemaName: SchemaName,
                schemaVersion: SchemaVersion,
                jsonSchema: BuildJsonSchema(),
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                temperature: temperature,
                maxTokens: maxTokens,
                phase: LlmPhase.EmotionalDirector,
                metadata: metadata);
        }

        public static bool TryParse(
            string? jsonText,
            bool requireCompleteJsonObject,
            out EmotionalDirectorDirection? direction,
            out string errorCode)
        {
            direction = null;
            errorCode = string.Empty;

            if (jsonText != null
                && jsonText.Length > GeneratedJsonObjectExtractionOptions.DefaultMaxInputChars)
            {
                errorCode = "output_too_large";
                return false;
            }

            string? json = jsonText;
            if (!requireCompleteJsonObject)
            {
                var extraction = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(jsonText);
                if (!extraction.Success)
                {
                    errorCode = MapExtractionFailure(jsonText, extraction.FailureCode);
                    return false;
                }

                json = extraction.Json;
            }
            else if (string.IsNullOrWhiteSpace(json))
            {
                errorCode = "empty_output";
                return false;
            }

            JObject root;
            try
            {
                JToken token;
                using (var stringReader = new StringReader(json!))
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    token = JToken.ReadFrom(
                        jsonReader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        });
                }

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

            if (!HasOnlyProperties(root, out var unexpected, Fields))
            {
                errorCode = "unexpected_field";
                return false;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in Fields)
            {
                if (!root.TryGetValue(field, out var token) || token.Type != JTokenType.String)
                {
                    errorCode = "missing_field";
                    return false;
                }

                string value = token.Value<string>()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errorCode = "blank_field";
                    return false;
                }

                string? semanticReason = ValidateFieldValue(value);
                if (semanticReason != null)
                {
                    errorCode = semanticReason;
                    return false;
                }

                values[field] = value;
            }

            direction = new EmotionalDirectorDirection(
                values["primary_emotion"],
                values["intensity"],
                values["underlying_feeling"],
                values["interpretation"],
                values["impulse"],
                values["restraint"],
                values["response_posture"]);
            return true;
        }

        private static string? ValidateFieldValue(string value)
        {
            string normalized = value.Trim();
            if (SymbolicOnlyRegex.IsMatch(normalized))
                return "symbolic_only";

            if (value.Length < MinFieldChars)
                return "field_too_short";

            if (value.Length > MaxFieldChars)
                return "field_too_long";

            if (ContainsMetaLanguage(normalized))
                return "meta_language";

            if (ContainsRawMechanics(normalized))
                return "raw_mechanics";

            if (ContainsDraftedReply(normalized))
                return "drafted_chat_reply";

            return null;
        }

        private static bool ContainsRawMechanics(string value)
        {
            string shorthand = value.Trim().TrimEnd('.', '!', '?');
            if (MechanicalShorthandValues.Contains(shorthand) || IsStatTierShorthand(shorthand))
                return true;

            return StatTypeReferenceRegex.IsMatch(value)
                || RollOrDiceMechanicsRegex.IsMatch(value)
                || InterestArithmeticRegex.IsMatch(value)
                || ExplicitMechanicsLanguageRegex.IsMatch(value);
        }

        private static bool IsStatTierShorthand(string value)
        {
            string[] tokens = value.Split(
                new[] { ' ', ':', '/', '|', '=' },
                StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length == 2
                && MechanicalStatValues.Contains(tokens[0])
                && MechanicalTierValues.Contains(tokens[1]);
        }

        private static bool ContainsDraftedReply(string value)
        {
            return FirstPersonMarkerRegex.IsMatch(value)
                || value.IndexOf('"') >= 0;
        }

        private static bool ContainsMetaLanguage(string value)
        {
            string lower = value.ToLowerInvariant();
            return lower.Contains("analysis:")
                || lower.Contains("meta analysis")
                || lower.Contains("prompt")
                || lower.Contains("director")
                || lower.Contains("instruction")
                || lower.Contains("schema")
                || lower.Contains("json")
                || lower.Contains("as an ai");
        }

        private static string MapExtractionFailure(
            string? text,
            GeneratedJsonObjectExtractionFailureCode failureCode)
        {
            switch (failureCode)
            {
                case GeneratedJsonObjectExtractionFailureCode.EmptyInput:
                    return "empty_output";
                case GeneratedJsonObjectExtractionFailureCode.NoObject:
                    return "no_json_object";
                case GeneratedJsonObjectExtractionFailureCode.UnterminatedObject:
                case GeneratedJsonObjectExtractionFailureCode.NoValidObject:
                    return StartsWithArray(text) ? "root_nonobject" : "malformed_json";
                case GeneratedJsonObjectExtractionFailureCode.InputTooLarge:
                case GeneratedJsonObjectExtractionFailureCode.ObjectTooLarge:
                    return "malformed_json";
                default:
                    return "malformed_json";
            }
        }

        private static bool StartsWithArray(string? text)
        {
            return !string.IsNullOrWhiteSpace(text) && text!.TrimStart().StartsWith("[", StringComparison.Ordinal);
        }

        private static bool HasOnlyProperties(
            JObject obj,
            out string? unexpectedProperty,
            params string[] allowedProperties)
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

        private static string BuildJsonSchema()
        {
            var properties = new JObject();
            foreach (string field in Fields)
            {
                properties[field] = new JObject
                {
                    ["type"] = "string",
                    ["minLength"] = MinFieldChars,
                    ["maxLength"] = MaxFieldChars,
                };
            }

            var schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray(Fields),
                ["properties"] = properties,
            };

            return schema.ToString(Formatting.None);
        }
    }
}

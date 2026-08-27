using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    internal static class CharacterEmotionalDirectionContract
    {
        public const string SchemaName = "emotional_director";
        public const string SchemaVersion = "emotional_director.v2";
        public const string ParserName = "CharacterEmotionalDirectionContract";

        private const int MinFieldChars = 3;
        private const string SchemaVersionField = "schema_version";

        private static readonly string[] StringFields =
        {
            "primary_emotion",
            "secondary_emotion",
            "regulatory_state",
            "trajectory",
            "core_threat_or_desire",
            "interpretation",
            "impulse",
            "restraint",
            "response_posture",
        };

        private static readonly string[] RequiredRootFields =
        {
            SchemaVersionField,
            "primary_emotion",
            "secondary_emotion",
            "regulatory_state",
            "activation",
            "trajectory",
            "core_threat_or_desire",
            "interpretation",
            "impulse",
            "restraint",
            "response_posture",
        };

        private static readonly HashSet<string> ProseFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "core_threat_or_desire",
                "interpretation",
                "impulse",
                "restraint",
                "response_posture",
            };

        private static readonly HashSet<string> RegulatoryStates =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "open",
                "controlled",
                "anxious",
                "guarded",
                "overwhelmed",
                "conflicted",
                "numb",
                "dissociated",
            };

        private static readonly HashSet<string> Trajectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "escalating",
                "easing",
                "volatile",
                "reversing",
                "steady",
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
            int? maxTokens,
            IReadOnlyDictionary<string, string> metadata,
            string phase,
            IReadOnlyList<string> allowedEmotions)
        {
            return new StructuredLlmRequest(
                schemaName: SchemaName,
                schemaVersion: SchemaVersion,
                jsonSchema: BuildJsonSchema(allowedEmotions),
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                temperature: temperature,
                maxTokens: maxTokens,
                phase: phase,
                metadata: metadata);
        }

        public static bool TryParse(
            string? jsonText,
            bool requireCompleteJsonObject,
            IReadOnlyList<string> allowedEmotions,
            out CharacterEmotionalDirection? direction,
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

            if (!root.TryGetValue(SchemaVersionField, out var schemaVersionToken)
                || schemaVersionToken.Type != JTokenType.String
                || !string.Equals(schemaVersionToken.Value<string>(), SchemaVersion, StringComparison.Ordinal))
            {
                errorCode = "invalid_schema_version";
                return false;
            }

            if (!HasOnlyProperties(root, out var unexpected, RequiredRootFields))
            {
                errorCode = "unexpected_field";
                return false;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in StringFields)
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

                if (ProseFields.Contains(field))
                {
                    string? semanticReason = ValidateFieldValue(value);
                    if (semanticReason != null)
                    {
                        errorCode = semanticReason;
                        return false;
                    }
                }

                values[field] = value;
            }

            if (!root.TryGetValue("activation", out var activationToken)
                || activationToken.Type != JTokenType.Integer)
            {
                errorCode = "invalid_activation";
                return false;
            }

            int activation = activationToken.Value<int>();
            if (activation < 1 || activation > 5)
            {
                errorCode = "invalid_activation";
                return false;
            }

            string? primaryEmotion = CanonicalEmotion(values["primary_emotion"], allowedEmotions);
            if (primaryEmotion == null)
            {
                errorCode = "unsupported_primary_emotion";
                return false;
            }

            string secondary = values["secondary_emotion"];
            bool hasSecondary = !string.Equals(
                secondary,
                CharacterEmotionalDirection.NoneSecondaryEmotion,
                StringComparison.OrdinalIgnoreCase);
            string secondaryEmotion = CharacterEmotionalDirection.NoneSecondaryEmotion;
            if (hasSecondary)
            {
                secondaryEmotion = CanonicalEmotion(secondary, allowedEmotions) ?? string.Empty;
                if (secondaryEmotion.Length == 0)
                {
                    errorCode = "unsupported_secondary_emotion";
                    return false;
                }

                if (string.Equals(primaryEmotion, secondaryEmotion, StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "duplicate_primary_secondary_emotion";
                    return false;
                }
            }

            string regulatoryState = Canonical(values["regulatory_state"], RegulatoryStates);
            if (regulatoryState.Length == 0)
            {
                errorCode = "unsupported_regulatory_state";
                return false;
            }

            if (string.Equals(regulatoryState, "conflicted", StringComparison.Ordinal)
                && !hasSecondary)
            {
                errorCode = "conflicted_requires_secondary_emotion";
                return false;
            }

            string trajectory = Canonical(values["trajectory"], Trajectories);
            if (trajectory.Length == 0)
            {
                errorCode = "unsupported_trajectory";
                return false;
            }

            direction = new CharacterEmotionalDirection(
                primaryEmotion,
                secondaryEmotion,
                regulatoryState,
                activation,
                trajectory,
                values["core_threat_or_desire"],
                values["interpretation"],
                values["impulse"],
                values["restraint"],
                values["response_posture"]);
            return true;
        }

        private static string? CanonicalEmotion(string value, IReadOnlyList<string> allowedEmotions)
        {
            return allowedEmotions.FirstOrDefault(
                emotion => string.Equals(emotion, value, StringComparison.OrdinalIgnoreCase));
        }

        private static string Canonical(string value, HashSet<string> allowedValues)
        {
            return allowedValues.FirstOrDefault(
                candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        private static string? ValidateFieldValue(string value)
        {
            string normalized = value.Trim();
            if (SymbolicOnlyRegex.IsMatch(normalized))
                return "symbolic_only";

            if (value.Length < MinFieldChars)
                return "field_too_short";

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

        private static string BuildJsonSchema(IReadOnlyList<string> allowedEmotions)
        {
            var properties = new JObject
            {
                [SchemaVersionField] = new JObject
                {
                    ["type"] = "string",
                    ["const"] = SchemaVersion,
                    ["description"] = "The contract schema version string. Must be exactly 'emotional_director.v2'.",
                },
                ["primary_emotion"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(allowedEmotions),
                    ["description"] = "The single dominant concrete felt emotion chosen from the configured vocabulary.",
                },
                ["secondary_emotion"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(allowedEmotions.Concat(new[] { CharacterEmotionalDirection.NoneSecondaryEmotion })),
                    ["description"] = "A distinct configured concrete emotion, or literal 'none'.",
                },
                ["regulatory_state"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(RegulatoryStates),
                    ["description"] = "The character's regulatory state.",
                },
                ["activation"] = new JObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = 5,
                    ["description"] = "Emotional activation from 1 through 5.",
                },
                ["trajectory"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(Trajectories),
                    ["description"] = "The movement of the emotional beat.",
                },
                ["core_threat_or_desire"] = StringProperty("Concise vulnerable threat or desire driving the reaction."),
                ["interpretation"] = StringProperty("How the latest visible message lands for this character."),
                ["impulse"] = StringProperty("Immediate behavioral urge, never drafted dialogue."),
                ["restraint"] = StringProperty("What prevents full expression."),
                ["response_posture"] = StringProperty("Actionable performance direction, never drafted dialogue."),
            };

            var schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray(RequiredRootFields),
                ["properties"] = properties,
            };

            return schema.ToString(Formatting.None);
        }

        private static JObject StringProperty(string description)
        {
            return new JObject
            {
                ["type"] = "string",
                ["minLength"] = MinFieldChars,
                ["description"] = description,
            };
        }
    }
}

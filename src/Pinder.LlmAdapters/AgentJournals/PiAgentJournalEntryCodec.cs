using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using Pi.Agent.Core;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.LlmAdapters.AgentJournals
{
    public sealed class PiAgentJournalDecodeResult
    {
        private PiAgentJournalDecodeResult(
            object? record,
            AgentJournalCompatibilityResult compatibility)
        {
            Record = record;
            Compatibility = compatibility;
        }

        public object? Record { get; }
        public AgentJournalCompatibilityResult Compatibility { get; }

        public static PiAgentJournalDecodeResult Known(object record, string customType)
            => new PiAgentJournalDecodeResult(
                record,
                new AgentJournalCompatibilityResult(AgentJournalCompatibilityKind.Known, customType, null, null));

        public static PiAgentJournalDecodeResult Compatible(AgentJournalCompatibilityResult compatibility)
            => new PiAgentJournalDecodeResult(null, compatibility);

        public static PiAgentJournalDecodeResult Invalid(
            string customType,
            string warning,
            string opaqueJson,
            IReadOnlyList<AgentJournalValidationError> errors)
            => Compatible(new AgentJournalCompatibilityResult(
                AgentJournalCompatibilityKind.Invalid,
                customType,
                warning,
                opaqueJson,
                errors));
    }

    public sealed class PiAgentJournalEntryCodec
    {
        public const int MaxOpaqueJsonBytes = 65536;

        public CustomEntry Encode(LlmInvocationRecord record)
        {
            ThrowIfInvalid(AgentJournalValidator.Validate(record));
            return new CustomEntry(null, null, null, AgentJournalSchemaNames.LlmInvocationV1, ToJsonObject(record));
        }

        public CustomEntry Encode(LlmResultRecord record)
        {
            ThrowIfInvalid(AgentJournalValidator.Validate(record));
            return new CustomEntry(null, null, null, AgentJournalSchemaNames.LlmResultV1, ToJsonObject(record));
        }

        public CustomEntry Encode(MessageLinkRecord record)
        {
            ThrowIfInvalid(AgentJournalValidator.Validate(record));
            return new CustomEntry(null, null, null, AgentJournalSchemaNames.MessageLinkV1, ToJsonObject(record));
        }

        public CustomEntry Encode(AgentJournalRoleFactPolicyDecisionRecord record)
        {
            ThrowIfInvalid(AgentJournalValidator.Validate(record));
            return new CustomEntry(null, null, null, AgentJournalSchemaNames.RoleFactPolicyDecisionV1, ToJsonObject(record));
        }

        public PiAgentJournalDecodeResult Decode(CustomEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            string json = NormalizeData(entry.Data);
            string customType = entry.CustomType ?? string.Empty;
            try
            {
                switch (customType)
                {
                    case AgentJournalSchemaNames.LlmInvocationV1:
                        return DecodeKnown(AgentJournalJson.Deserialize<LlmInvocationRecord>(json), customType, json);
                    case AgentJournalSchemaNames.LlmResultV1:
                        return DecodeKnown(AgentJournalJson.Deserialize<LlmResultRecord>(json), customType, json);
                    case AgentJournalSchemaNames.MessageLinkV1:
                        return DecodeKnown(AgentJournalJson.Deserialize<MessageLinkRecord>(json), customType, json);
                    case AgentJournalSchemaNames.RoleFactPolicyDecisionV1:
                        return DecodeKnown(AgentJournalJson.Deserialize<AgentJournalRoleFactPolicyDecisionRecord>(json), customType, json);
                    default:
                        return PiAgentJournalDecodeResult.Compatible(DecodeUnknown(customType, json));
                }
            }
            catch (JsonException)
            {
                return InvalidJson(customType, json);
            }
            catch (NotSupportedException)
            {
                return InvalidJson(customType, json);
            }
            catch (ArgumentException)
            {
                return InvalidJson(customType, json);
            }
        }

        private static PiAgentJournalDecodeResult DecodeKnown(LlmInvocationRecord? record, string customType, string json)
            => record == null
                ? InvalidNullRecord(customType, json)
                : DecodeValidated(record, customType, json, AgentJournalValidator.Validate(record));

        private static PiAgentJournalDecodeResult DecodeKnown(LlmResultRecord? record, string customType, string json)
            => record == null
                ? InvalidNullRecord(customType, json)
                : DecodeValidated(record, customType, json, AgentJournalValidator.Validate(record));

        private static PiAgentJournalDecodeResult DecodeKnown(MessageLinkRecord? record, string customType, string json)
            => record == null
                ? InvalidNullRecord(customType, json)
                : DecodeValidated(record, customType, json, AgentJournalValidator.Validate(record));

        private static PiAgentJournalDecodeResult DecodeKnown(AgentJournalRoleFactPolicyDecisionRecord? record, string customType, string json)
            => record == null
                ? InvalidNullRecord(customType, json)
                : DecodeValidated(record, customType, json, AgentJournalValidator.Validate(record));

        private static PiAgentJournalDecodeResult DecodeValidated(
            object record,
            string customType,
            string json,
            AgentJournalValidationResult validation)
            => validation.IsValid
                ? PiAgentJournalDecodeResult.Known(record, customType)
                : InvalidRecord(customType, json, validation);

        private static PiAgentJournalDecodeResult InvalidNullRecord(string customType, string json)
        {
            var validation = AgentJournalValidationResult.From(
                new[] { new AgentJournalValidationError("invalid_json", "$") });
            return InvalidRecord(customType, json, validation);
        }

        private static PiAgentJournalDecodeResult InvalidRecord(
            string customType,
            string json,
            AgentJournalValidationResult validation)
        {
            string warning = "Invalid known Pinder custom-entry payload: "
                + string.Join(", ", ErrorCodes(validation))
                + ".";
            return PiAgentJournalDecodeResult.Invalid(
                customType,
                warning,
                BoundOpaqueJson(json),
                validation.Errors);
        }

        private static PiAgentJournalDecodeResult InvalidJson(string customType, string json)
        {
            var errors = new[] { new AgentJournalValidationError("invalid_json", "$") };
            return PiAgentJournalDecodeResult.Invalid(
                customType,
                "Invalid known Pinder custom-entry payload: invalid_json@$.",
                BoundOpaqueJson(json),
                errors);
        }

        private static AgentJournalCompatibilityResult DecodeUnknown(string customType, string json)
        {
            if (!customType.StartsWith("pinder.", StringComparison.Ordinal))
            {
                return new AgentJournalCompatibilityResult(
                    AgentJournalCompatibilityKind.NonPinderCustomEntry,
                    customType,
                    null,
                    null);
            }

            return new AgentJournalCompatibilityResult(
                AgentJournalCompatibilityKind.UnknownPinderVersion,
                customType,
                "Unknown Pinder custom-entry version preserved as bounded opaque JSON.",
                BoundOpaqueJson(json));
        }

        private static string BoundOpaqueJson(string json)
        {
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
            if (byteCount <= MaxOpaqueJsonBytes)
            {
                return json;
            }
            return json.Substring(0, Math.Min(json.Length, MaxOpaqueJsonBytes / 4));
        }

        private static JsonNode ToJsonObject<T>(T record)
            => JsonNode.Parse(AgentJournalJson.Serialize(record))!;

        private static string NormalizeData(object data)
        {
            if (data is JObject jObject)
            {
                return jObject.ToString(Newtonsoft.Json.Formatting.None);
            }
            if (data is JToken token)
            {
                return token.ToString(Newtonsoft.Json.Formatting.None);
            }
            if (data is JsonNode node)
            {
                return node.ToJsonString();
            }
            if (data is JsonElement element)
            {
                return element.GetRawText();
            }
            if (data is string text)
            {
                return text;
            }
            return AgentJournalJson.Serialize(data);
        }

        private static void ThrowIfInvalid(AgentJournalValidationResult result)
        {
            if (!result.IsValid)
            {
                throw new ArgumentException("Agent journal record is invalid: " + string.Join(", ", ErrorCodes(result)));
            }
        }

        private static IEnumerable<string> ErrorCodes(AgentJournalValidationResult result)
        {
            foreach (AgentJournalValidationError error in result.Errors)
            {
                yield return error.Code + "@" + error.Path;
            }
        }
    }
}

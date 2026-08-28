using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Shared privacy contract for prompt provenance emitted by Core and
    /// persisted by an operation host. Values identify configured inputs;
    /// model-facing prose is never valid metadata.
    /// </summary>
    public static class PromptTraceDiagnosticContract
    {
        public const string RuntimeDateeContextSource = "runtime:DateeContext";
        public const string CharacterDiagnosisSource = "character:psychiatric_diagnosis";
        public const string CharacterEmotionalDirectionRuntimeSource = "runtime:CharacterEmotionalDirection";
        public const string CharacterEmotionalStatusRuntimeSource = "runtime:CharacterEmotionalStatus";
        public const string LegacyEmotionalDirectorRuntimeSource = "runtime:EmotionalDirectorDirection";
        public const string EmotionalReactionCatalogSource = "data/prompts/emotional-reactions.yaml";
        public const string ConversationHistorySource = "conversation-history";
        public const string RuntimeDateeResponsePlanSource = "runtime:datee-response-plan";

        private static readonly string[] MetadataKeysArray =
        {
            "prompt_key",
            "system_prompt_source",
            "user_template_source",
            "compiled_input_sources",
            "compiled_input_keys",
            "prompt_trace_type",
            "prompt_trace_sources",
            "prompt_trace_keys",
        };

        private static readonly HashSet<string> MetadataKeySet =
            new HashSet<string>(MetadataKeysArray, StringComparer.Ordinal);

        private static readonly HashSet<string> ExactSources =
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeDateeContextSource,
                CharacterDiagnosisSource,
                CharacterEmotionalDirectionRuntimeSource,
                CharacterEmotionalStatusRuntimeSource,
                LegacyEmotionalDirectorRuntimeSource,
                EmotionalReactionCatalogSource,
                ConversationHistorySource,
                RuntimeDateeResponsePlanSource,
            };

        private static readonly HashSet<string> ExactTraceKeys = BuildExactTraceKeys();

        public static IReadOnlyList<string> MetadataKeys { get; } =
            Array.AsReadOnly(MetadataKeysArray);

        public static bool IsMetadataKey(string key)
            => key == "datee_private_phase" || MetadataKeySet.Contains(key);

        public static bool IsSafe(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (key)
            {
                case "datee_private_phase":
                    return value == "director"
                        || value == "performance"
                        || value == "response-plan-reconciliation";
                case "prompt_trace_type":
                    return value == "datee" || value == "emotional_director";
                case "prompt_key":
                    return IsCatalogKey(value);
                case "system_prompt_source":
                case "user_template_source":
                    return IsSafeSource(value);
                case "compiled_input_sources":
                case "prompt_trace_sources":
                    return IsSafeList(value, IsSafeSource);
                case "compiled_input_keys":
                case "prompt_trace_keys":
                    return IsSafeList(value, IsSafeTraceKey);
                default:
                    return false;
            }
        }

        private static bool IsSafeList(string value, Func<string, bool> validator)
        {
            if (value.Length > 2048)
                return false;

            string[] segments = value.Split(',');
            return segments.Length <= 128
                && segments.All(segment => segment.Length > 0
                    && segment == segment.Trim()
                    && validator(segment));
        }

        public static bool IsSafeSource(string value)
            => ExactSources.Contains(value);

        public static bool IsSafeTraceKey(string value)
            => ExactTraceKeys.Contains(value) || IsCatalogKey(value);

        private static bool IsCatalogKey(string value)
            => (value.StartsWith("emotional-reaction-", StringComparison.Ordinal)
                || value.StartsWith("character-emotional-", StringComparison.Ordinal))
                && value.Length <= 96
                && value.All(ch => (ch >= 'a' && ch <= 'z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '-');

        private static HashSet<string> BuildExactTraceKeys()
        {
            var keys = new HashSet<string>(
                TherapistDiagnosisContract.RequiredFields,
                StringComparer.Ordinal)
            {
                "PlayerDeliveredMessage",
                "conversation-history",
                "ConversationHistory.Sender",
                "ConversationHistory.Text",
                "CharacterEmotionalDirection.PrimaryEmotion",
                "CharacterEmotionalDirection.SecondaryEmotion",
                "CharacterEmotionalDirection.RegulatoryState",
                "CharacterEmotionalDirection.Activation",
                "CharacterEmotionalDirection.Trajectory",
                "CharacterEmotionalDirection.CoreThreatOrDesire",
                "CharacterEmotionalDirection.Interpretation",
                "CharacterEmotionalDirection.Impulse",
                "CharacterEmotionalDirection.Restraint",
                "CharacterEmotionalDirection.ResponsePosture",
                "SubjectName",
                "SubjectHfi",
                "SubjectTor",
                "CounterpartName",
                "CounterpartHfi",
                "CounterpartTor",
                DateeResponsePlan.CurrentSchemaVersion,
            };
            return keys;
        }
    }
}

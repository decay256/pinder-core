using System;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Pinder.Core.Conversation
{
    public enum ConversationParticipantRole
    {
        PlayerAvatar = 1,
        Datee = 2,
    }

    public enum PromptFactVisibility
    {
        PrivateToSubject = 1,
        Public = 2,
        RevealedToPlayerAvatar = 3,
        RevealedToDatee = 4,
    }

    public enum PromptFactSourceKind
    {
        Backstory = 1,
        PsychologicalStake = 2,
        Diagnosis = 3,
        CognitiveSubtext = 4,
        Conversation = 5,
        EngineEvent = 6,
        AuthoredTransitionTarget = 7,
    }

    /// <summary>
    /// Immutable V1 envelope for resolved character material. Source and
    /// revelation references are typed and parsed before their canonical string
    /// values can be exposed to diagnostics or serialization.
    /// </summary>
    public sealed class OwnedPromptFactV1
    {
        public const int CurrentSchemaVersion = 1;
        private static readonly Regex PlaceholderTokenPattern = new Regex("\\{[^{}\\r\\n]+\\}", RegexOptions.CultureInvariant);

        public OwnedPromptFactV1(
            Guid subjectCharacterId,
            ConversationParticipantRole subjectRole,
            PromptFactVisibility visibility,
            PromptFactSourceKind sourceKind,
            PromptFactSourceId sourceId,
            string text,
            ConversationMessageReference? revealedBy = null,
            int schemaVersion = CurrentSchemaVersion)
        {
            ValidateSchemaVersion(schemaVersion);
            ValidateCharacterId(subjectCharacterId, "fact.subject_character_id.required", nameof(subjectCharacterId));
            ValidateParticipantRole(subjectRole, "fact.subject_role.invalid", nameof(subjectRole));
            ValidateVisibility(visibility, "fact.visibility.invalid", nameof(visibility));
            ValidateSourceKind(sourceKind, "fact.source_kind.invalid", nameof(sourceKind));
            if (sourceId == null) throw new ArgumentNullException(nameof(sourceId));
            if (sourceId.SourceKind != sourceKind)
            {
                throw new RoleFactContractException("fact.source_kind.mismatch", "source_id format does not match source_kind.");
            }
            if (sourceId.CharacterId.HasValue && sourceId.CharacterId.Value != subjectCharacterId)
            {
                throw new RoleFactContractException("fact.source_id.subject_mismatch", "source_id character ownership does not match subject_character_id.");
            }
            ValidateText(text);
            if (RequiresRevelationEvidence(visibility) && revealedBy == null)
            {
                throw new RoleFactContractException("fact.revealed_by.required", "revealed_by is required for revealed visibility.");
            }
            if (!RequiresRevelationEvidence(visibility) && revealedBy != null)
            {
                throw new RoleFactContractException("fact.revealed_by.unexpected", "revealed_by is only valid for revealed visibility.");
            }

            SchemaVersion = schemaVersion;
            SubjectCharacterId = subjectCharacterId;
            SubjectRole = subjectRole;
            Visibility = visibility;
            SourceKind = sourceKind;
            SourceReference = sourceId;
            Text = text.Trim();
            RevelationEvidence = revealedBy;
        }

        public int SchemaVersion { get; }
        public Guid SubjectCharacterId { get; }
        public ConversationParticipantRole SubjectRole { get; }
        public PromptFactVisibility Visibility { get; }
        public PromptFactSourceKind SourceKind { get; }
        [JsonIgnore]
        public PromptFactSourceId SourceReference { get; }
        [JsonPropertyName("source_id")]
        public string SourceId => SourceReference.Value;
        public string Text { get; }
        [JsonIgnore]
        public ConversationMessageReference? RevelationEvidence { get; }
        [JsonPropertyName("revealed_by")]
        public string? RevealedBy => RevelationEvidence?.Value;

        internal static void ValidateCharacterId(Guid value, string code, string parameterName)
        {
            if (value == Guid.Empty) throw new RoleFactContractException(code, $"{parameterName} must be a non-empty UUID.");
        }

        internal static void ValidateParticipantRole(ConversationParticipantRole value, string code, string parameterName)
        {
            if (!Enum.IsDefined(typeof(ConversationParticipantRole), value))
                throw new RoleFactContractException(code, $"{parameterName} is not a supported conversation participant role.");
        }

        internal static void ValidateVisibility(PromptFactVisibility value, string code, string parameterName)
        {
            if (!Enum.IsDefined(typeof(PromptFactVisibility), value))
                throw new RoleFactContractException(code, $"{parameterName} is not a supported prompt fact visibility.");
        }

        internal static void ValidateSourceKind(PromptFactSourceKind value, string code, string parameterName)
        {
            if (!Enum.IsDefined(typeof(PromptFactSourceKind), value))
                throw new RoleFactContractException(code, $"{parameterName} is not a supported prompt fact source kind.");
        }

        internal static void ValidateRequiredString(string? value, string code, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new RoleFactContractException(code, $"{parameterName} must be non-blank.");
        }

        internal static bool RequiresRevelationEvidence(PromptFactVisibility visibility)
            => visibility == PromptFactVisibility.RevealedToPlayerAvatar || visibility == PromptFactVisibility.RevealedToDatee;

        private static void ValidateSchemaVersion(int schemaVersion)
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new RoleFactContractException("fact.schema_version.unsupported", "OwnedPromptFactV1 requires schema_version 1.");
        }

        private static void ValidateText(string? text)
        {
            ValidateRequiredString(text, "fact.text.required", nameof(text));
            string trimmed = text!.Trim();
            if (PlaceholderTokenPattern.IsMatch(trimmed)
                || trimmed.IndexOf("resolved stem text", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new RoleFactContractException("fact.text.placeholder", "Resolved prompt fact text must not contain unresolved placeholders.");
            }
        }
    }
}

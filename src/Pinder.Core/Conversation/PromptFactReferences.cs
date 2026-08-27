using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Canonical, content-free provenance identifier. Instances can only be
    /// created by parsing one of the closed V1 source formats.
    /// </summary>
    public sealed class PromptFactSourceId : IEquatable<PromptFactSourceId>
    {
        private const int MaximumLength = 256;
        private static readonly Regex OpaqueTokenPattern = new Regex(
            "^[a-z][a-z0-9_-]{0,63}$",
            RegexOptions.CultureInvariant);

        private PromptFactSourceId(
            string value,
            PromptFactSourceKind sourceKind,
            Guid? characterId = null,
            string? backstoryCategory = null,
            string? backstoryField = null,
            int? stakeIndex = null)
        {
            Value = value;
            SourceKind = sourceKind;
            CharacterId = characterId;
            BackstoryCategory = backstoryCategory;
            BackstoryField = backstoryField;
            StakeIndex = stakeIndex;
        }

        public string Value { get; }
        public PromptFactSourceKind SourceKind { get; }
        public Guid? CharacterId { get; }
        internal string? BackstoryCategory { get; }
        internal string? BackstoryField { get; }
        internal int? StakeIndex { get; }

        public static PromptFactSourceId Parse(string value)
        {
            OwnedPromptFactV1.ValidateRequiredString(value, "source_id.required", nameof(value));
            if (value.Length > MaximumLength || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw Invalid();
            }

            string[] parts = value.Split(':');
            PromptFactSourceKind kind;
            Guid? characterId = null;
            string? backstoryCategory = null;
            string? backstoryField = null;
            int? stakeIndex = null;
            if (parts.Length == 5 && parts[0] == "character" && parts[2] == "backstory")
            {
                characterId = RequireCanonicalGuid(parts[1]);
                backstoryCategory = RequireOpaqueToken(parts[3]);
                backstoryField = RequireOpaqueToken(parts[4]);
                kind = PromptFactSourceKind.Backstory;
            }
            else if (parts.Length == 4 && parts[0] == "character" && parts[2] == "stake")
            {
                characterId = RequireCanonicalGuid(parts[1]);
                stakeIndex = RequireCanonicalNonNegativeInteger(parts[3]);
                kind = PromptFactSourceKind.PsychologicalStake;
            }
            else if (parts.Length == 4 && parts[0] == "character" && parts[2] == "diagnosis")
            {
                characterId = RequireCanonicalGuid(parts[1]);
                RequireOpaqueToken(parts[3]);
                kind = PromptFactSourceKind.Diagnosis;
            }
            else if (parts.Length == 4 && parts[0] == "character" && parts[2] == "cognitive-subtext")
            {
                characterId = RequireCanonicalGuid(parts[1]);
                RequireCanonicalNonNegativeInteger(parts[3]);
                kind = PromptFactSourceKind.CognitiveSubtext;
            }
            else if (parts.Length == 4 && parts[0] == "conversation" && parts[1] == "turn")
            {
                ConversationMessageReference.Parse(value);
                kind = PromptFactSourceKind.Conversation;
            }
            else if (parts.Length == 4 && parts[0] == "engine" && parts[1] == "event")
            {
                RequireCanonicalNonNegativeInteger(parts[2]);
                RequireOpaqueToken(parts[3]);
                kind = PromptFactSourceKind.EngineEvent;
            }
            else if (parts.Length == 4 && parts[0] == "engine" && parts[1] == "authored-target")
            {
                RequireCanonicalNonNegativeInteger(parts[2]);
                RequireOpaqueToken(parts[3]);
                kind = PromptFactSourceKind.AuthoredTransitionTarget;
            }
            else
            {
                throw Invalid();
            }

            return new PromptFactSourceId(
                value,
                kind,
                characterId,
                backstoryCategory,
                backstoryField,
                stakeIndex);
        }

        internal static string RequireOpaqueToken(string value, string parameterName = "segment")
        {
            if (value == null || !OpaqueTokenPattern.IsMatch(value))
            {
                throw new RoleFactContractException(
                    "source_id.segment.invalid",
                    $"{parameterName} must be a lowercase opaque token containing only letters, digits, underscores, or hyphens.");
            }

            return value;
        }

        internal static string RoleToken(ConversationParticipantRole role)
        {
            OwnedPromptFactV1.ValidateParticipantRole(role, "source_id.sender_role.invalid", nameof(role));
            return role == ConversationParticipantRole.PlayerAvatar ? "PLAYER_AVATAR" : "DATEE";
        }

        internal static ConversationParticipantRole ParseRoleToken(string value)
        {
            if (value == "PLAYER_AVATAR") return ConversationParticipantRole.PlayerAvatar;
            if (value == "DATEE") return ConversationParticipantRole.Datee;
            throw new RoleFactContractException("conversation_reference.sender_role.invalid", "sender_role is not canonical.");
        }

        internal static int RequireCanonicalNonNegativeInteger(string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                || parsed < 0
                || !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw Invalid();
            }

            return parsed;
        }

        private static Guid RequireCanonicalGuid(string value)
        {
            if (!Guid.TryParseExact(value, "D", out Guid parsed)
                || !string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal))
            {
                throw Invalid();
            }

            return parsed;
        }

        private static RoleFactContractException Invalid()
            => new RoleFactContractException("source_id.format.invalid", "source_id is not a canonical V1 provenance identifier.");

        public override string ToString() => Value;
        public bool Equals(PromptFactSourceId? other)
            => other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => Equals(obj as PromptFactSourceId);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <summary>
    /// Canonical evidence that a conversation message revealed a fact.
    /// </summary>
    public sealed class ConversationMessageReference : IEquatable<ConversationMessageReference>
    {
        private ConversationMessageReference(int turn, ConversationParticipantRole senderRole)
        {
            Turn = turn;
            SenderRole = senderRole;
            Value = $"conversation:turn:{turn.ToString(CultureInfo.InvariantCulture)}:{PromptFactSourceId.RoleToken(senderRole)}";
        }

        public int Turn { get; }
        public ConversationParticipantRole SenderRole { get; }
        public string Value { get; }

        public static ConversationMessageReference Create(int turn, ConversationParticipantRole senderRole)
        {
            if (turn < 0)
            {
                throw new RoleFactContractException("conversation_reference.turn.invalid", "turn must be zero or greater.");
            }

            OwnedPromptFactV1.ValidateParticipantRole(senderRole, "conversation_reference.sender_role.invalid", nameof(senderRole));
            return new ConversationMessageReference(turn, senderRole);
        }

        public static ConversationMessageReference Parse(string value)
        {
            OwnedPromptFactV1.ValidateRequiredString(value, "conversation_reference.required", nameof(value));
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw Invalid();
            }

            string[] parts = value.Split(':');
            if (parts.Length != 4 || parts[0] != "conversation" || parts[1] != "turn")
            {
                throw Invalid();
            }

            int turn = PromptFactSourceId.RequireCanonicalNonNegativeInteger(parts[2]);
            ConversationParticipantRole senderRole = PromptFactSourceId.ParseRoleToken(parts[3]);
            var reference = Create(turn, senderRole);
            if (!string.Equals(reference.Value, value, StringComparison.Ordinal))
            {
                throw Invalid();
            }

            return reference;
        }

        private static RoleFactContractException Invalid()
            => new RoleFactContractException("conversation_reference.format.invalid", "revealed_by must be conversation:turn:{turn}:{sender_role} in canonical form.");

        public override string ToString() => Value;
        public bool Equals(ConversationMessageReference? other)
            => other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => Equals(obj as ConversationMessageReference);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    }
}

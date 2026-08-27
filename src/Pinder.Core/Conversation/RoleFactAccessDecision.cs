using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Text-free prompt fact admission result. It is safe to serialize into
    /// diagnostics because it carries provenance and reason codes only.
    /// </summary>
    public sealed class RoleFactAccessDecision
    {
        internal RoleFactAccessDecision(
            bool admitted,
            string code,
            string factSourceId,
            Guid subjectCharacterId,
            ConversationParticipantRole subjectRole,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility)
        {
            OwnedPromptFactV1.ValidateRequiredString(code, "decision.code.required", nameof(code));
            OwnedPromptFactV1.ValidateRequiredString(factSourceId, "decision.fact_source_id.required", nameof(factSourceId));
            OwnedPromptFactV1.ValidateCharacterId(subjectCharacterId, "decision.subject_character_id.required", nameof(subjectCharacterId));
            OwnedPromptFactV1.ValidateCharacterId(recipientCharacterId, "decision.recipient_character_id.required", nameof(recipientCharacterId));
            OwnedPromptFactV1.ValidateParticipantRole(subjectRole, "decision.subject_role.invalid", nameof(subjectRole));
            OwnedPromptFactV1.ValidateParticipantRole(recipientRole, "decision.recipient_role.invalid", nameof(recipientRole));
            OwnedPromptFactV1.ValidateVisibility(visibility, "decision.visibility.invalid", nameof(visibility));

            Admitted = admitted;
            Code = code;
            FactSourceId = factSourceId;
            SubjectCharacterId = subjectCharacterId;
            SubjectRole = subjectRole;
            RecipientCharacterId = recipientCharacterId;
            RecipientRole = recipientRole;
            Visibility = visibility;
        }

        public bool Admitted { get; }
        public string Code { get; }
        public string FactSourceId { get; }
        public Guid SubjectCharacterId { get; }
        public ConversationParticipantRole SubjectRole { get; }
        public Guid RecipientCharacterId { get; }
        public ConversationParticipantRole RecipientRole { get; }
        public PromptFactVisibility Visibility { get; }
    }
}

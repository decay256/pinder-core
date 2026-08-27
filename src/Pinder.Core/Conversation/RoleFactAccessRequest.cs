using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// A request to admit one owned prompt fact into the recipient role's LLM
    /// context. Callers must construct this before prompt assembly.
    /// </summary>
    public sealed class RoleFactAccessRequest
    {
        public RoleFactAccessRequest(
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            OwnedPromptFactV1 fact)
        {
            OwnedPromptFactV1.ValidateCharacterId(recipientCharacterId, "request.recipient_character_id.required", nameof(recipientCharacterId));
            OwnedPromptFactV1.ValidateParticipantRole(recipientRole, "request.recipient_role.invalid", nameof(recipientRole));
            Fact = fact ?? throw new ArgumentNullException(nameof(fact));
            RecipientCharacterId = recipientCharacterId;
            RecipientRole = recipientRole;
        }

        public Guid RecipientCharacterId { get; }
        public ConversationParticipantRole RecipientRole { get; }
        public OwnedPromptFactV1 Fact { get; }
    }
}

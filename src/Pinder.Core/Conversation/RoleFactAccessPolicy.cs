using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Single fail-closed authorization policy for role-specific prompt facts.
    /// Prompt adapters must call this policy and must not duplicate parallel
    /// ad-hoc owner, role, or visibility filters in provider code.
    /// </summary>
    public static class RoleFactAccessPolicy
    {
        public static RoleFactAccessDecision Decide(RoleFactAccessRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            OwnedPromptFactV1 fact = request.Fact;
            if (request.RecipientCharacterId == fact.SubjectCharacterId)
            {
                if (request.RecipientRole == fact.SubjectRole)
                {
                    return Decision(true, "admitted.subject", request);
                }

                return Decision(false, "denied.identity_role_mismatch", request);
            }

            if (request.RecipientRole == fact.SubjectRole)
            {
                return Decision(false, "denied.identity_role_mismatch", request);
            }

            switch (fact.Visibility)
            {
                case PromptFactVisibility.PrivateToSubject:
                    return Decision(false, "denied.private_to_subject", request);
                case PromptFactVisibility.Public:
                    return Decision(true, "admitted.public", request);
                case PromptFactVisibility.RevealedToPlayerAvatar:
                    return RoleRevealedDecision(request, ConversationParticipantRole.PlayerAvatar, "admitted.revealed_to_player_avatar", "denied.revealed_to_player_avatar");
                case PromptFactVisibility.RevealedToDatee:
                    return RoleRevealedDecision(request, ConversationParticipantRole.Datee, "admitted.revealed_to_datee", "denied.revealed_to_datee");
                default:
                    return Decision(false, "denied.unknown_visibility", request);
            }
        }

        private static RoleFactAccessDecision RoleRevealedDecision(
            RoleFactAccessRequest request,
            ConversationParticipantRole admittedRole,
            string admittedCode,
            string deniedCode)
        {
            if (request.Fact.RevelationEvidence == null)
            {
                return Decision(false, "denied.revealed_by_required", request);
            }

            return request.RecipientRole == admittedRole
                ? Decision(true, admittedCode, request)
                : Decision(false, deniedCode, request);
        }

        private static RoleFactAccessDecision Decision(bool admitted, string code, RoleFactAccessRequest request)
            => new RoleFactAccessDecision(
                admitted,
                code,
                request.Fact.SourceId,
                request.Fact.SourceKind,
                request.Fact.SubjectCharacterId,
                request.Fact.SubjectRole,
                request.RecipientCharacterId,
                request.RecipientRole,
                request.Fact.Visibility);
    }
}

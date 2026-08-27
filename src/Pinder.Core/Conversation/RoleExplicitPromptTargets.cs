using System;
using Pinder.Core.Characters;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Role-explicit transition/revelation target owned by the player avatar.
    /// It may feed avatar option/director compilation only after policy checks.
    /// </summary>
    public sealed class AvatarRevelationTarget
    {
        private AvatarRevelationTarget(Guid subjectCharacterId, OwnedPromptFactV1 fact)
        {
            SubjectCharacterId = subjectCharacterId;
            Fact = fact;
        }

        public Guid SubjectCharacterId { get; }
        public OwnedPromptFactV1 Fact { get; }
        public string SourceId => Fact.SourceId;
        public string Text => Fact.Text;

        public static AvatarRevelationTarget Create(Guid subjectCharacterId, OwnedPromptFactV1 fact)
        {
            ValidateTarget(subjectCharacterId, fact, ConversationParticipantRole.PlayerAvatar);
            return new AvatarRevelationTarget(subjectCharacterId, fact);
        }

        public static AvatarRevelationTarget FromLegacyResolvedTarget(
            ResolvedRevelationTarget target,
            Guid subjectCharacterId,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility,
            PromptFactSourceId sourceId,
            ConversationMessageReference? revealedBy = null)
        {
            OwnedPromptFactV1 fact = LegacyResolvedRevelationTargetConversion.ToOwnedFact(
                target,
                subjectCharacterId,
                ConversationParticipantRole.PlayerAvatar,
                recipientCharacterId,
                recipientRole,
                visibility,
                sourceId,
                revealedBy);
            return Create(subjectCharacterId, fact);
        }

        private static void ValidateTarget(Guid subjectCharacterId, OwnedPromptFactV1 fact, ConversationParticipantRole expectedRole)
        {
            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            OwnedPromptFactV1.ValidateCharacterId(subjectCharacterId, "target.subject_character_id.required", nameof(subjectCharacterId));
            if (fact.SubjectRole != expectedRole)
            {
                throw new RoleFactContractException("target.subject_role_mismatch", "Prompt target fact is owned by the wrong participant role.");
            }

            if (fact.SubjectCharacterId != subjectCharacterId)
            {
                throw new RoleFactContractException("target.subject_character_mismatch", "Prompt target fact is owned by a different character id.");
            }
        }
    }

    /// <summary>
    /// Role-explicit transition/reaction target owned by the DATEE character.
    /// It may feed DATEE director/performance compilation only after policy checks.
    /// </summary>
    public sealed class DateeReactionTarget
    {
        private DateeReactionTarget(Guid subjectCharacterId, OwnedPromptFactV1 fact)
        {
            SubjectCharacterId = subjectCharacterId;
            Fact = fact;
        }

        public Guid SubjectCharacterId { get; }
        public OwnedPromptFactV1 Fact { get; }
        public string SourceId => Fact.SourceId;
        public string Text => Fact.Text;

        public static DateeReactionTarget Create(Guid subjectCharacterId, OwnedPromptFactV1 fact)
        {
            ValidateTarget(subjectCharacterId, fact, ConversationParticipantRole.Datee);
            return new DateeReactionTarget(subjectCharacterId, fact);
        }

        public static DateeReactionTarget FromLegacyResolvedTarget(
            ResolvedRevelationTarget target,
            Guid subjectCharacterId,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility,
            PromptFactSourceId sourceId,
            ConversationMessageReference? revealedBy = null)
        {
            OwnedPromptFactV1 fact = LegacyResolvedRevelationTargetConversion.ToOwnedFact(
                target,
                subjectCharacterId,
                ConversationParticipantRole.Datee,
                recipientCharacterId,
                recipientRole,
                visibility,
                sourceId,
                revealedBy);
            return Create(subjectCharacterId, fact);
        }

        private static void ValidateTarget(Guid subjectCharacterId, OwnedPromptFactV1 fact, ConversationParticipantRole expectedRole)
        {
            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            OwnedPromptFactV1.ValidateCharacterId(subjectCharacterId, "target.subject_character_id.required", nameof(subjectCharacterId));
            if (fact.SubjectRole != expectedRole)
            {
                throw new RoleFactContractException("target.subject_role_mismatch", "Prompt target fact is owned by the wrong participant role.");
            }

            if (fact.SubjectCharacterId != subjectCharacterId)
            {
                throw new RoleFactContractException("target.subject_character_mismatch", "Prompt target fact is owned by a different character id.");
            }
        }
    }

    internal static class LegacyResolvedRevelationTargetConversion
    {
        public static OwnedPromptFactV1 ToOwnedFact(
            ResolvedRevelationTarget target,
            Guid subjectCharacterId,
            ConversationParticipantRole subjectRole,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility,
            PromptFactSourceId sourceId,
            ConversationMessageReference? revealedBy)
        {
            ValidateMetadata(
                target,
                subjectCharacterId,
                subjectRole,
                recipientCharacterId,
                recipientRole,
                visibility,
                sourceId);

            var fact = new OwnedPromptFactV1(
                subjectCharacterId,
                subjectRole,
                visibility,
                sourceId.SourceKind,
                sourceId,
                target.StemText,
                revealedBy);

            RoleFactAccessDecision decision = RoleFactAccessPolicy.Decide(new RoleFactAccessRequest(
                recipientCharacterId,
                recipientRole,
                fact));
            if (!decision.Admitted)
            {
                throw new RoleFactContractException(
                    "target.access_denied",
                    $"Resolved revelation target was denied by role fact access policy: {decision.Code}.");
            }

            return fact;
        }

        private static void ValidateMetadata(
            ResolvedRevelationTarget target,
            Guid subjectCharacterId,
            ConversationParticipantRole subjectRole,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            PromptFactVisibility visibility,
            PromptFactSourceId sourceId)
        {
            OwnedPromptFactV1.ValidateCharacterId(subjectCharacterId, "target.subject_character_id.required", nameof(subjectCharacterId));
            OwnedPromptFactV1.ValidateParticipantRole(subjectRole, "target.subject_role.invalid", nameof(subjectRole));
            OwnedPromptFactV1.ValidateCharacterId(recipientCharacterId, "target.recipient_character_id.required", nameof(recipientCharacterId));
            OwnedPromptFactV1.ValidateParticipantRole(recipientRole, "target.recipient_role.invalid", nameof(recipientRole));
            OwnedPromptFactV1.ValidateVisibility(visibility, "target.visibility.invalid", nameof(visibility));
            if (sourceId == null)
            {
                throw new ArgumentNullException(nameof(sourceId));
            }

            PromptFactSourceKind expectedSourceKind = ResolveExpectedSourceKind(target);
            if (sourceId.SourceKind != expectedSourceKind)
            {
                throw new RoleFactContractException(
                    "target.source_kind_mismatch",
                    "source_id format does not match the resolved revelation target registry.");
            }

            if (sourceId.CharacterId != subjectCharacterId)
            {
                throw new RoleFactContractException(
                    "target.source_id.subject_mismatch",
                    "source_id character ownership does not match the resolved revelation target owner.");
            }

            ValidateTargetProvenance(target, sourceId);
        }

        private static void ValidateTargetProvenance(
            ResolvedRevelationTarget target,
            PromptFactSourceId sourceId)
        {
            if (sourceId.SourceKind == PromptFactSourceKind.PsychologicalStake)
            {
                if (target.Index < 0 || target.Index >= EmotionStemSelectionRules.StakeRegistrySize)
                {
                    throw new RoleFactContractException(
                        "target.index.invalid",
                        "Stake revelation target index is outside the canonical registry.");
                }

                if (sourceId.StakeIndex != target.Index)
                {
                    throw new RoleFactContractException(
                        "target.source_id.index_mismatch",
                        "source_id stake index does not match the resolved revelation target index.");
                }

                return;
            }

            if (target.Index < 0 || target.Index >= BackstoryValidator.RequiredCategories.Count)
            {
                throw new RoleFactContractException(
                    "target.index.invalid",
                    "Backstory revelation target index is outside the canonical registry.");
            }

            string expectedCategory = BackstoryValidator.RequiredCategories[target.Index];
            if (!string.Equals(sourceId.BackstoryCategory, expectedCategory, StringComparison.Ordinal))
            {
                throw new RoleFactContractException(
                    "target.source_id.category_mismatch",
                    "source_id backstory category does not match the resolved revelation target index.");
            }

            string expectedField = string.Equals(target.Field, "BIO_LIE", StringComparison.Ordinal)
                ? "bio_lie"
                : "tragic_reality";
            if (!string.Equals(sourceId.BackstoryField, expectedField, StringComparison.Ordinal))
            {
                throw new RoleFactContractException(
                    "target.source_id.field_mismatch",
                    "source_id backstory field does not match the resolved revelation target field.");
            }
        }

        private static PromptFactSourceKind ResolveExpectedSourceKind(ResolvedRevelationTarget target)
        {
            string registry = target.Registry;
            string field = target.Field;
            if (string.Equals(registry, EmotionStemSelectionRules.BackstoryRegistry, StringComparison.Ordinal))
            {
                if (string.Equals(field, "BIO_LIE", StringComparison.Ordinal)
                    || string.Equals(field, "TRAGIC_REALITY", StringComparison.Ordinal))
                {
                    return PromptFactSourceKind.Backstory;
                }

                throw new RoleFactContractException(
                    "target.field.invalid",
                    "Backstory revelation targets must use BIO_LIE or TRAGIC_REALITY.");
            }

            if (string.Equals(registry, EmotionStemSelectionRules.StakeRegistry, StringComparison.Ordinal))
            {
                if (string.Equals(field, "STAKE_LINE", StringComparison.Ordinal))
                {
                    return PromptFactSourceKind.PsychologicalStake;
                }

                throw new RoleFactContractException(
                    "target.field.invalid",
                    "Stake revelation targets must use STAKE_LINE.");
            }

            throw new RoleFactContractException(
                "target.registry.invalid",
                "Resolved revelation target registry is not supported.");
        }
    }
}

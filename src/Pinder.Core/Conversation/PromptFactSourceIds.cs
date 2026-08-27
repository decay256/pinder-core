using System;
using System.Globalization;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Builders for stable, content-free source ids used by owned prompt facts.
    /// Variable segments are bounded opaque tokens, never display text.
    /// </summary>
    public static class PromptFactSourceIds
    {
        public static PromptFactSourceId Backstory(Guid characterId, string category, string field)
        {
            ValidateCharacter(characterId);
            return Parse($"character:{characterId:D}:backstory:{Token(category, nameof(category))}:{Token(field, nameof(field))}");
        }

        public static PromptFactSourceId PsychologicalStake(Guid characterId, int zeroBasedIndex)
        {
            ValidateCharacter(characterId);
            ValidateNonNegative(zeroBasedIndex, nameof(zeroBasedIndex));
            return Parse($"character:{characterId:D}:stake:{zeroBasedIndex.ToString(CultureInfo.InvariantCulture)}");
        }

        public static PromptFactSourceId Diagnosis(Guid characterId, string key)
        {
            ValidateCharacter(characterId);
            return Parse($"character:{characterId:D}:diagnosis:{Token(key, nameof(key))}");
        }

        public static PromptFactSourceId CognitiveSubtext(Guid characterId, int turn)
        {
            ValidateCharacter(characterId);
            ValidateNonNegative(turn, nameof(turn));
            return Parse($"character:{characterId:D}:cognitive-subtext:{turn.ToString(CultureInfo.InvariantCulture)}");
        }

        public static PromptFactSourceId VisibleMessage(int turn, ConversationParticipantRole senderRole)
            => Parse(ConversationMessageReference.Create(turn, senderRole).Value);

        public static PromptFactSourceId EngineEvent(int turn, string stableKey)
        {
            ValidateNonNegative(turn, nameof(turn));
            return Parse($"engine:event:{turn.ToString(CultureInfo.InvariantCulture)}:{Token(stableKey, nameof(stableKey))}");
        }

        public static PromptFactSourceId AuthoredTransitionTarget(int turn, string stableKey)
        {
            ValidateNonNegative(turn, nameof(turn));
            return Parse($"engine:authored-target:{turn.ToString(CultureInfo.InvariantCulture)}:{Token(stableKey, nameof(stableKey))}");
        }

        private static PromptFactSourceId Parse(string value) => PromptFactSourceId.Parse(value);

        private static void ValidateCharacter(Guid characterId)
            => OwnedPromptFactV1.ValidateCharacterId(characterId, "source_id.character_id.required", nameof(characterId));

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new RoleFactContractException("source_id.index.invalid", $"{parameterName} must be zero or greater.");
            }
        }

        private static string Token(string value, string parameterName)
            => PromptFactSourceId.RequireOpaqueToken(value, parameterName);
    }
}

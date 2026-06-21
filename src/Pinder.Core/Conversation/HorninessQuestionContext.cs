using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context for generating a horny follow-up question after a horniness miss roll.
    /// Contains the conversation state needed to produce a contextual question
    /// that is appended to the message.
    /// </summary>
    public sealed class HorninessQuestionContext
    {
        /// <summary>The player character's system prompt (for voice consistency).</summary>
        public string PlayerAvatarPrompt { get; }

        /// <summary>The datee character's display name.</summary>
        public string DateeName { get; }

        /// <summary>The player character's display name.</summary>
        public string PlayerName { get; }

        /// <summary>The message the player just delivered this turn.</summary>
        public string DeliveredMessage { get; }

        /// <summary>Full conversation history up to this point.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        public HorninessQuestionContext(
            string playerAvatarPrompt,
            string dateeName,
            string playerName,
            string deliveredMessage,
            IReadOnlyList<(string Sender, string Text)> conversationHistory)
        {
            PlayerAvatarPrompt = playerAvatarPrompt ?? "";
            DateeName = dateeName ?? "";
            PlayerName = playerName ?? "";
            DeliveredMessage = deliveredMessage ?? "";
            ConversationHistory = conversationHistory ?? (IReadOnlyList<(string Sender, string Text)>)new List<(string, string)>();
        }
    }
}

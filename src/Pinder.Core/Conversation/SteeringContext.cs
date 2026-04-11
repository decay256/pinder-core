using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context for generating a steering question after a successful steering roll.
    /// Contains the conversation state needed to produce a contextual question
    /// that nudges the conversation toward meeting up.
    /// </summary>
    public sealed class SteeringContext
    {
        /// <summary>The player character's system prompt (for voice consistency).</summary>
        public string PlayerPrompt { get; }

        /// <summary>The opponent character's display name.</summary>
        public string OpponentName { get; }

        /// <summary>The player character's display name.</summary>
        public string PlayerName { get; }

        /// <summary>The message the player just delivered this turn.</summary>
        public string DeliveredMessage { get; }

        /// <summary>Full conversation history up to this point.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        public SteeringContext(
            string playerPrompt,
            string opponentName,
            string playerName,
            string deliveredMessage,
            IReadOnlyList<(string Sender, string Text)> conversationHistory)
        {
            PlayerPrompt = playerPrompt ?? "";
            OpponentName = opponentName ?? "";
            PlayerName = playerName ?? "";
            DeliveredMessage = deliveredMessage ?? "";
            ConversationHistory = conversationHistory ?? (IReadOnlyList<(string Sender, string Text)>)new List<(string, string)>();
        }
    }
}

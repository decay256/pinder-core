using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when generating dialogue options.
    /// Contains exactly what the LLM needs and nothing more.
    /// </summary>
    public sealed class DialogueContext
    {
        /// <summary>Assembled system prompt for the player character.</summary>
        public string PlayerPrompt { get; }

        /// <summary>Assembled system prompt for the opponent character.</summary>
        public string OpponentPrompt { get; }

        /// <summary>Conversation history as (sender, text) pairs in order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The opponent's last message, or empty if first turn.</summary>
        public string OpponentLastMessage { get; }

        /// <summary>Names of currently active traps (for taint injection).</summary>
        public IReadOnlyList<string> ActiveTraps { get; }

        /// <summary>Current interest meter value.</summary>
        public int CurrentInterest { get; }

        public DialogueContext(
            string playerPrompt,
            string opponentPrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string opponentLastMessage,
            IReadOnlyList<string> activeTraps,
            int currentInterest)
        {
            PlayerPrompt = playerPrompt ?? throw new System.ArgumentNullException(nameof(playerPrompt));
            OpponentPrompt = opponentPrompt ?? throw new System.ArgumentNullException(nameof(opponentPrompt));
            ConversationHistory = conversationHistory ?? throw new System.ArgumentNullException(nameof(conversationHistory));
            OpponentLastMessage = opponentLastMessage ?? throw new System.ArgumentNullException(nameof(opponentLastMessage));
            ActiveTraps = activeTraps ?? throw new System.ArgumentNullException(nameof(activeTraps));
            CurrentInterest = currentInterest;
        }
    }
}

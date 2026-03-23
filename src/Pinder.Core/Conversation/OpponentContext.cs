using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when generating the opponent's response.
    /// </summary>
    public sealed class OpponentContext
    {
        /// <summary>Assembled system prompt for the player character.</summary>
        public string PlayerPrompt { get; }

        /// <summary>Assembled system prompt for the opponent character.</summary>
        public string OpponentPrompt { get; }

        /// <summary>Conversation history as (sender, text) pairs in order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The opponent's last message, or empty if first turn.</summary>
        public string OpponentLastMessage { get; }

        /// <summary>Names of currently active traps.</summary>
        public IReadOnlyList<string> ActiveTraps { get; }

        /// <summary>Current interest meter value.</summary>
        public int CurrentInterest { get; }

        /// <summary>The player's delivered message (post-degradation).</summary>
        public string PlayerDeliveredMessage { get; }

        /// <summary>Interest value before this turn's roll.</summary>
        public int InterestBefore { get; }

        /// <summary>Interest value after this turn's roll.</summary>
        public int InterestAfter { get; }

        /// <summary>Opponent's simulated response delay in minutes (from timing profile).</summary>
        public double ResponseDelayMinutes { get; }

        public OpponentContext(
            string playerPrompt,
            string opponentPrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string opponentLastMessage,
            IReadOnlyList<string> activeTraps,
            int currentInterest,
            string playerDeliveredMessage,
            int interestBefore,
            int interestAfter,
            double responseDelayMinutes)
        {
            PlayerPrompt = playerPrompt ?? throw new System.ArgumentNullException(nameof(playerPrompt));
            OpponentPrompt = opponentPrompt ?? throw new System.ArgumentNullException(nameof(opponentPrompt));
            ConversationHistory = conversationHistory ?? throw new System.ArgumentNullException(nameof(conversationHistory));
            OpponentLastMessage = opponentLastMessage ?? throw new System.ArgumentNullException(nameof(opponentLastMessage));
            ActiveTraps = activeTraps ?? throw new System.ArgumentNullException(nameof(activeTraps));
            CurrentInterest = currentInterest;
            PlayerDeliveredMessage = playerDeliveredMessage ?? throw new System.ArgumentNullException(nameof(playerDeliveredMessage));
            InterestBefore = interestBefore;
            InterestAfter = interestAfter;
            ResponseDelayMinutes = responseDelayMinutes;
        }
    }
}

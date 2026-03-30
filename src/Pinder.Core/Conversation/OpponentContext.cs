using System.Collections.Generic;
using Pinder.Core.Stats;

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

        /// <summary>Shadow stat thresholds for the player, or null if not applicable.</summary>
        public Dictionary<ShadowStatType, int>? ShadowThresholds { get; }

        /// <summary>Full trap taint instructions for active traps, or null if none.</summary>
        public string[]? ActiveTrapInstructions { get; }

        /// <summary>Display name of the player character. Default empty for backward compatibility.</summary>
        public string PlayerName { get; }

        /// <summary>Display name of the opponent character. Default empty for backward compatibility.</summary>
        public string OpponentName { get; }

        /// <summary>Current turn number (1-based). Default 0 for backward compatibility.</summary>
        public int CurrentTurn { get; }

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
            double responseDelayMinutes,
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            string playerName = "",
            string opponentName = "",
            int currentTurn = 0)
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
            ShadowThresholds = shadowThresholds;
            ActiveTrapInstructions = activeTrapInstructions;
            PlayerName = playerName ?? "";
            OpponentName = opponentName ?? "";
            CurrentTurn = currentTurn;
        }
    }
}

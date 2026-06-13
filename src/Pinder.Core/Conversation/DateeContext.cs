using System.Collections.Generic;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when generating the datee's response.
    /// </summary>
    public sealed class DateeContext
    {
        /// <summary>Assembled system prompt for the player character.</summary>
        public string PlayerAvatarPrompt { get; }

        /// <summary>Assembled system prompt for the datee character.</summary>
        public string DateePrompt { get; }

        /// <summary>Conversation history as (sender, text) pairs in order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The datee's last message, or empty if first turn.</summary>
        public string DateeLastMessage { get; }

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

        /// <summary>Datee's simulated response delay in minutes (from timing profile).</summary>
        public double ResponseDelayMinutes { get; }

        /// <summary>Shadow stat thresholds for the player, or null if not applicable.</summary>
        public Dictionary<ShadowStatType, int>? ShadowThresholds { get; }

        /// <summary>Full trap taint instructions for active traps, or null if none.</summary>
        public string[]? ActiveTrapInstructions { get; }

        /// <summary>Display name of the player character. Default empty for backward compatibility.</summary>
        public string PlayerName { get; }

        /// <summary>Display name of the datee character. Default empty for backward compatibility.</summary>
        public string DateeName { get; }

        /// <summary>Current turn number (1-based). Default 0 for backward compatibility.</summary>
        public int CurrentTurn { get; }

        /// <summary>Failure tier of the player's last roll. None means success. Default None for backward compatibility.</summary>
        public FailureTier DeliveryTier { get; }

        /// <summary>Active archetype directive for the datee character, or null if none.</summary>
        public string ActiveArchetypeDirective { get; }

        public DateeContext(
            string playerAvatarPrompt,
            string dateePrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string dateeLastMessage,
            IReadOnlyList<string> activeTraps,
            int currentInterest,
            string playerDeliveredMessage,
            int interestBefore,
            int interestAfter,
            double responseDelayMinutes,
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            string playerName = "",
            string dateeName = "",
            int currentTurn = 0,
            FailureTier deliveryTier = FailureTier.Success,
            string activeArchetypeDirective = null)
        {
            PlayerAvatarPrompt = playerAvatarPrompt ?? throw new System.ArgumentNullException(nameof(playerAvatarPrompt));
            DateePrompt = dateePrompt ?? throw new System.ArgumentNullException(nameof(dateePrompt));
            ConversationHistory = conversationHistory ?? throw new System.ArgumentNullException(nameof(conversationHistory));
            DateeLastMessage = dateeLastMessage ?? throw new System.ArgumentNullException(nameof(dateeLastMessage));
            ActiveTraps = activeTraps ?? throw new System.ArgumentNullException(nameof(activeTraps));
            CurrentInterest = currentInterest;
            PlayerDeliveredMessage = playerDeliveredMessage ?? throw new System.ArgumentNullException(nameof(playerDeliveredMessage));
            InterestBefore = interestBefore;
            InterestAfter = interestAfter;
            ResponseDelayMinutes = responseDelayMinutes;
            ShadowThresholds = shadowThresholds;
            ActiveTrapInstructions = activeTrapInstructions;
            PlayerName = playerName ?? "";
            DateeName = dateeName ?? "";
            CurrentTurn = currentTurn;
            DeliveryTier = deliveryTier;
            ActiveArchetypeDirective = activeArchetypeDirective;
        }
    }
}

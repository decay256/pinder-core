using System.Collections.Generic;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when delivering a chosen dialogue option.
    /// Includes outcome information for degradation.
    /// </summary>
    public sealed class DeliveryContext
    {
        /// <summary>Assembled system prompt for the player character.</summary>
        public string PlayerPrompt { get; }

        /// <summary>Assembled system prompt for the opponent character.</summary>
        public string OpponentPrompt { get; }

        /// <summary>Conversation history as (sender, text) pairs in order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The opponent's last message, or empty if first turn.</summary>
        public string OpponentLastMessage { get; }

        /// <summary>The dialogue option the player chose.</summary>
        public DialogueOption ChosenOption { get; }

        /// <summary>
        /// The failure tier of the roll outcome.
        /// None means success; any other value means failure at that tier.
        /// </summary>
        public FailureTier Outcome { get; }

        /// <summary>How much the roll beat the DC by (for success grading). Negative or zero on failure.</summary>
        public int BeatDcBy { get; }

        /// <summary>Names/IDs of currently active traps (for taint injection).</summary>
        public IReadOnlyList<string> ActiveTraps { get; }

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

        /// <summary>True if the roll was a Natural 20.</summary>
        public bool IsNat20 { get; }

        /// <summary>
        /// Stat-specific failure instruction text from delivery-instructions.yaml, or null on success.
        /// Only populated when the roll is a failure.
        /// </summary>
        public string StatFailureInstruction { get; }

        /// <summary>
        /// Active archetype directive for the player character (e.g.
        /// <c>"ACTIVE ARCHETYPE: The Peacock (clear)\n..."</c>), or null if
        /// the player has no active archetype. Threaded into the delivery LLM
        /// prompt so the rewrite respects the character's voice (#372 / #375).
        /// </summary>
        public string ActiveArchetypeDirective { get; }

        public DeliveryContext(
            string playerPrompt,
            string opponentPrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string opponentLastMessage,
            DialogueOption chosenOption,
            FailureTier outcome,
            int beatDcBy,
            IReadOnlyList<string> activeTraps,
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            string playerName = "",
            string opponentName = "",
            int currentTurn = 0,
            bool isNat20 = false,
            string statFailureInstruction = null,
            string activeArchetypeDirective = null)
        {
            PlayerPrompt = playerPrompt ?? throw new System.ArgumentNullException(nameof(playerPrompt));
            OpponentPrompt = opponentPrompt ?? throw new System.ArgumentNullException(nameof(opponentPrompt));
            ConversationHistory = conversationHistory ?? throw new System.ArgumentNullException(nameof(conversationHistory));
            OpponentLastMessage = opponentLastMessage ?? throw new System.ArgumentNullException(nameof(opponentLastMessage));
            ChosenOption = chosenOption ?? throw new System.ArgumentNullException(nameof(chosenOption));
            Outcome = outcome;
            BeatDcBy = beatDcBy;
            ActiveTraps = activeTraps ?? throw new System.ArgumentNullException(nameof(activeTraps));
            ShadowThresholds = shadowThresholds;
            ActiveTrapInstructions = activeTrapInstructions;
            PlayerName = playerName ?? "";
            OpponentName = opponentName ?? "";
            CurrentTurn = currentTurn;
            IsNat20 = isNat20;
            StatFailureInstruction = statFailureInstruction;
            ActiveArchetypeDirective = activeArchetypeDirective;
        }
    }
}

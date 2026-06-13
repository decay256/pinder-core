using System.Collections.Generic;
using Pinder.Core.Characters;
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
        /// <summary>Assembled system prompt for the player avatar (the in-game character the human portrays) — this session's own character.</summary>
        public string PlayerAvatarPrompt { get; }

        /// <summary>
        /// Issue #1123 — strict bleed isolation. The avatar (delivery) session
        /// carries ONLY the datee's PUBLIC dating-app card (name + public
        /// profile fields), never the datee's full assembled system prompt
        /// (private stake, stat block, archetype directives, voice spec). The
        /// datee otherwise appears only as sent messages in the labelled
        /// transcript. Never null; <see cref="PublicProfileCard.Empty"/> when no
        /// card is supplied.
        /// </summary>
        public PublicProfileCard DateeCard { get; }

        /// <summary>Conversation history as (sender, text) pairs in order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The datee's last message, or empty if first turn.</summary>
        public string DateeLastMessage { get; }

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

        /// <summary>Display name of the datee character. Default empty for backward compatibility.</summary>
        public string DateeName { get; }

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
            string playerAvatarPrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string dateeLastMessage,
            DialogueOption chosenOption,
            FailureTier outcome,
            int beatDcBy,
            IReadOnlyList<string> activeTraps,
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            string playerName = "",
            string dateeName = "",
            int currentTurn = 0,
            bool isNat20 = false,
            string statFailureInstruction = null,
            string activeArchetypeDirective = null,
            PublicProfileCard? dateeCard = null)
        {
            PlayerAvatarPrompt = playerAvatarPrompt ?? throw new System.ArgumentNullException(nameof(playerAvatarPrompt));
            DateeCard = dateeCard ?? PublicProfileCard.Empty;
            ConversationHistory = conversationHistory ?? throw new System.ArgumentNullException(nameof(conversationHistory));
            DateeLastMessage = dateeLastMessage ?? throw new System.ArgumentNullException(nameof(dateeLastMessage));
            ChosenOption = chosenOption ?? throw new System.ArgumentNullException(nameof(chosenOption));
            Outcome = outcome;
            BeatDcBy = beatDcBy;
            ActiveTraps = activeTraps ?? throw new System.ArgumentNullException(nameof(activeTraps));
            ShadowThresholds = shadowThresholds;
            ActiveTrapInstructions = activeTrapInstructions;
            PlayerName = playerName ?? "";
            DateeName = dateeName ?? "";
            CurrentTurn = currentTurn;
            IsNat20 = isNat20;
            StatFailureInstruction = statFailureInstruction;
            ActiveArchetypeDirective = activeArchetypeDirective;
        }
    }
}

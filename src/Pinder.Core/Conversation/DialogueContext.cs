using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when generating dialogue options.
    /// Contains exactly what the LLM needs and nothing more.
    /// </summary>
    public sealed class DialogueContext
    {
        /// <summary>Assembled system prompt for the player character.</summary>
        public string PlayerAvatarPrompt { get; }

        /// <summary>Assembled system prompt for the datee character.</summary>
        public string DateePrompt { get; }

        /// <summary>Conversation history as (sender, text) pairs in order.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        /// <summary>The datee's last message, or empty if first turn.</summary>
        public string DateeLastMessage { get; }

        /// <summary>Names of currently active traps (for taint injection).</summary>
        public IReadOnlyList<string> ActiveTraps { get; }

        /// <summary>Current interest meter value.</summary>
        public int CurrentInterest { get; }

        /// <summary>Shadow stat thresholds for the player, or null if not applicable.</summary>
        public Dictionary<ShadowStatType, int>? ShadowThresholds { get; }

        /// <summary>Available callback opportunities from prior turns, or null if none.</summary>
        public List<CallbackOpportunity>? CallbackOpportunities { get; }

        /// <summary>Current horniness shadow stat level (0 if not applicable).</summary>
        public int HorninessLevel { get; }

        /// <summary>Whether a Rizz option must be included due to Horniness mechanic.</summary>
        public bool RequiresRizzOption { get; }

        /// <summary>Full trap taint instructions for active traps, or null if none.</summary>
        public string[]? ActiveTrapInstructions { get; }

        /// <summary>Display name of the player character. Default empty for backward compatibility.</summary>
        public string PlayerName { get; }

        /// <summary>Display name of the datee character. Default empty for backward compatibility.</summary>
        public string DateeName { get; }

        /// <summary>Current turn number (1-based). Default 0 for backward compatibility.</summary>
        public int CurrentTurn { get; }

        /// <summary>The player's texting style fragment for voice reinforcement. Empty string if not available.</summary>
        public string PlayerTextingStyle { get; }

        /// <summary>The datee's active tell from their last response, if any. Used to craft specific options.</summary>
        public Tell? ActiveTell { get; }

        /// <summary>The stats available for options this turn (randomly drawn). Null means all 6 stats available.</summary>
        public StatType[]? AvailableStats { get; }

        /// <summary>Active archetype directive for the player character, or null if none.</summary>
        public string ActiveArchetypeDirective { get; }

        /// <summary>Max dialogue options configured in GameDefinition. Default 3.</summary>
        public int MaxDialogueOptions { get; }

        /// <summary>
        /// #950: parsed stake lines (one entry per numbered line from the PSYCHOLOGICAL STAKE block).
        /// When non-empty, SessionDocumentBuilder injects a per-turn stake-coverage summary so the
        /// option generator knows which lines are still untouched.
        /// Null or empty means no stake is available (skip coverage injection).
        /// </summary>
        public string[]? StakeLines { get; }

        /// <summary>
        /// #950: 0-based indices of stake lines already referenced in a chosen option this session.
        /// Used with <see cref="StakeLines"/> to build the untouched-lines list injected per turn.
        /// Null or empty means no lines have been referenced yet.
        /// </summary>
        public System.Collections.Generic.IReadOnlyCollection<int>? StakeLinesReferenced { get; }

        public DialogueContext(
            string playerAvatarPrompt,
            string dateePrompt,
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string dateeLastMessage,
            IReadOnlyList<string> activeTraps,
            int currentInterest,
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            List<CallbackOpportunity>? callbackOpportunities = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            string[]? activeTrapInstructions = null,
            string playerName = "",
            string dateeName = "",
            int currentTurn = 0,
            string playerTextingStyle = "",
            Tell? activeTell = null,
            StatType[]? availableStats = null,
            string activeArchetypeDirective = null,
            string[]? stakeLines = null,
            System.Collections.Generic.IReadOnlyCollection<int>? stakeLinesReferenced = null,
            int maxDialogueOptions = 3)
        {
            PlayerAvatarPrompt = playerAvatarPrompt ?? throw new System.ArgumentNullException(nameof(playerAvatarPrompt));
            DateePrompt = dateePrompt ?? throw new System.ArgumentNullException(nameof(dateePrompt));
            ConversationHistory = conversationHistory ?? throw new System.ArgumentNullException(nameof(conversationHistory));
            DateeLastMessage = dateeLastMessage ?? throw new System.ArgumentNullException(nameof(dateeLastMessage));
            ActiveTraps = activeTraps ?? throw new System.ArgumentNullException(nameof(activeTraps));
            CurrentInterest = currentInterest;
            ShadowThresholds = shadowThresholds;
            CallbackOpportunities = callbackOpportunities;
            HorninessLevel = horninessLevel;
            RequiresRizzOption = requiresRizzOption;
            ActiveTrapInstructions = activeTrapInstructions;
            PlayerName = playerName ?? "";
            DateeName = dateeName ?? "";
            CurrentTurn = currentTurn;
            PlayerTextingStyle = playerTextingStyle ?? "";
            ActiveTell = activeTell;
            AvailableStats = availableStats;
            ActiveArchetypeDirective = activeArchetypeDirective;
            StakeLines = stakeLines;
            StakeLinesReferenced = stakeLinesReferenced;
            MaxDialogueOptions = maxDialogueOptions;
        }
    }
}

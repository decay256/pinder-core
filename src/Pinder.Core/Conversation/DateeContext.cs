using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when generating the datee's response.
    /// </summary>
    public sealed class DateeContext
    {
        /// <summary>
        /// Issue #1123 — strict bleed isolation. The datee session carries ONLY
        /// the avatar's PUBLIC dating-app card (name + public profile fields),
        /// never the avatar's full assembled system prompt (private stake, stat
        /// block, archetype directives, voice spec). The avatar otherwise
        /// appears only as sent messages in the labelled transcript. Never null;
        /// <see cref="PublicProfileCard.Empty"/> when no card is supplied.
        /// </summary>
        public PublicProfileCard PlayerAvatarCard { get; }

        /// <summary>Assembled system prompt for the datee character (this session's own character — not a bleed).</summary>
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

        /// <summary>True when the horniness overlay made the player's delivered message overtly horny/eager this turn.</summary>
        public bool HorninessOverlayApplied { get; }

        /// <summary>
        /// Miss tier of the horniness overlay this turn; only meaningful when HorninessOverlayApplied is true.
        /// </summary>
        public FailureTier HorninessTier { get; }

        /// <summary>
        /// Resolved revelation target for injecting into the LLM prompt.
        /// </summary>
        public ResolvedRevelationTarget? ResolvedTarget { get; }

        /// <summary>
        /// Therapeutic cognitive subtext for injecting into the LLM prompt.
        /// </summary>
        public string? CognitiveSubtext { get; }

        public DateeContext(
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
            string activeArchetypeDirective = null,
            PublicProfileCard? playerAvatarCard = null,
            bool horninessOverlayApplied = false,
            FailureTier horninessTier = FailureTier.Success,
            ResolvedRevelationTarget? resolvedTarget = null,
            string? cognitiveSubtext = null)
        {
            PlayerAvatarCard = playerAvatarCard ?? PublicProfileCard.Empty;
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
            HorninessOverlayApplied = horninessOverlayApplied;
            HorninessTier = horninessTier;
            ResolvedTarget = resolvedTarget;
            CognitiveSubtext = cognitiveSubtext;
        }
    }
}

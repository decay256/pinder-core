using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context for generating an improved message after a strong or legendary success roll.
    /// Contains the conversation state needed to produce a better line.
    /// </summary>
    public sealed class SuccessImprovementContext
    {
        /// <summary>The player character's system prompt (for voice consistency).</summary>
        public string PlayerAvatarPrompt { get; }

        /// <summary>The datee character's display name.</summary>
        public string DateeName { get; }

        /// <summary>The player character's display name.</summary>
        public string PlayerName { get; }

        /// <summary>The message the player intended to deliver this turn before improvement.</summary>
        public string DeliveredMessage { get; }

        /// <summary>The stat used for the roll.</summary>
        public StatType Stat { get; }

        /// <summary>The tier key for the success margin (e.g. "strong", "critical", "exceptional").</summary>
        public string TierKey { get; }

        /// <summary>Full conversation history up to this point.</summary>
        public IReadOnlyList<(string Sender, string Text)> ConversationHistory { get; }

        public SuccessImprovementContext(
            string playerAvatarPrompt,
            string dateeName,
            string playerName,
            string deliveredMessage,
            StatType stat,
            string tierKey,
            IReadOnlyList<(string Sender, string Text)> conversationHistory)
        {
            PlayerAvatarPrompt = playerAvatarPrompt ?? "";
            DateeName = dateeName ?? "";
            PlayerName = playerName ?? "";
            DeliveredMessage = deliveredMessage ?? "";
            Stat = stat;
            TierKey = tierKey ?? "";
            ConversationHistory = conversationHistory ?? (IReadOnlyList<(string Sender, string Text)>)new List<(string, string)>();
        }
    }
}
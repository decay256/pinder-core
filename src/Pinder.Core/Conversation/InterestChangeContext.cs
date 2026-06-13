namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when interest crosses a threshold,
    /// used to generate a narrative beat.
    /// </summary>
    public sealed class InterestChangeContext
    {
        /// <summary>The datee character's name.</summary>
        public string DateeName { get; }

        /// <summary>Interest value before the change.</summary>
        public int InterestBefore { get; }

        /// <summary>Interest value after the change.</summary>
        public int InterestAfter { get; }

        /// <summary>The new interest state after the change.</summary>
        public InterestState NewState { get; }

        /// <summary>
        /// The datee's assembled system prompt, used to generate
        /// interest change beats in the datee's voice/character.
        /// </summary>
        public string? DateePrompt { get; }

        /// <summary>
        /// Recent conversation history — passed so the beat can reference specific details.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<(string Sender, string Text)>? ConversationHistory { get; }

        /// <summary>The player character's display name (used to label history entries).</summary>
        public string? PlayerName { get; }

        public InterestChangeContext(
            string dateeName,
            int interestBefore,
            int interestAfter,
            InterestState newState,
            string? dateePrompt = null,
            System.Collections.Generic.IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string? playerName = null)
        {
            DateeName = dateeName ?? throw new System.ArgumentNullException(nameof(dateeName));
            InterestBefore = interestBefore;
            InterestAfter = interestAfter;
            NewState = newState;
            DateePrompt = dateePrompt;
            ConversationHistory = conversationHistory;
            PlayerName = playerName;
        }
    }
}

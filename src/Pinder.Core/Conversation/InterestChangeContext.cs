namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Context passed to the LLM when interest crosses a threshold,
    /// used to generate a narrative beat.
    /// </summary>
    public sealed class InterestChangeContext
    {
        /// <summary>The opponent character's name.</summary>
        public string OpponentName { get; }

        /// <summary>Interest value before the change.</summary>
        public int InterestBefore { get; }

        /// <summary>Interest value after the change.</summary>
        public int InterestAfter { get; }

        /// <summary>The new interest state after the change.</summary>
        public InterestState NewState { get; }

        /// <summary>
        /// The opponent's assembled system prompt, used to generate
        /// interest change beats in the opponent's voice/character.
        /// </summary>
        public string? OpponentPrompt { get; }

        /// <summary>
        /// Recent conversation history — passed so the beat can reference specific details.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<(string Sender, string Text)>? ConversationHistory { get; }

        /// <summary>The player character's display name (used to label history entries).</summary>
        public string? PlayerName { get; }

        public InterestChangeContext(
            string opponentName,
            int interestBefore,
            int interestAfter,
            InterestState newState,
            string? opponentPrompt = null,
            System.Collections.Generic.IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string? playerName = null)
        {
            OpponentName = opponentName ?? throw new System.ArgumentNullException(nameof(opponentName));
            InterestBefore = interestBefore;
            InterestAfter = interestAfter;
            NewState = newState;
            OpponentPrompt = opponentPrompt;
            ConversationHistory = conversationHistory;
            PlayerName = playerName;
        }
    }
}

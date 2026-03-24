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

        public InterestChangeContext(
            string opponentName,
            int interestBefore,
            int interestAfter,
            InterestState newState)
        {
            OpponentName = opponentName ?? throw new System.ArgumentNullException(nameof(opponentName));
            InterestBefore = interestBefore;
            InterestAfter = interestAfter;
            NewState = newState;
        }
    }
}

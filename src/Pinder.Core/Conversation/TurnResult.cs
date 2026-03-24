using Pinder.Core.Rolls;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of resolving a turn: roll outcome, messages, and updated game state.
    /// </summary>
    public sealed class TurnResult
    {
        /// <summary>The full roll result.</summary>
        public RollResult Roll { get; }

        /// <summary>The player's message text after degradation.</summary>
        public string DeliveredMessage { get; }

        /// <summary>The opponent's response message.</summary>
        public string OpponentMessage { get; }

        /// <summary>Narrative beat text if an interest threshold was crossed, null otherwise.</summary>
        public string? NarrativeBeat { get; }

        /// <summary>Net interest delta applied this turn (includes momentum).</summary>
        public int InterestDelta { get; }

        /// <summary>Snapshot of game state after this turn.</summary>
        public GameStateSnapshot StateAfter { get; }

        /// <summary>True if the game ended this turn.</summary>
        public bool IsGameOver { get; }

        /// <summary>The outcome if the game ended, null otherwise.</summary>
        public GameOutcome? Outcome { get; }

        public TurnResult(
            RollResult roll,
            string deliveredMessage,
            string opponentMessage,
            string? narrativeBeat,
            int interestDelta,
            GameStateSnapshot stateAfter,
            bool isGameOver,
            GameOutcome? outcome)
        {
            Roll = roll ?? throw new System.ArgumentNullException(nameof(roll));
            DeliveredMessage = deliveredMessage ?? throw new System.ArgumentNullException(nameof(deliveredMessage));
            OpponentMessage = opponentMessage ?? throw new System.ArgumentNullException(nameof(opponentMessage));
            NarrativeBeat = narrativeBeat;
            InterestDelta = interestDelta;
            StateAfter = stateAfter ?? throw new System.ArgumentNullException(nameof(stateAfter));
            IsGameOver = isGameOver;
            Outcome = outcome;
        }
    }
}

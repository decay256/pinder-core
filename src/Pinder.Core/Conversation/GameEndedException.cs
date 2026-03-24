using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Thrown when an operation is attempted on a GameSession that has already ended.
    /// </summary>
    public sealed class GameEndedException : InvalidOperationException
    {
        /// <summary>The outcome that ended the game.</summary>
        public GameOutcome Outcome { get; }

        public GameEndedException(GameOutcome outcome)
            : base($"Game has ended with outcome: {outcome}")
        {
            Outcome = outcome;
        }

        public GameEndedException(GameOutcome outcome, string message)
            : base(message)
        {
            Outcome = outcome;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Thrown when an operation is attempted on a GameSession that has already ended.
    /// </summary>
    public sealed class GameEndedException : InvalidOperationException
    {
        /// <summary>The outcome that ended the game.</summary>
        public GameOutcome Outcome { get; }

        /// <summary>Shadow growth events that occurred when the game ended (e.g., ghost Dread +1). Empty if none.</summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        public GameEndedException(GameOutcome outcome)
            : base($"Game has ended with outcome: {outcome}")
        {
            Outcome = outcome;
            ShadowGrowthEvents = Array.Empty<string>();
        }

        public GameEndedException(GameOutcome outcome, string message)
            : base(message)
        {
            Outcome = outcome;
            ShadowGrowthEvents = Array.Empty<string>();
        }

        public GameEndedException(GameOutcome outcome, IReadOnlyList<string> shadowGrowthEvents)
            : base($"Game has ended with outcome: {outcome}")
        {
            Outcome = outcome;
            ShadowGrowthEvents = shadowGrowthEvents ?? Array.Empty<string>();
        }
    }
}

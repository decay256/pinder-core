using System;
using System.Collections.Generic;
using Pinder.Core.Rolls;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a Read action: SA vs DC 12 to reveal opponent interest.
    /// </summary>
    public sealed class ReadResult
    {
        /// <summary>True if the SA roll met or exceeded DC 12.</summary>
        public bool Success { get; }

        /// <summary>Current interest value. Non-null only on success; null on failure.</summary>
        public int? InterestValue { get; }

        /// <summary>The roll result for transparency/logging.</summary>
        public RollResult Roll { get; }

        /// <summary>Snapshot of game state after the action resolved.</summary>
        public GameStateSnapshot StateAfter { get; }

        /// <summary>XP earned from this action: 5 on success, 2 on failure.</summary>
        public int XpEarned { get; }

        /// <summary>
        /// Shadow growth events that occurred.
        /// Contains "Overthinking +1 (Read failed)" on failure when SessionShadowTracker is available.
        /// Empty list on success or when no tracker is configured.
        /// </summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        public ReadResult(
            bool success,
            int? interestValue,
            RollResult roll,
            GameStateSnapshot stateAfter,
            int xpEarned,
            IReadOnlyList<string> shadowGrowthEvents)
        {
            Success = success;
            InterestValue = interestValue;
            Roll = roll ?? throw new ArgumentNullException(nameof(roll));
            StateAfter = stateAfter ?? throw new ArgumentNullException(nameof(stateAfter));
            XpEarned = xpEarned;
            ShadowGrowthEvents = shadowGrowthEvents ?? Array.Empty<string>();
        }
    }
}

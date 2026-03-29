using System;
using Pinder.Core.Rolls;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a Recover action: SA vs DC 12 to clear an active trap.
    /// </summary>
    public sealed class RecoverResult
    {
        /// <summary>True if the SA roll met or exceeded DC 12.</summary>
        public bool Success { get; }

        /// <summary>The ID/name of the cleared trap. Non-null only on success; null on failure.</summary>
        public string? ClearedTrapName { get; }

        /// <summary>The roll result for transparency/logging.</summary>
        public RollResult Roll { get; }

        /// <summary>Snapshot of game state after the action resolved.</summary>
        public GameStateSnapshot StateAfter { get; }

        /// <summary>XP earned from this action: 15 on recovery success, 2 on failure.</summary>
        public int XpEarned { get; }

        public RecoverResult(
            bool success,
            string? clearedTrapName,
            RollResult roll,
            GameStateSnapshot stateAfter,
            int xpEarned)
        {
            Success = success;
            ClearedTrapName = clearedTrapName;
            Roll = roll ?? throw new ArgumentNullException(nameof(roll));
            StateAfter = stateAfter ?? throw new ArgumentNullException(nameof(stateAfter));
            XpEarned = xpEarned;
        }
    }
}

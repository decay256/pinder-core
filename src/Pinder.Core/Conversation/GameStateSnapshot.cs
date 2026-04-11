namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Immutable snapshot of the game state at a point in time.
    /// </summary>
    public sealed class GameStateSnapshot
    {
        /// <summary>Current interest meter value.</summary>
        public int Interest { get; }

        /// <summary>Current interest state derived from the meter value.</summary>
        public InterestState State { get; }

        /// <summary>Current momentum streak (consecutive successes).</summary>
        public int MomentumStreak { get; }

        /// <summary>Names of all currently active traps.</summary>
        public string[] ActiveTrapNames { get; }

        /// <summary>Detailed info for each active trap (stat, duration, penalty).</summary>
        public TrapDetail[] ActiveTrapDetails { get; }

        /// <summary>Current turn number (0-based before first turn).</summary>
        public int TurnNumber { get; }

        /// <summary>True if The Triple bonus is active for the current turn (+1 to all rolls).</summary>
        public bool TripleBonusActive { get; }

        public GameStateSnapshot(
            int interest,
            InterestState state,
            int momentumStreak,
            string[] activeTrapNames,
            int turnNumber,
            bool tripleBonusActive = false,
            TrapDetail[] activeTrapDetails = null)
        {
            Interest = interest;
            State = state;
            MomentumStreak = momentumStreak;
            ActiveTrapNames = activeTrapNames ?? System.Array.Empty<string>();
            ActiveTrapDetails = activeTrapDetails ?? System.Array.Empty<TrapDetail>();
            TurnNumber = turnNumber;
            TripleBonusActive = tripleBonusActive;
        }
    }
}

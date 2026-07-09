using System.Collections.Generic;

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

        /// <summary>True if The Triple bonus is active for the current turn (+2 to all rolls).</summary>
        public bool TripleBonusActive { get; }

        /// <summary>
        /// #905: Probability (0.0..1.0) that the datee will ghost on this turn.
        /// Derived from interest state: 0.25 when Bored, 0.0 otherwise. Hosts can
        /// surface this value without duplicating interest thresholds.
        /// </summary>
        public double GhostProbabilityPerTurn { get; }

        /// <summary>
        /// #788: snapshot of the engine-owned datee LLM conversation history at
        /// the time the snapshot was taken. Each entry's role is <c>"user"</c>
        /// or <c>"assistant"</c>. Always non-null: empty list when no datee calls
        /// have resolved yet.
        /// </summary>
        public IReadOnlyList<ConversationMessage> DateeHistory { get; }

        /// <summary>
        /// #1123: snapshot of the engine-owned avatar LLM conversation history
        /// at the time the snapshot was taken: the symmetric sibling of
        /// <see cref="DateeHistory"/>. Each entry's role is <c>"user"</c> or
        /// <c>"assistant"</c>. Always non-null: empty list when no avatar calls
        /// have resolved yet.
        /// </summary>
        public IReadOnlyList<ConversationMessage> AvatarHistory { get; }

        public IReadOnlyDictionary<string, int> ShadowValues { get; }

        public GameStateSnapshot(
            int interest,
            InterestState state,
            int momentumStreak,
            string[] activeTrapNames,
            int turnNumber,
            bool tripleBonusActive = false,
            TrapDetail[] activeTrapDetails = null,
            IReadOnlyList<ConversationMessage> dateeHistory = null,
            double ghostProbabilityPerTurn = 0.0,
            IReadOnlyList<ConversationMessage> avatarHistory = null,
            IReadOnlyDictionary<string, int> shadowValues = null)
        {
            Interest = interest;
            State = state;
            MomentumStreak = momentumStreak;
            ActiveTrapNames = activeTrapNames ?? System.Array.Empty<string>();
            ActiveTrapDetails = activeTrapDetails ?? System.Array.Empty<TrapDetail>();
            TurnNumber = turnNumber;
            TripleBonusActive = tripleBonusActive;
            DateeHistory = dateeHistory ?? System.Array.Empty<ConversationMessage>();
            AvatarHistory = avatarHistory ?? System.Array.Empty<ConversationMessage>();
            GhostProbabilityPerTurn = ghostProbabilityPerTurn;
            ShadowValues = shadowValues ?? new Dictionary<string, int>();
        }
    }
}

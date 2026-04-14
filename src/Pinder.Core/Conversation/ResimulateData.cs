using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Carries all the mid-session state needed to restore a GameSession for resimulation.
    /// Populated from a TurnSnapshot by the session runner. All fields use plain types
    /// so that Pinder.Core does not reference Pinder.SessionRunner.
    /// </summary>
    public sealed class ResimulateData
    {
        /// <summary>Interest to restore (absolute value, not a delta).</summary>
        public int TargetInterest { get; set; }

        /// <summary>Turn number at the time of the snapshot.</summary>
        public int TurnNumber { get; set; }

        /// <summary>Momentum streak to restore.</summary>
        public int MomentumStreak { get; set; }

        /// <summary>
        /// Effective shadow values by shadow stat name.
        /// Key = ShadowStatType.ToString(), Value = effective total (base + in-session growth).
        /// </summary>
        public Dictionary<string, int> ShadowValues { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Active traps: (stat name as StatType.ToString(), turns remaining).
        /// </summary>
        public List<(string TrapStat, int TurnsRemaining)> ActiveTraps { get; set; } = new List<(string, int)>();

        /// <summary>Full conversation history: (sender, text) pairs in chronological order.</summary>
        public List<(string Sender, string Text)> ConversationHistory { get; set; } = new List<(string, string)>();

        /// <summary>
        /// Combo history window (last up-to-3 turns): (stat name as StatType.ToString(), succeeded).
        /// </summary>
        public List<(string StatName, bool Succeeded)> ComboHistory { get; set; } = new List<(string, bool)>();

        /// <summary>Whether The Triple bonus is pending for the next roll.</summary>
        public bool PendingTripleBonus { get; set; }

        /// <summary>Cumulative Rizz failure count for Despair shadow tracking.</summary>
        public int RizzCumulativeFailureCount { get; set; }
    }
}

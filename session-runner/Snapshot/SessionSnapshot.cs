using System.Collections.Generic;

namespace Pinder.SessionRunner.Snapshot
{
    /// <summary>
    /// Snapshot of the full session setup written once before turn 1.
    /// Captures everything needed to reconstruct the starting conditions
    /// of a playtest session for replay/resimulation.
    /// </summary>
    public sealed class InitialSessionSnapshot
    {
        // ── Character snapshots ───────────────────────────────────────────
        public CharacterSnapshot Player { get; set; } = null!;
        public CharacterSnapshot Opponent { get; set; } = null!;

        // ── Session config ────────────────────────────────────────────────
        public int SessionHorniness { get; set; }
        public int HorninessRoll { get; set; }
        public int HorninessTimeModifier { get; set; }
        public int StartingInterest { get; set; }
        public int MaxTurns { get; set; }
        public string ModelSpec { get; set; } = string.Empty;
        public string SessionStartedAt { get; set; } = string.Empty;

        // ── Psychological stakes (save so reruns don't regenerate) ────────
        public string PlayerPsychologicalStake { get; set; } = string.Empty;
        public string OpponentPsychologicalStake { get; set; } = string.Empty;

        // ── Game definition values at time of run ─────────────────────────
        public int GlobalDcBias { get; set; }
        public int MaxDialogueOptions { get; set; }
    }

    /// <summary>
    /// Character data captured at session start.
    /// </summary>
    public sealed class CharacterSnapshot
    {
        public string DisplayName { get; set; } = string.Empty;
        public int Level { get; set; }
        public int LevelBonus { get; set; }
        public Dictionary<string, int> Stats { get; set; } = new Dictionary<string, int>();  // stat name → value
        public string Bio { get; set; } = string.Empty;
        public string AssembledSystemPrompt { get; set; } = string.Empty;
        public string[] EquippedItems { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>
    /// Snapshot of tracking state written after each ResolveTurnAsync call.
    /// Contains everything needed to reconstruct mid-session state.
    /// </summary>
    public sealed class TurnSnapshot
    {
        public int TurnNumber { get; set; }
        public int Interest { get; set; }

        /// <summary>Shadow stat values at end of this turn. Key = ShadowStatType name.</summary>
        public Dictionary<string, int> ShadowValues { get; set; } = new Dictionary<string, int>();

        public int MomentumStreak { get; set; }
        public List<TrapSnapshot> ActiveTraps { get; set; } = new List<TrapSnapshot>();
        public TellSnapshot? ActiveTell { get; set; }

        /// <summary>Last up to 3 turns of combo-relevant history (stat + success).</summary>
        public List<TurnHistoryEntry> ComboHistory { get; set; } = new List<TurnHistoryEntry>();

        public bool PendingTripleBonus { get; set; }

        /// <summary>Ordered list of stat names used (one per turn, same index as turn number).</summary>
        public List<string> StatsUsedHistory { get; set; } = new List<string>();

        /// <summary>Parallel to StatsUsedHistory: was that turn's pick the highest-% option?</summary>
        public List<bool> HighestPctHistory { get; set; } = new List<bool>();

        public int CharmUsageCount { get; set; }
        public bool CharmMadnessTriggered { get; set; }
        public int SaUsageCount { get; set; }
        public bool SaOverthinkingTriggered { get; set; }
        public int RizzCumulativeFailureCount { get; set; }

        /// <summary>Full conversation history up to and including this turn.</summary>
        public List<ConversationEntry> ConversationHistory { get; set; } = new List<ConversationEntry>();
    }

    /// <summary>Active trap state at the time of snapshot.</summary>
    public sealed class TrapSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string Stat { get; set; } = string.Empty;
        public int TurnsRemaining { get; set; }
    }

    /// <summary>Active tell at the time of snapshot, if any.</summary>
    public sealed class TellSnapshot
    {
        public string Stat { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>One entry in the combo history window (last 3 turns).</summary>
    public sealed class TurnHistoryEntry
    {
        public string Stat { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
    }

    /// <summary>A single message in the conversation history.</summary>
    public sealed class ConversationEntry
    {
        public string Sender { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}

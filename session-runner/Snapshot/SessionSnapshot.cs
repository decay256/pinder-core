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
    ///
    /// <para>
    /// Fields covered (per AGENTS.md schema-discipline rule — every
    /// player-visible <c>GameSession</c> field MUST appear here):
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>TurnNumber</c>, <c>Interest</c></description></item>
    ///   <item><description><c>ShadowValues</c> (per <c>ShadowStatType</c>)</description></item>
    ///   <item><description><c>MomentumStreak</c>, <c>PendingTripleBonus</c></description></item>
    ///   <item><description><c>ActiveTraps</c>, <c>ActiveTell</c></description></item>
    ///   <item><description><c>ComboHistory</c> (last 3 turns of stat + success)</description></item>
    ///   <item><description><c>StatsUsedHistory</c>, <c>HighestPctHistory</c></description></item>
    ///   <item><description><c>CharmUsageCount</c>, <c>CharmMadnessTriggered</c></description></item>
    ///   <item><description><c>SaUsageCount</c>, <c>SaOverthinkingTriggered</c></description></item>
    ///   <item><description><c>RizzCumulativeFailureCount</c></description></item>
    ///   <item><description>
    ///     <c>ConversationHistory</c> (per-entry sender + text + per-layer text_diffs[]
    ///     — issue #305). Includes the turn-0 scene-setting entries seeded by
    ///     <c>GameSession.SeedSceneEntries</c> (issue #333): three entries with
    ///     sender == <c>"[scene]"</c> for player bio, opponent bio, and the
    ///     LLM-generated outfit description. The <c>callback_strip</c> layer
    ///     (issue #339) appears as one more entry in <c>TextDiffs</c> when
    ///     same-turn callback phrases were stripped from the delivered message.
    ///   </description></item>
    ///   <item><description>
    ///     <c>OpponentHistory</c> (issue #788): engine-owned opponent-LLM
    ///     conversation history. Each entry carries <c>Role</c>
    ///     (<c>"user"</c> or <c>"assistant"</c>) and <c>Content</c>. Survives
    ///     snapshot/restore so a replayed session can reproduce the same
    ///     multi-turn opponent context the original session ran with. Empty
    ///     list when no opponent calls have resolved yet.
    ///   </description></item>
    /// </list>
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

        /// <summary>
        /// Full conversation history up to and including this turn.
        /// Per #305 each entry now carries optional <see cref="ConversationEntry.TextDiffs"/>
        /// for the player's delivered message (Misfire / Steering /
        /// Horniness / Shadow layers); opponent entries leave it empty.
        /// </summary>
        public List<ConversationEntry> ConversationHistory { get; set; } = new List<ConversationEntry>();

        /// <summary>
        /// Issue #788: engine-owned opponent-LLM conversation history at the
        /// time of snapshot. Each entry's <see cref="OpponentHistoryEntry.Role"/>
        /// is <c>"user"</c> or <c>"assistant"</c>. Empty when no opponent calls
        /// have resolved yet.
        /// </summary>
        public List<OpponentHistoryEntry> OpponentHistory { get; set; } = new List<OpponentHistoryEntry>();
    }

    /// <summary>
    /// Issue #788: one entry of opponent-LLM conversation history. <c>Role</c>
    /// is the OpenAI/Anthropic wire role (<c>"user"</c> or <c>"assistant"</c>);
    /// <c>Content</c> is the raw text content.
    /// </summary>
    public sealed class OpponentHistoryEntry
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
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

        /// <summary>
        /// Issue #305: per-layer word-level diffs for the player's
        /// delivered message on this turn (Misfire / Steering / Horniness
        /// / Shadow). Empty list for opponent entries and for player
        /// entries with no recorded transformations. Mirrors the wire
        /// <c>TextDiffDto</c> shape so the snapshot can be deserialised
        /// straight into a renderer / replay tool.
        /// </summary>
        public List<TextDiffSnapshot> TextDiffs { get; set; } = new List<TextDiffSnapshot>();
    }

    /// <summary>
    /// Issue #305: snapshot shape for one text-transform layer's
    /// word-level diff. Layer name + before/after strings + per-token
    /// spans (Keep / Remove / Add).
    /// </summary>
    public sealed class TextDiffSnapshot
    {
        public string Layer { get; set; } = string.Empty;
        public string Before { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
        public List<TextDiffSpanSnapshot> Spans { get; set; } = new List<TextDiffSpanSnapshot>();
    }

    /// <summary>
    /// Issue #305: one word-level token span inside a
    /// <see cref="TextDiffSnapshot"/>. <see cref="Type"/> is one of
    /// <c>Keep</c> / <c>Remove</c> / <c>Add</c> (mirrors
    /// <c>Pinder.Core.Text.DiffSpanType</c>).
    /// </summary>
    public sealed class TextDiffSpanSnapshot
    {
        public string Type { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}

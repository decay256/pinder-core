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
    ///     <c>Events</c> (issue #474, deferred from i18n Phase 1.5
    ///     #436): per-turn list of <see cref="EventSnapshot"/> entries
    ///     covering <c>combo_hit</c>, <c>tell_read</c>, <c>callback_hit</c>,
    ///     <c>nat_20</c>, <c>nat_1</c>, <c>miss_by_N</c>,
    ///     <c>horniness_fail</c>, <c>shadow_tick_*</c>, <c>trap_activated</c>.
    ///     Each entry carries the canonical kind, the turn number, and the
    ///     human-readable interpretation string picked deterministically
    ///     (FNV-1a-32 over <c>(kind, turn_number)</c>) from the
    ///     <c>data/i18n/&lt;locale&gt;/events.yaml</c> catalog — byte-for-byte
    ///     identical to the frontend's variantIndex, so engine-emit and
    ///     web-render agree.
    ///   </description></item>
    ///   <item><description>
    ///     <c>OpponentHistory</c> (issue #788): engine-owned opponent-LLM
    ///     conversation history. Each entry carries <c>Role</c>
    ///     (<c>"user"</c> or <c>"assistant"</c>) and <c>Content</c>. Survives
    ///     snapshot/restore so a replayed session can reproduce the same
    ///     multi-turn opponent context the original session ran with. Empty
    ///     list when no opponent calls have resolved yet.
    ///   </description></item>
    ///   <item><description>
    ///     <c>DefendingRollStat</c> (issue #906): the defending stat used in
    ///     the option roll on this turn (<c>StatBlock.DefenceTable[Stat]</c>).
    ///     Empty string on turns with no roll.
    ///   </description></item>
    ///   <item><description>
    ///     <c>GhostProbabilityPerTurn</c> (issue #905): probability (0.0..1.0)
    ///     that the opponent ghosts on this turn. Derived from
    ///     <c>GameStateSnapshot.GhostProbabilityPerTurn</c> (0.25 when Bored,
    ///     0.0 otherwise). Added explicitly since <c>TurnSnapshot</c> does
    ///     not inline <c>GameStateSnapshot</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>OpponentDefenseSnapshot</c> (issue #903): the opponent's defense
    ///     posture at the start of this turn. Dictionary keyed on attacker stat
    ///     name (PascalCase), each entry carrying <c>DefendingStat</c>,
    ///     <c>EffectiveModifier</c>, and <c>BaseModifier</c>. Null when not
    ///     available (legacy snapshots before #903).
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
        /// Issue #906: defending stat used in the roll for this turn.
        /// Derived from <c>StatBlock.DefenceTable[Stat]</c>. Empty string on turns
        /// with no roll (ghosted / skipped). Populated in <c>BuildTurnSnapshot</c>.
        /// </summary>
        public string DefendingRollStat { get; set; } = string.Empty;

        /// <summary>
        /// Issue #905: probability (0.0..1.0) that the opponent will ghost on
        /// this turn. 0.25 when the session's interest state is Bored, 0.0
        /// otherwise. Copied from <see cref="Pinder.Core.Conversation.GameStateSnapshot.GhostProbabilityPerTurn"/>.
        /// </summary>
        public double GhostProbabilityPerTurn { get; set; }

        /// <summary>
        /// Issue #903: opponent defense posture at the start of this turn.
        /// Dictionary keyed on attacker stat name (PascalCase). Each value
        /// carries <c>DefendingStat</c>, <c>EffectiveModifier</c> (shadow-
        /// adjusted + trap DC bonus), and <c>BaseModifier</c> (raw base stat).
        /// Null on legacy snapshots taken before #903.
        /// </summary>
        public Dictionary<string, TurnDefenseEntry>? OpponentDefenseSnapshot { get; set; }

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

        /// <summary>
        /// Issue #474: events fired on this turn, with their deterministic
        /// human-readable interpretation strings from the i18n catalog.
        /// Empty list when no event-class condition was met (a fully
        /// neutral turn). Order is the engine's emission order:
        ///
        /// <list type="number">
        ///   <item><description>roll-class first (nat_20 / nat_1 / miss_by_N)</description></item>
        ///   <item><description>combo / tell / callback bonuses</description></item>
        ///   <item><description>horniness_fail</description></item>
        ///   <item><description>shadow_tick_* (one per shadow that grew)</description></item>
        ///   <item><description>trap_activated</description></item>
        /// </list>
        ///
        /// <para>Pre-#474 snapshots persist without this field; replay
        /// tooling treats it as optional. No data migration needed.</para>
        /// </summary>
        public List<EventSnapshot> Events { get; set; } = new List<EventSnapshot>();
    }

    /// <summary>
    /// Issue #474: one event fired on a turn, with its deterministic
    /// human-readable interpretation. <see cref="Kind"/> is the canonical
    /// event kind (<c>combo_hit</c>, <c>nat_20</c>, etc. — the keys in
    /// <c>data/i18n/&lt;locale&gt;/events.yaml</c>);
    /// <see cref="EventInterpretation"/> is the chosen variant string,
    /// picked deterministically by
    /// <see cref="Pinder.Core.I18n.VariantPicker.PickIndex"/> on
    /// <c>(Kind, TurnNumber)</c>. Empty interpretation when the catalog
    /// has no entry for the kind (defensive fallback for forward
    /// compatibility with new event kinds added engine-side before yaml).
    /// </summary>
    public sealed class EventSnapshot
    {
        public string Kind { get; set; } = string.Empty;
        public int TurnNumber { get; set; }
        public string EventInterpretation { get; set; } = string.Empty;
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

    /// <summary>
    /// Issue #903: one entry in the <see cref="TurnSnapshot.OpponentDefenseSnapshot"/> dictionary.
    /// </summary>
    public sealed class TurnDefenseEntry
    {
        public string DefendingStat      { get; set; } = string.Empty;
        public int    EffectiveModifier  { get; set; }
        public int    BaseModifier       { get; set; }
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

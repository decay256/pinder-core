using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Sealed class wrapping assembled character data needed at runtime.
    /// Produced by the character assembly pipeline and consumed by GameSession.
    /// </summary>
    public sealed class CharacterProfile
    {
        /// <summary>The character's stat block for roll resolution.</summary>
        public StatBlock Stats { get; }

        /// <summary>The fully assembled system prompt for LLM interactions.</summary>
        public string AssembledSystemPrompt { get; private set; }

        /// <summary>
        /// The base system prompt before psychological stake was appended.
        /// Used when building opponent context for the player agent — the player
        /// should not see the opponent's generated stake as prior knowledge.
        /// </summary>
        public string BaseSystemPrompt { get; private set; }

        /// <summary>Display name shown in conversation history.</summary>
        public string DisplayName { get; }

        /// <summary>Timing profile for reply delay computation.</summary>
        public TimingProfile Timing { get; }

        /// <summary>Character level (1-based) for level bonus in rolls.</summary>
        public int Level { get; }

        /// <summary>The character's one-liner bio shown on their profile.</summary>
        public string Bio { get; }

        /// <summary>
        /// Issue #562: self-reported gender identity (e.g. "she/her",
        /// "they/them"). Sourced from the <c>gender_identity</c> field in
        /// the character JSON. Surfaced in the
        /// <see cref="OpponentVisibleProfile"/> as a Tinder-card-equivalent
        /// field. Empty when not set on the character file.
        /// </summary>
        public string GenderIdentity { get; }

        /// <summary>
        /// The texting style fragment(s) joined, for injection into
        /// option-generation prompts. Empty string if not available.
        /// </summary>
        public string TextingStyleFragment { get; }

        /// <summary>
        /// Issue #781: the final aggregated texting-style axis lines as
        /// computed by <c>TextingStyleAggregator.AggregateWithAudit</c>.
        /// Each line has the shape <c>&quot;axis: rule&quot;</c>
        /// (e.g. <c>"emoji: ends every sentence with an emoji"</c>).
        /// Lines are in canonical axis order (emoji, shorthand, grammar,
        /// structure, length, tics, stance, register, pacing); missing
        /// axes are dropped rather than back-filled.
        /// Empty when the aggregator produced no output (no items / anatomy
        /// configured for this character).
        /// </summary>
        public IReadOnlyList<string> TextingStyleLines { get; }

        /// <summary>
        /// Issue #1067: final aggregated texting-style axis lines with source attribution as computed by
        /// <c>TextingStyleAggregator.AggregateWithAudit</c>.
        /// </summary>
        public IReadOnlyList<Prompts.TextingStyleAggregator.AttributedTextingStyleLine> AttributedTextingStyleLines { get; }

        /// <summary>
        /// Issue #404: per-source breakdown of the texting-style fragments
        /// that were joined into <see cref="TextingStyleFragment"/>. Each
        /// entry pairs <c>(kind, source, fragment)</c>: kind is
        /// <c>"item"</c> or <c>"anatomy"</c>, source is the item display
        /// name or anatomy tier name, fragment is the contributed string.
        /// Used by the Character Sheet 'Texting Style' tab. Items appear
        /// before anatomy tiers — same injection order the assembler uses.
        /// </summary>
        public IReadOnlyList<TextingStyleFragmentSource> TextingStyleSources { get; }

        /// <summary>
        /// The character's active archetype, or null if none resolved.
        /// Carries name, behavior directive, and interference level.
        /// </summary>
        public ActiveArchetype ActiveArchetype { get; }

        /// <summary>
        /// Issue #779: permanent psychological stake loaded from the character JSON.
        /// Populated from the on-disk <c>psychological_stake</c> field at load time.
        /// Kept settable so legacy test paths (and the admin regenerate endpoint)
        /// can still overwrite it without a new constructor signature.
        /// </summary>
        public string? PsychologicalStake { get; set; }

        /// <summary>
        /// Display names of equipped items, in slot order.
        /// Used to build the visible profile shown to the opposing player at T1.
        /// </summary>
        public IReadOnlyList<string> EquippedItemDisplayNames { get; }

        public IReadOnlyDictionary<string, BackstoryFact>? Backstory { get; }
        public IReadOnlyList<string>? StakeLines { get; }
        public IReadOnlyDictionary<string, string>? PsychiatricDiagnosis { get; }
        public IReadOnlyList<string>? BackstoryFragments { get; }

        /// <summary>Appends additional text to the assembled system prompt.</summary>
        public void AppendToSystemPrompt(string text)
        {
            if (!string.IsNullOrEmpty(text))
                AssembledSystemPrompt += text;
        }

        /// <summary>Overwrites the assembled system prompt with a new value.</summary>
        public void UpdateSystemPrompt(string newPrompt)
        {
            AssembledSystemPrompt = newPrompt ?? "";
        }

        /// <summary>
        /// Freezes BaseSystemPrompt to the current AssembledSystemPrompt value.
        /// Call this immediately before appending the psychological stake so the
        /// base prompt remains clean for opponent profile injection.
        /// </summary>
        public void FreezeBasePrompt()
        {
            BaseSystemPrompt = AssembledSystemPrompt;
        }

        public CharacterProfile(
            StatBlock stats,
            string assembledSystemPrompt,
            string displayName,
            TimingProfile timing,
            int level,
            string bio = "",
            string textingStyleFragment = "",
            ActiveArchetype activeArchetype = null,
            IReadOnlyList<string> equippedItemDisplayNames = null,
            IReadOnlyList<TextingStyleFragmentSource> textingStyleSources = null,
            string genderIdentity = "",
            IReadOnlyList<string> textingStyleLines = null,
            IReadOnlyDictionary<string, BackstoryFact>? backstory = null,
            IReadOnlyList<string>? stakeLines = null,
            IReadOnlyDictionary<string, string>? psychiatricDiagnosis = null,
            IReadOnlyList<string>? backstoryFragments = null,
            IReadOnlyList<Prompts.TextingStyleAggregator.AttributedTextingStyleLine> attributedTextingStyleLines = null)
        {
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            AssembledSystemPrompt = assembledSystemPrompt ?? throw new ArgumentNullException(nameof(assembledSystemPrompt));
            BaseSystemPrompt = AssembledSystemPrompt;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Level = level;
            Bio = bio ?? string.Empty;
            TextingStyleFragment = textingStyleFragment ?? string.Empty;
            ActiveArchetype = activeArchetype;
            EquippedItemDisplayNames = equippedItemDisplayNames ?? new System.Collections.Generic.List<string>();
            TextingStyleSources = textingStyleSources ?? new System.Collections.Generic.List<TextingStyleFragmentSource>();
            // #562: optional, defaults to "" so existing test fixtures
            // and unit-test ctors work without modification.
            GenderIdentity = genderIdentity ?? string.Empty;
            // #781: final aggregated texting-style lines. Defaults to empty
            // list so existing callers (test fixtures) don't need to pass it.
            TextingStyleLines = textingStyleLines ?? new System.Collections.Generic.List<string>();
            Backstory = backstory;
            StakeLines = stakeLines;
            PsychiatricDiagnosis = psychiatricDiagnosis;
            BackstoryFragments = backstoryFragments;
            AttributedTextingStyleLines = attributedTextingStyleLines ?? new System.Collections.Generic.List<Prompts.TextingStyleAggregator.AttributedTextingStyleLine>();
        }
    }
}

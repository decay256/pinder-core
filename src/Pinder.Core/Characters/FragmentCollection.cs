using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// The fully assembled output of the character-construction pipeline.
    /// Contains concatenated fragments, ranked archetypes, timing profile, and stat block.
    /// </summary>
    public sealed class FragmentCollection
    {
        /// <summary>All personality fragments from equipped items and anatomy tiers.</summary>
        public IReadOnlyList<string> PersonalityFragments   { get; }

        /// <summary>All backstory fragments from equipped items and anatomy tiers.</summary>
        public IReadOnlyList<string> BackstoryFragments     { get; }

        /// <summary>All texting style fragments from equipped items and anatomy tiers.</summary>
        public IReadOnlyList<string> TextingStyleFragments  { get; }

        /// <summary>
        /// Issue #404: per-source breakdown of texting-style fragments. Each
        /// entry corresponds 1:1 with an entry in <see cref="TextingStyleFragments"/>
        /// (same length, same order). Items appear before anatomy tiers — same
        /// injection order the assembler uses. Empty fragments are dropped
        /// from both lists. Used by the Character Sheet 'Texting Style' tab
        /// to render the per-source breakdown without re-deriving it from
        /// item / anatomy definitions on the controller side.
        /// </summary>
        public IReadOnlyList<TextingStyleFragmentSource> TextingStyleSources { get; }

        /// <summary>
        /// Archetypes sorted descending by occurrence count across all sources.
        /// Each entry is (archetype name, count).
        /// </summary>
        public IReadOnlyList<(string Archetype, int Count)> RankedArchetypes { get; }

        /// <summary>Assembled timing profile (summed from all sources).</summary>
        public TimingProfile Timing { get; }

        /// <summary>Effective stat block (base + item + anatomy modifiers, shadow applied).</summary>
        public StatBlock Stats { get; }

        /// <summary>
        /// The resolved active archetype for this character, or null if none could be determined.
        /// Selected based on character level and archetype frequency.
        /// </summary>
        public ActiveArchetype ActiveArchetype { get; }

        public FragmentCollection(
            IReadOnlyList<string> personalityFragments,
            IReadOnlyList<string> backstoryFragments,
            IReadOnlyList<string> textingStyleFragments,
            IReadOnlyList<(string Archetype, int Count)> rankedArchetypes,
            TimingProfile timing,
            StatBlock stats,
            ActiveArchetype activeArchetype = null,
            IReadOnlyList<TextingStyleFragmentSource> textingStyleSources = null)
        {
            PersonalityFragments  = personalityFragments;
            BackstoryFragments    = backstoryFragments;
            TextingStyleFragments = textingStyleFragments;
            RankedArchetypes      = rankedArchetypes;
            Timing                = timing;
            Stats                 = stats;
            ActiveArchetype       = activeArchetype;
            TextingStyleSources   = textingStyleSources ?? new List<TextingStyleFragmentSource>();
        }
    }

    /// <summary>
    /// Issue #404: one source-attributed entry of a texting-style fragment.
    /// Pairs with <see cref="FragmentCollection.TextingStyleSources"/>.
    /// </summary>
    public sealed class TextingStyleFragmentSource
    {
        /// <summary>
        /// One of <c>"item"</c> or <c>"anatomy"</c>. Encoded as a string
        /// (rather than enum) so the wire DTO can pass it through verbatim.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Display name of the source. For items, the item's <c>DisplayName</c>;
        /// for anatomy tiers, the tier's <c>TierName</c>.
        /// </summary>
        public string Source { get; }

        /// <summary>The fragment string this source contributed.</summary>
        public string Fragment { get; }

        public TextingStyleFragmentSource(string kind, string source, string fragment)
        {
            Kind = kind;
            Source = source;
            Fragment = fragment;
        }
    }
}

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
            ActiveArchetype activeArchetype = null)
        {
            PersonalityFragments  = personalityFragments;
            BackstoryFragments    = backstoryFragments;
            TextingStyleFragments = textingStyleFragments;
            RankedArchetypes      = rankedArchetypes;
            Timing                = timing;
            Stats                 = stats;
            ActiveArchetype       = activeArchetype;
        }
    }
}

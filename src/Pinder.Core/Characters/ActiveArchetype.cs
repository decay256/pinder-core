namespace Pinder.Core.Characters
{
    /// <summary>
    /// Represents the currently active archetype for a character, including
    /// its behavioral directive and interference level.
    /// </summary>
    public sealed class ActiveArchetype
    {
        /// <summary>Display name (e.g. "The Peacock").</summary>
        public string Name { get; }

        /// <summary>Behavioral instruction text from archetype definitions.</summary>
        public string Behavior { get; }

        /// <summary>How many times this archetype appeared in the character's fragments.</summary>
        public int Count { get; }

        /// <summary>
        /// Total number of archetype-tendency votes across the character's full
        /// build (every archetype, every fragment). Used to compute the
        /// share-of-votes-based <see cref="InterferenceLevel"/>.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="Count"/> when not supplied — that yields
        /// <c>ratio = 1.0</c> and the legacy "always dominant" behaviour. Real
        /// callers (CharacterAssembler) supply the real total so the intensity
        /// reflects how dominant the archetype actually is in the build, not
        /// just the absolute count of votes.
        /// </remarks>
        public int TotalCount { get; }

        /// <summary>
        /// Interference level derived from this archetype's share of all
        /// archetype-tendency votes in the build (#375):
        ///   ≥ 0.7 → "dominant"  (a clear majority — the archetype is the build)
        ///   ≥ 0.4 → "clear"     (a real lead but not crushing)
        ///   else  → "slight"    (one of several voices)
        ///
        /// "Dominant" therefore requires the archetype to take roughly
        /// 70%+ of the build's archetype votes, not just a single-vote
        /// lead. Per the #375 acceptance tests:
        ///   [Pun Troll: 2, Player: 1]      → 2/3 = 0.67 → "clear"
        ///   [Pun Troll: 4, Player: 1]      → 4/5 = 0.80 → "dominant"
        ///   [Pun Troll: 2, Player: 2]      → 2/4 = 0.50 → "clear" (tied)
        ///   [Pun Troll: 1, Player: 1, ...] → 1/3 = 0.33 → "slight"
        /// </summary>
        public string InterferenceLevel
        {
            get
            {
                int total = TotalCount > 0 ? TotalCount : Count;
                if (total <= 0) return "slight";
                double ratio = (double)Count / (double)total;
                if (ratio >= 0.7) return "dominant";
                if (ratio >= 0.4) return "clear";
                return "slight";
            }
        }

        /// <summary>
        /// Full directive string suitable for injection into LLM prompts.
        /// Format: "ACTIVE ARCHETYPE: {Name} ({interference})\n{behavior}"
        /// </summary>
        public string Directive
        {
            get
            {
                return $"ACTIVE ARCHETYPE: {Name} ({InterferenceLevel})\n{Behavior}";
            }
        }

        /// <summary>
        /// Construct an ActiveArchetype.
        /// </summary>
        /// <param name="name">Archetype display name.</param>
        /// <param name="behavior">Behavioral instruction text.</param>
        /// <param name="count">Vote count for this archetype.</param>
        /// <param name="totalCount">
        /// Total archetype-tendency vote count across the build. When omitted
        /// (zero or negative), <paramref name="count"/> is used — preserving
        /// the legacy "ratio = 1.0 = dominant" behaviour for callers that
        /// haven't migrated yet. CharacterAssembler.ResolveActiveArchetype
        /// always supplies the real total.
        /// </param>
        public ActiveArchetype(string name, string behavior, int count, int totalCount = 0)
        {
            Name = name ?? string.Empty;
            Behavior = behavior ?? string.Empty;
            Count = count;
            TotalCount = totalCount > 0 ? totalCount : count;
        }
    }
}

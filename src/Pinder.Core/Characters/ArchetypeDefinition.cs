namespace Pinder.Core.Characters
{
    /// <summary>
    /// Immutable definition of an archetype, including its tier and level range.
    /// Tiers reflect character progression stages:
    ///   Tier 1 = Levels 1–3 (low level)
    ///   Tier 2 = Levels 2–6 (early game)
    ///   Tier 3 = Levels 3–9 (mid game)
    ///   Tier 4 = Levels 5+  (high level)
    /// Tiers overlap by design — a level 5 character qualifies for tiers 2, 3, and 4.
    /// </summary>
    public sealed class ArchetypeDefinition
    {
        /// <summary>Display name (e.g. "The Sniper").</summary>
        public string Name { get; }

        /// <summary>Minimum character level (inclusive) for this archetype to be eligible.</summary>
        public int MinLevel { get; }

        /// <summary>Maximum character level (inclusive) for this archetype to be eligible.</summary>
        public int MaxLevel { get; }

        /// <summary>
        /// Archetype tier (1–4). Determines which character level brackets can produce
        /// this archetype as dominant. Sourced from archetypes-enriched.yaml.
        /// </summary>
        public int Tier { get; }

        public ArchetypeDefinition(string name, int minLevel, int maxLevel, int tier)
        {
            Name = name;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            Tier = tier;
        }

        /// <summary>
        /// Returns true if the given character level falls within this archetype's eligible range.
        /// </summary>
        public bool IsEligibleAtLevel(int characterLevel)
        {
            return characterLevel >= MinLevel && characterLevel <= MaxLevel;
        }
    }
}

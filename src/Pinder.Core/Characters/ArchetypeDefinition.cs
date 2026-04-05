namespace Pinder.Core.Characters
{
    /// <summary>
    /// Immutable definition of an archetype, including the level range
    /// in which it is eligible as a dominant archetype.
    /// </summary>
    public sealed class ArchetypeDefinition
    {
        /// <summary>Display name (e.g. "The Sniper").</summary>
        public string Name { get; }

        /// <summary>Minimum character level (inclusive) for this archetype to be eligible.</summary>
        public int MinLevel { get; }

        /// <summary>Maximum character level (inclusive) for this archetype to be eligible.</summary>
        public int MaxLevel { get; }

        public ArchetypeDefinition(string name, int minLevel, int maxLevel)
        {
            Name = name;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
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

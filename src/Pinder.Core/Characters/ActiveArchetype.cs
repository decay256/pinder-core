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
        /// Interference level derived from count: 1-2 = "slight", 3-5 = "clear", 6+ = "dominant".
        /// </summary>
        public string InterferenceLevel
        {
            get
            {
                if (Count >= 6) return "dominant";
                if (Count >= 3) return "clear";
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

        public ActiveArchetype(string name, string behavior, int count)
        {
            Name = name ?? string.Empty;
            Behavior = behavior ?? string.Empty;
            Count = count;
        }
    }
}

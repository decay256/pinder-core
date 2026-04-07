using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Static catalog of all archetype definitions with their level ranges.
    /// Sourced from rules/extracted/archetypes-enriched.yaml §3 summary table.
    /// </summary>
    public static class ArchetypeCatalog
    {
        private static readonly Dictionary<string, ArchetypeDefinition> _byName;

        static ArchetypeCatalog()
        {
            var defs = new[]
            {
                new ArchetypeDefinition("The Hey Opener",          1,  3),
                new ArchetypeDefinition("The DTF Opener",          1,  5),
                new ArchetypeDefinition("The One-Word Replier",    1,  5),
                new ArchetypeDefinition("The Wall of Text",        1,  5),
                new ArchetypeDefinition("The Copy-Paste Machine",  2,  5),
                new ArchetypeDefinition("The Pickup Line Spammer", 1,  6),
                new ArchetypeDefinition("The Exploding Nice Guy",  1,  6),
                new ArchetypeDefinition("The Oversharer",          2,  7),
                new ArchetypeDefinition("The Philosopher",         2,  7),
                new ArchetypeDefinition("The Instagram Recruiter", 2,  6),
                new ArchetypeDefinition("The Bot / Scammer",       1,  4),
                new ArchetypeDefinition("The Zombie",              3,  8),
                new ArchetypeDefinition("The Breadcrumber",        4,  9),
                new ArchetypeDefinition("The Love Bomber",         3,  9),
                new ArchetypeDefinition("The Peacock",             3,  8),
                new ArchetypeDefinition("The Slow Fader",          2,  8),
                new ArchetypeDefinition("The Ghost",               1, 10),
                new ArchetypeDefinition("The Player",              5, 10),
                new ArchetypeDefinition("The Sniper",              5, 11),
                new ArchetypeDefinition("The Bio Responder",       4, 11),
            };

            _byName = new Dictionary<string, ArchetypeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
                _byName[d.Name] = d;
        }

        /// <summary>
        /// Returns the archetype definition for the given name, or null if not found.
        /// Name comparison is case-insensitive.
        /// </summary>
        public static ArchetypeDefinition? GetByName(string name)
        {
            _byName.TryGetValue(name, out var def);
            return def;
        }

        /// <summary>
        /// Returns all known archetype definitions.
        /// </summary>
        public static IReadOnlyCollection<ArchetypeDefinition> All => _byName.Values;

        /// <summary>
        /// Returns true if the given archetype name is eligible at the specified level.
        /// Unknown archetypes are always considered eligible (no filtering applied).
        /// </summary>
        public static bool IsEligibleAtLevel(string archetypeName, int characterLevel)
        {
            var def = GetByName(archetypeName);
            if (def == null) return true; // unknown archetypes are not filtered
            return def.IsEligibleAtLevel(characterLevel);
        }

        /// <summary>
        /// Returns the behavioral instruction for the given archetype, or a
        /// placeholder if no behavior text is registered.
        /// </summary>
        public static string GetBehavior(string archetypeName)
        {
            if (_behaviors.TryGetValue(archetypeName, out var behavior))
                return behavior;
            return $"Follow {archetypeName} behavioral pattern.";
        }

        /// <summary>
        /// Register behavior text for an archetype (e.g. loaded from YAML).
        /// </summary>
        public static void RegisterBehavior(string archetypeName, string behavior)
        {
            if (!string.IsNullOrEmpty(archetypeName) && !string.IsNullOrEmpty(behavior))
                _behaviors[archetypeName] = behavior;
        }

        private static readonly Dictionary<string, string> _behaviors
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

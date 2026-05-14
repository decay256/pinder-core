using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Static catalog of all archetype definitions with their level ranges, tiers,
    /// and behavioral instruction text.
    ///
    /// Tier definitions (from archetypes-enriched.yaml):
    ///   Tier 1 — Low Level  (Levels 1–3)
    ///   Tier 2 — Early Game (Levels 2–6)
    ///   Tier 3 — Mid Game   (Levels 3–9)
    ///   Tier 4 — High Level (Levels 5+)
    ///
    /// Tiers overlap by design. A level-5 character qualifies for tiers 2, 3, and 4.
    /// Archetype selection filters by the character's eligible tiers and picks the
    /// highest-count archetype whose tier is in that eligible set.
    /// </summary>
    public static class ArchetypeCatalog
    {
        // ── Tier level boundaries ─────────────────────────────────────────────

        private const int Tier1Min = 1;  private const int Tier1Max = 3;
        private const int Tier2Min = 2;  private const int Tier2Max = 6;
        private const int Tier3Min = 3;  private const int Tier3Max = 9;
        private const int Tier4Min = 5;  // no upper bound

        // ── Archetype registry ────────────────────────────────────────────────

        private static readonly Dictionary<string, ArchetypeDefinition> _byName;

        static ArchetypeCatalog()
        {
            // (name, minLevel, maxLevel, tier)
            // Level ranges are kept from archetypes-enriched.yaml for backward
            // compatibility; tier drives dominant-archetype filtering.
            var defs = new[]
            {
                new ArchetypeDefinition("The Hey Opener",          1,  3,  1),
                new ArchetypeDefinition("The DTF Opener",          1,  5,  1),
                new ArchetypeDefinition("The One-Word Replier",    1,  5,  1),
                new ArchetypeDefinition("The Wall of Text",        1,  5,  2),
                new ArchetypeDefinition("The Copy-Paste Machine",  2,  5,  2),
                new ArchetypeDefinition("The Pickup Line Spammer", 1,  6,  2),
                new ArchetypeDefinition("The Exploding Nice Guy",  1,  6,  2),
                new ArchetypeDefinition("The Oversharer",          2,  7,  2),
                new ArchetypeDefinition("The Philosopher",         2,  7,  2),
                new ArchetypeDefinition("The Instagram Recruiter", 2,  6,  2),
                new ArchetypeDefinition("The Bot / Scammer",       1,  4,  3),
                new ArchetypeDefinition("The Zombie",              3,  8,  3),
                new ArchetypeDefinition("The Breadcrumber",        4,  9,  3),
                new ArchetypeDefinition("The Love Bomber",         3,  9,  3),
                new ArchetypeDefinition("The Peacock",             3,  8,  3),
                new ArchetypeDefinition("The Slow Fader",          2,  8,  4),
                new ArchetypeDefinition("The Ghost",               1, 10,  4),
                new ArchetypeDefinition("The Player",              5, 10,  4),
                new ArchetypeDefinition("The Sniper",              5, 11,  4),
                new ArchetypeDefinition("The Bio Responder",       4, 11,  4),
            };

            _byName = new Dictionary<string, ArchetypeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
                _byName[d.Name] = d;

            // Phase 5 of #871: archetype behavior text is no longer
            // embedded as const strings. It is sourced exclusively from
            // data/prompts/archetypes.yaml via ArchetypeYamlLoader.LoadFromPromptCatalog(),
            // wired at startup by PromptWiring.Wire().
        }

        // ── Public API ────────────────────────────────────────────────────────

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
        /// Returns the set of tiers (1–4) that a character qualifies for at the given level.
        /// Tiers overlap by design:
        ///   Tier 1: levels 1–3
        ///   Tier 2: levels 2–6
        ///   Tier 3: levels 3–9
        ///   Tier 4: levels 5+
        /// </summary>
        public static IReadOnlyList<int> GetCharacterTiers(int characterLevel)
        {
            var tiers = new List<int>(4);
            if (characterLevel >= Tier1Min && characterLevel <= Tier1Max) tiers.Add(1);
            if (characterLevel >= Tier2Min && characterLevel <= Tier2Max) tiers.Add(2);
            if (characterLevel >= Tier3Min && characterLevel <= Tier3Max) tiers.Add(3);
            if (characterLevel >= Tier4Min)                               tiers.Add(4);
            return tiers.AsReadOnly();
        }

        /// <summary>
        /// Returns true if the given archetype name is eligible for a character
        /// at <paramref name="characterLevel"/>, using tier-based filtering.
        ///
        /// An archetype is eligible when its tier is in the character's eligible
        /// tier set (see <see cref="GetCharacterTiers"/>).
        ///
        /// Unknown archetypes (not in catalog) are always considered eligible.
        /// When characterLevel is 0, no filtering is applied (backward-compatible).
        /// </summary>
        public static bool IsEligibleAtLevel(string archetypeName, int characterLevel)
        {
            if (characterLevel <= 0) return true; // no filtering when level unset

            var def = GetByName(archetypeName);
            if (def == null) return true; // unknown archetypes are not filtered

            var characterTiers = GetCharacterTiers(characterLevel);
            foreach (int t in characterTiers)
                if (t == def.Tier) return true;

            return false;
        }

        /// <summary>
        /// Resolver for behavior text loaded from
        /// <c>data/prompts/archetypes.yaml</c>. Wired at startup via
        /// <c>ArchetypeYamlLoader.LoadFromPromptCatalog()</c>, typically
        /// called from <c>PromptWiring.Wire()</c>. After Phase 5 of #871,
        /// this MUST be wired before calling <see cref="GetBehavior"/> —
        /// there are no embedded const fallbacks.
        /// </summary>
        /// <remarks>
        /// The delegate receives the archetype name (e.g. <c>"The Hey Opener"</c>)
        /// and returns the behavior string, or null if the name is unrecognised.
        /// This indirection avoids a hard assembly reference from
        /// <c>Pinder.Core</c> to <c>Pinder.LlmAdapters</c>.
        /// </remarks>
        public static Func<string, string?>? BehaviorResolver { get; set; }

        /// <summary>
        /// Returns the behavioral instruction for the given archetype.
        /// Consults <see cref="BehaviorResolver"/> first, then the
        /// in-memory dictionary (populated by
        /// <see cref="RegisterBehavior"/> calls). After Phase 5 of #871,
        /// throws <see cref="InvalidOperationException"/> if the
        /// archetype is unrecognised — there are no embedded const
        /// fallbacks and returning a placeholder would silently degrade
        /// the LLM prompt.
        /// </summary>
        public static string GetBehavior(string archetypeName)
        {
            if (BehaviorResolver != null)
            {
                var resolved = BehaviorResolver(archetypeName);
                if (resolved != null) return resolved;
            }
            if (_behaviors.TryGetValue(archetypeName, out var behavior))
                return behavior;
            // Unknown archetype — return a generic instruction.
            // Custom characters may define archetypes not in the 20-canon
            // catalog (e.g. "The Pun Troll" in a character definition).
            // These are not hardcoded prompt content; the name itself is
            // the only content and comes from the character data.
            return $"Assume the personality and communication style of '{archetypeName}'.";
        }

        /// <summary>
        /// Register behavior text for an archetype (e.g. loaded from YAML at runtime).
        /// Overwrites any existing registration for the same name.
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

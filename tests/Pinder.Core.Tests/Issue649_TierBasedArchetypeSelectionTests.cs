using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #649 — tier-based archetype selection.
    ///
    /// Tier definitions:
    ///   Tier 1 = Levels 1–3
    ///   Tier 2 = Levels 2–6
    ///   Tier 3 = Levels 3–9
    ///   Tier 4 = Levels 5+
    ///
    /// Tiers overlap by design: level 5 qualifies for tiers 2, 3, and 4.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue649_TierBasedArchetypeSelectionTests
    {
        // ── Tier assignment tests ─────────────────────────────────────────────

        [Theory]
        [InlineData("The Hey Opener",          1)]
        [InlineData("The DTF Opener",          1)]
        [InlineData("The One-Word Replier",    1)]
        [InlineData("The Wall of Text",        2)]
        [InlineData("The Copy-Paste Machine",  2)]
        [InlineData("The Pickup Line Spammer", 2)]
        [InlineData("The Exploding Nice Guy",  2)]
        [InlineData("The Oversharer",          2)]
        [InlineData("The Philosopher",         2)]
        [InlineData("The Instagram Recruiter", 2)]
        [InlineData("The Bot / Scammer",       3)]
        [InlineData("The Zombie",              3)]
        [InlineData("The Breadcrumber",        3)]
        [InlineData("The Love Bomber",         3)]
        [InlineData("The Peacock",             3)]
        [InlineData("The Slow Fader",          4)]
        [InlineData("The Ghost",               4)]
        [InlineData("The Player",              4)]
        [InlineData("The Sniper",              4)]
        [InlineData("The Bio Responder",       4)]
        public void ArchetypeDefinition_HasCorrectTier(string name, int expectedTier)
        {
            var def = ArchetypeCatalog.GetByName(name);
            Assert.NotNull(def);
            Assert.Equal(expectedTier, def!.Tier);
        }

        // ── GetCharacterTiers tests ───────────────────────────────────────────

        [Theory]
        [InlineData(1, new[] { 1 })]
        [InlineData(2, new[] { 1, 2 })]
        [InlineData(3, new[] { 1, 2, 3 })]
        [InlineData(4, new[] { 2, 3 })]
        [InlineData(5, new[] { 2, 3, 4 })]
        [InlineData(6, new[] { 2, 3, 4 })]
        [InlineData(7, new[] { 3, 4 })]
        [InlineData(8, new[] { 3, 4 })]
        [InlineData(9, new[] { 3, 4 })]
        [InlineData(10, new[] { 4 })]
        [InlineData(11, new[] { 4 })]
        public void GetCharacterTiers_ReturnsCorrectTiersForLevel(int level, int[] expectedTiers)
        {
            var tiers = ArchetypeCatalog.GetCharacterTiers(level);
            Assert.Equal(expectedTiers.OrderBy(x => x), tiers.OrderBy(x => x));
        }

        // ── IsEligibleAtLevel tier-based filtering ────────────────────────────

        [Theory]
        // Level 1: only Tier 1 archetypes eligible
        [InlineData("The Hey Opener",          1, true)]   // T1
        [InlineData("The DTF Opener",          1, true)]   // T1
        [InlineData("The Wall of Text",        1, false)]  // T2
        [InlineData("The Sniper",              1, false)]  // T4
        [InlineData("The Ghost",               1, false)]  // T4

        // Level 3: Tiers 1, 2, 3 eligible
        [InlineData("The Hey Opener",          3, true)]   // T1
        [InlineData("The Wall of Text",        3, true)]   // T2
        [InlineData("The Peacock",             3, true)]   // T3
        [InlineData("The Sniper",              3, false)]  // T4
        [InlineData("The Ghost",               3, false)]  // T4

        // Level 5: Tiers 2, 3, 4 eligible
        [InlineData("The Hey Opener",          5, false)]  // T1 — not in tiers 2/3/4
        [InlineData("The Wall of Text",        5, true)]   // T2
        [InlineData("The Love Bomber",         5, true)]   // T3
        [InlineData("The Sniper",              5, true)]   // T4
        [InlineData("The Ghost",               5, true)]   // T4

        // Level 10: only Tier 4 eligible
        [InlineData("The Hey Opener",          10, false)] // T1
        [InlineData("The Philosopher",         10, false)] // T2
        [InlineData("The Love Bomber",         10, false)] // T3
        [InlineData("The Sniper",              10, true)]  // T4
        [InlineData("The Bio Responder",       10, true)]  // T4
        public void IsEligibleAtLevel_TierBased(string name, int level, bool expected)
        {
            Assert.Equal(expected, ArchetypeCatalog.IsEligibleAtLevel(name, level));
        }

        [Fact]
        public void IsEligibleAtLevel_Level0_NoFiltering()
        {
            // characterLevel=0 means "no filtering" (backward-compatible)
            Assert.True(ArchetypeCatalog.IsEligibleAtLevel("The Hey Opener", 0));
            Assert.True(ArchetypeCatalog.IsEligibleAtLevel("The Sniper", 0));
        }

        [Fact]
        public void IsEligibleAtLevel_UnknownArchetype_AlwaysEligible()
        {
            Assert.True(ArchetypeCatalog.IsEligibleAtLevel("Made Up Archetype", 1));
            Assert.True(ArchetypeCatalog.IsEligibleAtLevel("Made Up Archetype", 10));
        }

        // ── Behavior text populated ───────────────────────────────────────────

        [Theory]
        [InlineData("The Hey Opener")]
        [InlineData("The DTF Opener")]
        [InlineData("The One-Word Replier")]
        [InlineData("The Wall of Text")]
        [InlineData("The Copy-Paste Machine")]
        [InlineData("The Pickup Line Spammer")]
        [InlineData("The Exploding Nice Guy")]
        [InlineData("The Oversharer")]
        [InlineData("The Philosopher")]
        [InlineData("The Instagram Recruiter")]
        [InlineData("The Bot / Scammer")]
        [InlineData("The Zombie")]
        [InlineData("The Breadcrumber")]
        [InlineData("The Love Bomber")]
        [InlineData("The Peacock")]
        [InlineData("The Slow Fader")]
        [InlineData("The Ghost")]
        [InlineData("The Player")]
        [InlineData("The Sniper")]
        [InlineData("The Bio Responder")]
        public void GetBehavior_AllArchetypes_HaveRealBehaviorText(string name)
        {
            var behavior = ArchetypeCatalog.GetBehavior(name);
            // Must not be the placeholder "Follow X behavioral pattern."
            Assert.DoesNotContain("behavioral pattern", behavior);
            Assert.True(behavior.Length > 50, $"{name} behavior text is suspiciously short: {behavior}");
        }

        // ── Assembly integration — tier filtering changes what wins ───────────

        private static readonly IReadOnlyDictionary<StatType, int> ZeroStats =
            new Dictionary<StatType, int>();
        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        private sealed class FakeItemRepo : IItemRepository
        {
            private readonly Dictionary<string, ItemDefinition> _items = new();

            public void Add(string id, params string[] archetypes)
            {
                _items[id] = new ItemDefinition(
                    id, id, "head", "common",
                    new Dictionary<StatType, int>(),
                    "personality", "backstory", "texting",
                    archetypes,
                    new TimingModifier(0, 1f, 0f, "neutral"));
            }

            public ItemDefinition? GetItem(string id) { _items.TryGetValue(id, out var v); return v; }
            public IEnumerable<ItemDefinition> GetAll() => _items.Values;
        }

        private sealed class NullAnatomyRepo : IAnatomyRepository
        {
            public AnatomyParameterDefinition? GetParameter(string id) => null;
            public IEnumerable<AnatomyParameterDefinition> GetAll()
                => Enumerable.Empty<AnatomyParameterDefinition>();
        }

        [Fact]
        public void Assemble_Level1_GetsOnlyTier1Archetype()
        {
            // At level 1, only Tier 1 archetypes are eligible.
            // The Sniper (Tier 4, count=3) should lose to The Hey Opener (Tier 1, count=1).
            var items = new FakeItemRepo();
            items.Add("a", "The Sniper", "The Sniper", "The Sniper");
            items.Add("b", "The Hey Opener");

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 1);

            Assert.Equal("The Hey Opener", result.ActiveArchetype?.Name);
        }

        [Fact]
        public void Assemble_Level5_GetsTier4ArchetypeOverTier1()
        {
            // At level 5, tiers 2/3/4 eligible. Tier 1 excluded.
            // The Sniper (Tier 4, count=2) should beat The Hey Opener (Tier 1, count=3).
            var items = new FakeItemRepo();
            items.Add("a", "The Hey Opener", "The Hey Opener", "The Hey Opener"); // T1, excluded at lvl 5
            items.Add("b", "The Sniper", "The Sniper");                           // T4, eligible

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 5);

            Assert.Equal("The Sniper", result.ActiveArchetype?.Name);
        }

        [Fact]
        public void Assemble_Level5_GeraldLevelCharacter_ActiveArchetypeHasBehavior()
        {
            // Level 5 → tiers 2, 3, 4. Verify the resolved archetype has real behavior text.
            var items = new FakeItemRepo();
            items.Add("a", "The Sniper", "The Sniper");
            items.Add("b", "The Ghost");

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 5);

            Assert.NotNull(result.ActiveArchetype);
            Assert.NotEmpty(result.ActiveArchetype!.Behavior);
            Assert.DoesNotContain("behavioral pattern", result.ActiveArchetype.Behavior);
        }

        [Fact]
        public void Assemble_FallbackToUnfiltered_WhenNoTierMatch()
        {
            // Level 1: only Tier 1 eligible. If no Tier 1 archetypes exist in build,
            // fall back to highest-count overall.
            var items = new FakeItemRepo();
            items.Add("a", "The Sniper", "The Sniper"); // T4
            items.Add("b", "The Player");               // T4

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 1);

            // Fallback: highest count overall
            Assert.NotNull(result.ActiveArchetype);
            Assert.Equal("The Sniper", result.ActiveArchetype!.Name);
        }

        [Fact]
        public void ActiveArchetype_Directive_ContainsTierFilteredBehavior()
        {
            // Confirms that the directive built from the resolved archetype
            // contains real behavior text (not a placeholder).
            var items = new FakeItemRepo();
            items.Add("a", "The Breadcrumber", "The Breadcrumber"); // T3

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 5); // T3 eligible at level 5

            Assert.NotNull(result.ActiveArchetype);
            var directive = result.ActiveArchetype!.Directive;
            Assert.Contains("ACTIVE ARCHETYPE: The Breadcrumber", directive);
            Assert.Contains("Sends just enough", directive); // real behavior text
        }
    }
}

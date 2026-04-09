using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for archetype level-range filtering in CharacterAssembler.
    /// Verifies that dominant archetype selection respects level ranges
    /// per rules §archetypes.
    /// </summary>
    public class ArchetypeLevelFilterTests
    {
        // -- Helpers ----------------------------------------------------------

        private static readonly IReadOnlyDictionary<StatType, int> ZeroStats =
            new Dictionary<StatType, int>();
        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        /// <summary>
        /// Fake item repo that returns items with specific archetype tendencies.
        /// </summary>
        private sealed class FakeItemRepo : IItemRepository
        {
            private readonly Dictionary<string, ItemDefinition> _items =
                new Dictionary<string, ItemDefinition>();

            public void Add(string id, params string[] archetypes)
            {
                _items[id] = new ItemDefinition(
                    id, id, "head", "common",
                    new Dictionary<StatType, int>(),
                    "personality", "backstory", "texting",
                    archetypes,
                    new TimingModifier(0, 1f, 0f, "neutral"));
            }

            public ItemDefinition? GetItem(string itemId)
            {
                _items.TryGetValue(itemId, out var item);
                return item;
            }

            public IEnumerable<ItemDefinition> GetAll() => _items.Values;
        }

        private sealed class NullAnatomyRepo : IAnatomyRepository
        {
            public AnatomyParameterDefinition? GetParameter(string parameterId) => null;
            public IEnumerable<AnatomyParameterDefinition> GetAll()
                => Enumerable.Empty<AnatomyParameterDefinition>();
        }

        // -- ArchetypeCatalog tests -------------------------------------------

        [Fact]
        public void Catalog_SniperRange_5To11()
        {
            var def = ArchetypeCatalog.GetByName("The Sniper");
            Assert.NotNull(def);
            Assert.Equal(5, def!.MinLevel);
            Assert.Equal(11, def.MaxLevel);
        }

        [Fact]
        public void Catalog_HeyOpenerRange_1To3()
        {
            var def = ArchetypeCatalog.GetByName("The Hey Opener");
            Assert.NotNull(def);
            Assert.Equal(1, def!.MinLevel);
            Assert.Equal(3, def.MaxLevel);
        }

        [Fact]
        public void Catalog_All20Archetypes()
        {
            Assert.Equal(20, ArchetypeCatalog.All.Count);
        }

        [Fact]
        public void Catalog_CaseInsensitiveLookup()
        {
            Assert.NotNull(ArchetypeCatalog.GetByName("the sniper"));
            Assert.NotNull(ArchetypeCatalog.GetByName("THE SNIPER"));
        }

        [Fact]
        public void Catalog_UnknownArchetype_ReturnsNull()
        {
            Assert.Null(ArchetypeCatalog.GetByName("The Nonexistent"));
        }

        [Fact]
        public void Catalog_UnknownArchetype_IsAlwaysEligible()
        {
            Assert.True(ArchetypeCatalog.IsEligibleAtLevel("Made Up Archetype", 1));
            Assert.True(ArchetypeCatalog.IsEligibleAtLevel("Made Up Archetype", 99));
        }

        [Theory]
        [InlineData("The Sniper", 4, false)]
        [InlineData("The Sniper", 5, true)]
        [InlineData("The Sniper", 11, true)]
        [InlineData("The Sniper", 12, false)]
        [InlineData("The Hey Opener", 1, true)]
        [InlineData("The Hey Opener", 3, true)]
        [InlineData("The Hey Opener", 4, false)]
        [InlineData("The Bio Responder", 3, false)]
        [InlineData("The Bio Responder", 4, true)]
        [InlineData("The Bio Responder", 11, true)]
        public void Catalog_IsEligibleAtLevel(string name, int level, bool expected)
        {
            Assert.Equal(expected, ArchetypeCatalog.IsEligibleAtLevel(name, level));
        }

        // -- CharacterAssembler level filtering tests -------------------------

        [Fact]
        public void Assemble_Level1_CannotHaveSniperDominant()
        {
            // Sniper has level range 5-11, so a Level 1 character should not get it
            var items = new FakeItemRepo();
            // 3x Sniper tendencies, 1x Hey Opener
            items.Add("a", "The Sniper", "The Sniper", "The Sniper");
            items.Add("b", "The Hey Opener");

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 1);

            // Dominant (first ranked) should be Hey Opener, not Sniper
            Assert.Equal("The Hey Opener", result.RankedArchetypes[0].Archetype);
        }

        [Fact]
        public void Assemble_Level7_CanHaveSniperDominant()
        {
            // Sniper is eligible at level 7 (range 5-11)
            var items = new FakeItemRepo();
            items.Add("a", "The Sniper", "The Sniper", "The Sniper");
            items.Add("b", "The Hey Opener");

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 7);

            // Sniper eligible and has highest count → dominant
            Assert.Equal("The Sniper", result.RankedArchetypes[0].Archetype);
            // Hey Opener NOT eligible at level 7 (range 1-3) → filtered out
            Assert.DoesNotContain(result.RankedArchetypes,
                a => a.Archetype == "The Hey Opener");
        }

        [Fact]
        public void Assemble_Level7_CannotHaveHeyOpenerDominant()
        {
            // Hey Opener range 1-3, not eligible at level 7
            var items = new FakeItemRepo();
            items.Add("a", "The Hey Opener", "The Hey Opener", "The Hey Opener");
            items.Add("b", "The Sniper");

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 7);

            // Hey Opener filtered out; Sniper is dominant
            Assert.Equal("The Sniper", result.RankedArchetypes[0].Archetype);
        }

        [Fact]
        public void Assemble_NoLevelProvided_NoFiltering()
        {
            // Default characterLevel=0 means no filtering (backward compat)
            var items = new FakeItemRepo();
            items.Add("a", "The Sniper", "The Sniper", "The Sniper");
            items.Add("b", "The Hey Opener");

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow);

            // No filtering → Sniper wins by count
            Assert.Equal("The Sniper", result.RankedArchetypes[0].Archetype);
        }

        [Fact]
        public void Assemble_AllArchetypesFilteredOut_FallsBackToUnfiltered()
        {
            // Edge case: if level filtering eliminates ALL archetypes,
            // fall back to unfiltered list
            var items = new FakeItemRepo();
            items.Add("a", "The Sniper"); // range 5-11
            items.Add("b", "The Player"); // range 5-10

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 1);

            // Both filtered out at level 1, but fallback keeps them
            Assert.NotEmpty(result.RankedArchetypes);
        }

        [Fact]
        public void Assemble_UnknownArchetypesKeptDuringFiltering()
        {
            // Unknown archetypes not in catalog are always eligible
            var items = new FakeItemRepo();
            items.Add("a", "Custom Archetype", "Custom Archetype");
            items.Add("b", "The Sniper"); // range 5-11

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 1);

            // Custom Archetype stays (unknown = always eligible), Sniper filtered out
            Assert.Equal("Custom Archetype", result.RankedArchetypes[0].Archetype);
            Assert.Single(result.RankedArchetypes);
        }

        [Fact]
        public void Assemble_MultipleEligible_HighestCountWins()
        {
            var items = new FakeItemRepo();
            items.Add("a", "The Philosopher", "The Philosopher"); // range 2-7
            items.Add("b", "The Oversharer"); // range 2-7
            items.Add("c", "The Ghost"); // range 1-10

            var assembler = new CharacterAssembler(items, new NullAnatomyRepo());
            var result = assembler.Assemble(
                new[] { "a", "b", "c" },
                new Dictionary<string, string>(),
                ZeroStats, ZeroShadow,
                characterLevel: 5);

            // All three eligible at level 5; Philosopher has count 2 → dominant
            Assert.Equal("The Philosopher", result.RankedArchetypes[0].Archetype);
        }
    }
}

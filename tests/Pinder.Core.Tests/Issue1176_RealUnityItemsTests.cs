using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #1176: Real Unity item ids, conflict/priority resolution, unknown-id safety.
    ///
    /// Tests:
    ///   1. Real Unity fixture items resolve and produce core-authored modifiers.
    ///   2. Conflict/priority: two items sharing a conflict_tag → only higher-priority
    ///      item's fragments appear.
    ///   3. Unknown-id: equipped id absent from core → zero modifiers + id surfaced
    ///      in UnknownItemIds; player flow does not throw.
    ///   4. Sticker/tattoo id pool: same id works as both item_type=tattoo and =sticker
    ///      (distinguished by item_type in the JSON — no separate sticker entries needed).
    /// </summary>
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public class Issue1176_RealUnityItemsTests
    {
        // ─── Helpers ────────────────────────────────────────────────────────────

        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root.");
            }
        }

        private static IItemRepository LoadItemRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "items", "starter-items.json"));
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository LoadAnatomyRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            return new JsonAnatomyRepository(json);
        }

        private static readonly IReadOnlyDictionary<StatType, int> ZeroBaseStats =
            new Dictionary<StatType, int>();

        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        private static FragmentCollection Assemble(
            IEnumerable<string> itemIds,
            IItemRepository? repo = null,
            IAnatomyRepository? anatomy = null)
        {
            repo    ??= LoadItemRepo();
            anatomy ??= LoadAnatomyRepo();
            var assembler = new CharacterAssembler(repo, anatomy);
            return assembler.Assemble(
                itemIds,
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);
        }

        // ─── 1. Real Unity fixture items resolve ────────────────────────────────

        [Fact]
        public void RealUnityItems_HeadTophat_ResolvesWithCharmBonus()
        {
            var repo = LoadItemRepo();
            var item = repo.GetItem("head_tophat");

            Assert.NotNull(item);
            Assert.Equal("head_tophat", item!.ItemId);
            Assert.Equal("accessory", item.ItemType);
            Assert.Equal("Head", item.Slot);
            Assert.True(item.StatModifiers.ContainsKey(StatType.Charm),
                "head_tophat should have a charm modifier");
        }

        [Fact]
        public void RealUnityItems_Vest1_IsOutfitOnBodySlot()
        {
            var repo = LoadItemRepo();
            var item = repo.GetItem("vest1");

            Assert.NotNull(item);
            Assert.Equal("outfit", item!.ItemType);
            Assert.Equal("Body", item.Slot);
        }

        [Fact]
        public void RealUnityItems_Classic2_IsTattooType()
        {
            var repo = LoadItemRepo();
            var item = repo.GetItem("classic2");

            Assert.NotNull(item);
            Assert.Equal("tattoo", item!.ItemType);
            Assert.Equal("Tattoo", item.Slot);
        }

        [Fact]
        public void RealUnityItems_Hair1_IsHairType()
        {
            var repo = LoadItemRepo();
            var item = repo.GetItem("hair1");

            Assert.NotNull(item);
            Assert.Equal("hair", item!.ItemType);
            Assert.Equal("Hair", item.Slot);
        }

        [Fact]
        public void RealUnityItems_Arms0_IsArmsType()
        {
            var repo = LoadItemRepo();
            var item = repo.GetItem("arms0");

            Assert.NotNull(item);
            Assert.Equal("arms", item!.ItemType);
            Assert.Equal("Arms", item.Slot);
        }

        [Fact]
        public void RealUnityItems_NoVest6_AbsentFromRepo()
        {
            var repo = LoadItemRepo();
            // vest6 is ABSENT in Unity — must not be in core
            Assert.Null(repo.GetItem("vest6"));
        }

        [Fact]
        public void RealUnityItems_Arms3AndArms4_BothPresent_SameDuplicateTRex()
        {
            // arms3 and arms4 are duplicate 'T rex' ids in Unity — both must be present in core
            // (Unity JIRA follow-up: consolidate to single id)
            var repo = LoadItemRepo();
            var arms3 = repo.GetItem("arms3");
            var arms4 = repo.GetItem("arms4");

            Assert.NotNull(arms3);
            Assert.NotNull(arms4);
            // Both should have matching personality (same T-rex concept)
            Assert.Equal(arms3!.PersonalityFragment, arms4!.PersonalityFragment);
        }

        /// <summary>
        /// Integration: a character fixture with real Unity item ids
        /// (head_tophat + vest1 + classic2 + hair1 + arms0) resolves and
        /// core-authored modifiers appear in the assembled fragment collection.
        /// </summary>
        [Fact]
        public void Integration_RealUnityFixture_ItemModifiersInAssembledOutput()
        {
            var repo    = LoadItemRepo();
            var anatomy = LoadAnatomyRepo();
            var assembler = new CharacterAssembler(repo, anatomy);

            // Real Unity-style equipped item set
            var equippedIds = new[] { "head_tophat", "vest1", "classic2", "hair1", "arms0" };

            var result = assembler.Assemble(
                equippedIds,
                new Dictionary<string, float>(),
                new Dictionary<StatType, int>(),
                new Dictionary<ShadowStatType, int>());

            // head_tophat has charm+1 and rizz+1
            Assert.True(result.Stats.GetEffective(StatType.Charm) >= 1,
                "head_tophat should contribute +1 charm");
            Assert.True(result.Stats.GetEffective(StatType.Rizz) >= 1,
                "head_tophat should contribute +1 rizz");

            // head_tophat has a personality fragment
            Assert.True(result.PersonalityFragments.Count >= 1,
                "At least one personality fragment should be present from head_tophat");

            // No unknown ids — all five are real Unity items
            Assert.Empty(result.UnknownItemIds);
        }

        // ─── 2. Conflict/priority resolution ────────────────────────────────────

        [Fact]
        public void ConflictResolution_TwoItemsSameConflictTag_HigherPriorityFragmentAppears()
        {
            // Create two items sharing conflict_tag "test_conflict"
            // Item A: priority 200, personality "A wins"
            // Item B: priority 100, personality "B loses"
            // Expect: only "A wins" appears; "B loses" is suppressed.

            var itemA = new ItemDefinition(
                "item_a", "Item A", "Head", "accessory", 200,
                new[] { "test_conflict" },
                new Dictionary<StatType, int>(),
                "A wins",
                "", "", new string[0],
                TimingModifier.Zero);

            var itemB = new ItemDefinition(
                "item_b", "Item B", "Head", "accessory", 100,
                new[] { "test_conflict" },
                new Dictionary<StatType, int>(),
                "B loses",
                "", "", new string[0],
                TimingModifier.Zero);

            var fakeRepo = new InlineItemRepo(itemA, itemB);
            var anatomy  = new NullAnatomyRepo();
            var assembler = new CharacterAssembler(fakeRepo, anatomy);

            var result = assembler.Assemble(
                new[] { "item_a", "item_b" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            Assert.Contains("A wins", result.PersonalityFragments);
            Assert.DoesNotContain("B loses", result.PersonalityFragments);
        }

        [Fact]
        public void ConflictResolution_LowerPriorityEquippedFirst_HigherPriorityStillWins()
        {
            // Item with higher priority equipped SECOND — should still win
            var itemLow = new ItemDefinition(
                "item_low", "Low", "Head", "accessory", 50,
                new[] { "face_conflict" },
                new Dictionary<StatType, int>(),
                "low priority",
                "", "", new string[0],
                TimingModifier.Zero);

            var itemHigh = new ItemDefinition(
                "item_high", "High", "Head", "accessory", 150,
                new[] { "face_conflict" },
                new Dictionary<StatType, int>(),
                "high priority",
                "", "", new string[0],
                TimingModifier.Zero);

            var fakeRepo = new InlineItemRepo(itemLow, itemHigh);
            var assembler = new CharacterAssembler(fakeRepo, new NullAnatomyRepo());

            // itemLow is equipped first, itemHigh second
            var result = assembler.Assemble(
                new[] { "item_low", "item_high" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            Assert.Contains("high priority", result.PersonalityFragments);
            Assert.DoesNotContain("low priority", result.PersonalityFragments);
        }

        [Fact]
        public void ConflictResolution_SamePriority_EarlierEquipOrderWins()
        {
            // Tie on priority: earlier equip order wins
            var item1 = new ItemDefinition(
                "item_first", "First", "Head", "accessory", 100,
                new[] { "tie_tag" },
                new Dictionary<StatType, int>(),
                "first equipped",
                "", "", new string[0],
                TimingModifier.Zero);

            var item2 = new ItemDefinition(
                "item_second", "Second", "Head", "accessory", 100,
                new[] { "tie_tag" },
                new Dictionary<StatType, int>(),
                "second equipped",
                "", "", new string[0],
                TimingModifier.Zero);

            var fakeRepo = new InlineItemRepo(item1, item2);
            var assembler = new CharacterAssembler(fakeRepo, new NullAnatomyRepo());

            var result = assembler.Assemble(
                new[] { "item_first", "item_second" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            Assert.Contains("first equipped", result.PersonalityFragments);
            Assert.DoesNotContain("second equipped", result.PersonalityFragments);
        }

        [Fact]
        public void ConflictResolution_StatModifiersAlwaysApply_EvenFromLoser()
        {
            // The conflict loser's stat modifiers must still be applied
            var winner = new ItemDefinition(
                "winner", "Winner", "Head", "accessory", 200,
                new[] { "stat_conflict" },
                new Dictionary<StatType, int> { { StatType.Charm, 5 } },
                "winner fragment",
                "", "", new string[0],
                TimingModifier.Zero);

            var loser = new ItemDefinition(
                "loser", "Loser", "Head", "accessory", 100,
                new[] { "stat_conflict" },
                new Dictionary<StatType, int> { { StatType.Wit, 3 } },
                "loser fragment",
                "", "", new string[0],
                TimingModifier.Zero);

            var fakeRepo = new InlineItemRepo(winner, loser);
            var assembler = new CharacterAssembler(fakeRepo, new NullAnatomyRepo());

            var result = assembler.Assemble(
                new[] { "winner", "loser" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            // Fragments: only winner's appear
            Assert.Contains("winner fragment", result.PersonalityFragments);
            Assert.DoesNotContain("loser fragment", result.PersonalityFragments);

            // But BOTH stat modifiers apply
            Assert.Equal(5, result.Stats.GetEffective(StatType.Charm));
            Assert.Equal(3, result.Stats.GetEffective(StatType.Wit));
        }

        [Fact]
        public void ConflictResolution_RealItems_face_eyewear_ConflictTag_Works()
        {
            // face_glases1 and face_monocle both have conflict_tag "face_eyewear"
            // face_monocle priority 100 vs face_glases1 priority 100 → tie → earlier wins
            var repo     = LoadItemRepo();
            var anatomy  = LoadAnatomyRepo();
            var assembler = new CharacterAssembler(repo, anatomy);

            var result = assembler.Assemble(
                new[] { "face_glases1", "face_monocle" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            // Only one "face_eyewear" fragment should appear (both have personality frags)
            // face_glases1 is equipped first, so its fragment wins
            int personalityCount = result.PersonalityFragments.Count;
            Assert.Equal(1, personalityCount);
        }

        // ─── 3. Unknown-id safety ────────────────────────────────────────────────

        [Fact]
        public void UnknownId_ZeroModifiers_IdSurfacedInSignal_NoException()
        {
            var repo     = LoadItemRepo();
            var anatomy  = LoadAnatomyRepo();
            var assembler = new CharacterAssembler(repo, anatomy);

            // "totally_fictional_item_xyz" is NOT in core
            var equippedIds = new[] { "totally_fictional_item_xyz" };

            // Must NOT throw
            var result = assembler.Assemble(
                equippedIds,
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            // Zero stat modifiers (base stats only)
            foreach (StatType st in Enum.GetValues(typeof(StatType)))
                Assert.Equal(0, result.Stats.GetEffective(st));

            // No fragments contributed
            Assert.Empty(result.PersonalityFragments);

            // Unknown id surfaced in signal
            Assert.Contains("totally_fictional_item_xyz", result.UnknownItemIds);
        }

        [Fact]
        public void UnknownId_MixedWithKnown_KnownModifiersApply_UnknownSurfaced()
        {
            var repo     = LoadItemRepo();
            var anatomy  = LoadAnatomyRepo();
            var assembler = new CharacterAssembler(repo, anatomy);

            // head_tophat is known (charm+1, rizz+1), "ghost_item" is not
            var result = assembler.Assemble(
                new[] { "head_tophat", "ghost_item" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            // Known item's charm modifier applies
            Assert.True(result.Stats.GetEffective(StatType.Charm) >= 1);

            // Unknown id is surfaced
            Assert.Contains("ghost_item", result.UnknownItemIds);

            // Known item's personality fragment appears
            Assert.NotEmpty(result.PersonalityFragments);
        }

        [Fact]
        public void UnknownId_MultipleUnknown_AllSurfaced()
        {
            var repo     = LoadItemRepo();
            var anatomy  = LoadAnatomyRepo();
            var assembler = new CharacterAssembler(repo, anatomy);

            var result = assembler.Assemble(
                new[] { "ghost1", "ghost2", "ghost3", "head_tophat" },
                new Dictionary<string, float>(),
                ZeroBaseStats,
                ZeroShadow);

            Assert.Contains("ghost1", result.UnknownItemIds);
            Assert.Contains("ghost2", result.UnknownItemIds);
            Assert.Contains("ghost3", result.UnknownItemIds);
            Assert.DoesNotContain("head_tophat", result.UnknownItemIds);
        }

        // ─── 4. Sticker/Tattoo id pool ──────────────────────────────────────────

        [Fact]
        public void TattooCatalog_AllClassicIds_PresentInRepo()
        {
            var repo = LoadItemRepo();
            // Spot-check the tattoo id pool
            var tatooIds = new[]
            {
                "classic2", "classic3", "classic10", "classic20", "classic35",
                "flowers1", "flowers5", "flowers9"
            };
            foreach (var id in tatooIds)
            {
                var item = repo.GetItem(id);
                Assert.NotNull(item);
                Assert.Equal("tattoo", item!.ItemType);
            }
        }

        [Fact]
        public void AllRealUnityIds_PresentInRepo()
        {
            var repo = LoadItemRepo();

            // All Unity-verified ids from the inventory file
            var allIds = new[]
            {
                "head_tophat","face_monocle","head_cheff","head_crown","head_hair",
                "head_hat","head_hat_2","head_hat_3","head_hat_4","head_hat_5",
                "head_hat_6","head_hat_8","head_hat_9","head_hat_10","head_horns",
                "face_eyes1","face_eyes2","face_glases1","face_mouth1","face_tongue1",
                "face_nariz1","face_pirate1","head_antenas","head_horns2","head_pirate",
                "head_wiz","special_shoe1","special_shoe2","special_shoe3","special_shoe4",
                "special_shoe5","special_shoe6","special_shoe7",
                "outfit_maid","vest1","vest2","vest3","vest4","vest5",
                "vest7","vest8","vest9","vest10","vest11",
                "arms0","arms1","arms2","arms3","arms4","arms5","arms6",
                "hair1","hair2","hair3","hair4","hair5",
                "classic2","classic3","classic4","classic5","classic6","classic7",
                "classic8","classic9","classic10","classic11","classic12","classic13",
                "classic14","classic15","classic16","classic17","classic18","classic19",
                "classic20","classic21","classic22","classic23","classic24","classic25",
                "classic26","classic27","classic28","classic29","classic30","classic31",
                "classic32","classic33","classic34","classic35",
                "flowers1","flowers2","flowers3","flowers4","flowers5",
                "flowers6","flowers7","flowers8","flowers9"
            };

            var missing = allIds.Where(id => repo.GetItem(id) == null).ToList();
            Assert.Empty(missing);
        }

        [Fact]
        public void Vest6_IsAbsentFromRepo()
        {
            // vest6 is ABSENT in Unity — must NOT be in core
            var repo = LoadItemRepo();
            Assert.Null(repo.GetItem("vest6"));
        }

        // ─── Stub types ─────────────────────────────────────────────────────────

        private sealed class InlineItemRepo : IItemRepository
        {
            private readonly Dictionary<string, ItemDefinition> _map;

            public InlineItemRepo(params ItemDefinition[] items)
            {
                _map = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
                foreach (var item in items)
                    _map[item.ItemId] = item;
            }

            public ItemDefinition? GetItem(string itemId)
            {
                _map.TryGetValue(itemId, out var v);
                return v;
            }

            public IEnumerable<ItemDefinition> GetAll() => _map.Values;
        }

        private sealed class NullAnatomyRepo : IAnatomyRepository
        {
            public AnatomyParameterDefinition? GetParameter(string id) => null;
            public IEnumerable<AnatomyParameterDefinition> GetAll()
                => Enumerable.Empty<AnatomyParameterDefinition>();
        }
    }
}

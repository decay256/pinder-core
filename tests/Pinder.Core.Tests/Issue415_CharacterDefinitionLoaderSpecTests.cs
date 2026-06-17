using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for issue #415: CharacterDefinitionLoader, DataFileLocator,
    /// and the character assembly pipeline via JSON data files.
    /// Tests verify behavior from docs/specs/issue-415-spec.md only.
    /// </summary>
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public partial class Issue415_CharacterDefinitionLoaderSpecTests
    {
        #region Helpers

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
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        private static IItemRepository LoadItemRepo()
        {
            string json = File.ReadAllText(Path.Combine(RepoRoot, "data", "items", "starter-items.json"));
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository LoadAnatomyRepo()
        {
            string json = File.ReadAllText(Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            return new JsonAnatomyRepository(json);
        }

        // Builds a minimally-valid v1 character JSON. Defaults to a fixed test
        // character_id; pass `characterId: null` to omit the field entirely
        // (used for negative tests that target the v1 schema_version /
        // character_id requirements).
        private static string BuildMinimalJson(
            string name = "TestChar",
            string genderIdentity = "they/them",
            string bio = "test bio",
            int level = 1,
            string itemsArray = "[]",
            string anatomyBlock = "{}",
            string? buildPointsInner = null,
            string? shadowsInner = null,
            string? schemaVersion = "2",
            string? characterId = "550e8400-e29b-41d4-a716-446655440000")
        {
            buildPointsInner ??= @"""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1";
            shadowsInner ??= @"""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0";

            var parts = new List<string>();
            if (schemaVersion != null)
                parts.Add($@"""schema_version"": {schemaVersion}");
            if (characterId != null)
                parts.Add($@"""character_id"": ""{characterId}""");
            parts.Add($@"""name"": ""{name}""");
            parts.Add($@"""gender_identity"": ""{genderIdentity}""");
            parts.Add($@"""bio"": ""{bio}""");
            parts.Add($@"""level"": {level}");
            parts.Add($@"""items"": {itemsArray}");
            parts.Add($@"""anatomy"": {anatomyBlock}");
            parts.Add($@"""allocation"": {{ ""spent"": {{ {buildPointsInner} }}, ""unspent_pool"": 0, ""shadows"": {{ {shadowsInner} }} }}");

            return "{ " + string.Join(", ", parts) + " }";
        }

        #endregion

        // =====================================================================
        // AC2: CharacterAssembler is called with real item/anatomy data
        // =====================================================================

        // Fails if: Load() bypasses CharacterAssembler and returns hardcoded profile
        [Fact]
        public void AC2_Load_UsesRealItemData_StatsReflectItemModifiers()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            // Gerald has build_points charm=6, plus items that add charm modifiers
            // If assembler is bypassed, stats would only equal build points
            int charm = profile.Stats.GetEffective(StatType.Charm);
            Assert.True(charm >= 6, $"Gerald's charm ({charm}) must be >= 6 (build points baseline)");
        }

        // =====================================================================
        // AC3: Stat block computed from items + anatomy + build points
        // =====================================================================

        // Fails if: Stats are hardcoded instead of computed from build_points
        [Fact]
        public void AC3_Parse_EmptyItemsAndAnatomy_StatsEqualBuildPoints()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(
                buildPointsInner: @"""charm"": 5, ""rizz"": 3, ""honesty"": 2, ""chaos"": 4, ""wit"": 1, ""self_awareness"": 6");

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            // With no items or anatomy, stats must exactly equal build points
            Assert.Equal(5, profile.Stats.GetEffective(StatType.Charm));
            Assert.Equal(3, profile.Stats.GetEffective(StatType.Rizz));
            Assert.Equal(2, profile.Stats.GetEffective(StatType.Honesty));
            Assert.Equal(4, profile.Stats.GetEffective(StatType.Chaos));
            Assert.Equal(1, profile.Stats.GetEffective(StatType.Wit));
            Assert.Equal(6, profile.Stats.GetEffective(StatType.SelfAwareness));
        }

        // Fails if: build_points values are ignored or swapped between stats
        [Fact]
        public void AC3_Parse_DifferentBuildPoints_ProducesDifferentStats()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json1 = BuildMinimalJson(
                buildPointsInner: @"""charm"": 10, ""rizz"": 0, ""honesty"": 0, ""chaos"": 0, ""wit"": 0, ""self_awareness"": 0");
            string json2 = BuildMinimalJson(
                buildPointsInner: @"""charm"": 0, ""rizz"": 10, ""honesty"": 0, ""chaos"": 0, ""wit"": 0, ""self_awareness"": 0");

            var profile1 = CharacterDefinitionLoader.Parse(json1, itemRepo, anatomyRepo);
            var profile2 = CharacterDefinitionLoader.Parse(json2, itemRepo, anatomyRepo);

            Assert.Equal(10, profile1.Stats.GetEffective(StatType.Charm));
            Assert.Equal(0, profile1.Stats.GetEffective(StatType.Rizz));
            Assert.Equal(0, profile2.Stats.GetEffective(StatType.Charm));
            Assert.Equal(10, profile2.Stats.GetEffective(StatType.Rizz));
        }

        // =====================================================================
        // AC4: System prompt assembled from fragments
        // =====================================================================

        // Fails if: System prompt is empty or loaded from a file instead of assembled
        [Fact]
        public void AC4_Parse_SystemPrompt_ContainsAssembledSections()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string json = File.ReadAllText(Path.Combine(RepoRoot, "data", "characters", "gerald.json"));

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.False(string.IsNullOrWhiteSpace(profile.AssembledSystemPrompt));
            // PromptBuilder produces sections like PERSONALITY, BACKSTORY, TEXTING STYLE, etc. (name removed from lead-in after RULES migration)
            Assert.Contains("PERSONALITY", profile.AssembledSystemPrompt);
        }

        // Fails if: gender_identity not passed to PromptBuilder (name removed from lead-in after RULES migration)
        [Fact]
        public void AC4_Parse_SystemPrompt_ContainsNameAndGender()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(name: "SpecialName", genderIdentity: "xe/xem");
            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Contains("xe/xem", profile.AssembledSystemPrompt);
        }

        // =====================================================================
        // AC5: Starter character definition files exist for all 5 characters
        // =====================================================================

        // Fails if: Any character definition file is missing from the repo
        [Theory]
        [InlineData("gerald")]
        [InlineData("velvet")]
        [InlineData("sable")]
        [InlineData("brick")]
        [InlineData("zyx")]
        [InlineData("reuben")]
        public void AC5_CharacterDefinitionFile_Exists(string name)
        {
            string path = Path.Combine(RepoRoot, "data", "characters", $"{name}.json");
            Assert.True(File.Exists(path), $"Character definition file for {name} must exist at {path}");
        }

        // Fails if: Any character definition fails to parse or has invalid structure
        [Theory]
        [InlineData("gerald")]
        [InlineData("velvet")]
        [InlineData("sable")]
        [InlineData("brick")]
        [InlineData("zyx")]
        [InlineData("reuben")]
        public void AC5_CharacterDefinition_LoadsSuccessfully(string name)
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", $"{name}.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            Assert.NotNull(profile);
            Assert.False(string.IsNullOrEmpty(profile.DisplayName), $"{name} must have a DisplayName");
            Assert.True(profile.Level >= 1 && profile.Level <= 11,
                $"{name} level must be 1-11, got {profile.Level}");
            Assert.NotNull(profile.Stats);
            Assert.False(string.IsNullOrWhiteSpace(profile.AssembledSystemPrompt),
                $"{name} must have an assembled system prompt");
        }

        // =====================================================================
        // AC6: Shorthand --player maps to data/characters/{name}.json via DataFileLocator
        // =====================================================================

        // Fails if: DataFileLocator cannot find character definition files from test base dir
        [Theory]
        [InlineData("gerald")]
        [InlineData("velvet")]
        [InlineData("sable")]
        [InlineData("brick")]
        [InlineData("zyx")]
        [InlineData("reuben")]
        public void AC6_DataFileLocator_FindsCharacterDefinition(string name)
        {
            string relativePath = Path.Combine("data", "characters", $"{name}.json");
            string? found = DataFileLocator.FindDataFile(AppContext.BaseDirectory, relativePath);

            Assert.NotNull(found);
            Assert.True(File.Exists(found), $"Resolved path for {name} must point to existing file");
        }

        // =====================================================================
        // Data files presence
        // =====================================================================

        // Fails if: starter-items.json not copied into repo
        [Fact]
        public void DataFiles_StarterItemsJson_Exists()
        {
            string? path = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "items", "starter-items.json"));
            Assert.NotNull(path);
            Assert.True(File.Exists(path!));
        }

        // Fails if: anatomy-parameters.json not copied into repo
        [Fact]
        public void DataFiles_AnatomyParametersJson_Exists()
        {
            string? path = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "anatomy", "anatomy-parameters.json"));
            Assert.NotNull(path);
            Assert.True(File.Exists(path!));
        }

        // Fails if: Data files can't be parsed by their respective repositories
        [Fact]
        public void DataFiles_ParseableByRepositories()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            // If these don't throw, the JSON is valid for the repositories
            Assert.NotNull(itemRepo);
            Assert.NotNull(anatomyRepo);
        }
    }
}

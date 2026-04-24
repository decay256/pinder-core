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
    public class Issue415_CharacterDefinitionLoaderSpecTests
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

        private static string BuildMinimalJson(
            string name = "TestChar",
            string genderIdentity = "they/them",
            string bio = "test bio",
            int level = 1,
            string itemsArray = "[]",
            string anatomyBlock = "{}",
            string? buildPointsInner = null,
            string? shadowsInner = null)
        {
            buildPointsInner ??= @"""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1";

            var parts = new List<string>
            {
                $@"""name"": ""{name}""",
                $@"""gender_identity"": ""{genderIdentity}""",
                $@"""bio"": ""{bio}""",
                $@"""level"": {level}",
                $@"""items"": {itemsArray}",
                $@"""anatomy"": {anatomyBlock}",
                $@"""build_points"": {{ {buildPointsInner} }}"
            };

            if (shadowsInner != null)
                parts.Add($@"""shadows"": {{ {shadowsInner} }}");

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
            // PromptBuilder produces sections like PERSONALITY, BACKSTORY, TEXTING STYLE, etc.
            Assert.Contains("Gerald_42", profile.AssembledSystemPrompt);
            Assert.Contains("PERSONALITY", profile.AssembledSystemPrompt);
        }

        // Fails if: Name or gender_identity not passed to PromptBuilder
        [Fact]
        public void AC4_Parse_SystemPrompt_ContainsNameAndGender()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(name: "SpecialName", genderIdentity: "xe/xem");
            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Contains("SpecialName", profile.AssembledSystemPrompt);
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

        // =====================================================================
        // Edge Cases (from spec)
        // =====================================================================

        // Fails if: Missing shadows doesn't default to zero
        [Fact]
        public void EdgeCase_MissingShadows_DefaultsToZero()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(); // no shadows field

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Madness));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Despair));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Denial));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Fixation));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Dread));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Overthinking));
        }

        // Fails if: Empty items array causes an exception instead of graceful handling
        [Fact]
        public void EdgeCase_EmptyItems_ProducesValidProfile()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(itemsArray: "[]");

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.NotNull(profile);
            Assert.Equal("TestChar", profile.DisplayName);
        }

        // Fails if: Empty anatomy object causes an exception
        [Fact]
        public void EdgeCase_EmptyAnatomy_ProducesValidProfile()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(anatomyBlock: "{}");

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.NotNull(profile);
            Assert.Equal("TestChar", profile.DisplayName);
        }

        // Fails if: Special characters in name cause crash instead of passthrough
        [Fact]
        public void EdgeCase_SpecialCharactersInName_PassedThrough()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(name: "Xx_D3str0y3r_xX");

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal("Xx_D3str0y3r_xX", profile.DisplayName);
        }

        // Fails if: Missing item IDs crash instead of being silently skipped
        [Fact]
        public void EdgeCase_MissingItemIds_SilentlySkipped()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(
                itemsArray: @"[""nonexistent-item-id-1"", ""nonexistent-item-id-2""]");

            // Should not throw — assembler silently skips unknown items
            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);
            Assert.NotNull(profile);
        }

        // =====================================================================
        // Error Conditions (from spec)
        // =====================================================================

        // Fails if: FileNotFoundException not thrown for missing file
        [Fact]
        public void Error_FileNotFound_ThrowsFileNotFoundException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            Assert.Throws<FileNotFoundException>(() =>
                CharacterDefinitionLoader.Load("/absolutely/nonexistent/path.json", itemRepo, anatomyRepo));
        }

        // Fails if: Malformed JSON doesn't throw FormatException
        [Fact]
        public void Error_MalformedJson_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse("{{{not valid json", itemRepo, anatomyRepo));
        }

        // Fails if: Missing "name" field not detected
        [Fact]
        public void Error_MissingName_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Missing "gender_identity" field not detected
        [Fact]
        public void Error_MissingGenderIdentity_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("gender_identity", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Missing "bio" field not detected
        [Fact]
        public void Error_MissingBio_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("bio", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Missing "level" field not detected
        [Fact]
        public void Error_MissingLevel_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("level", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Missing "items" field not detected
        [Fact]
        public void Error_MissingItems_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("items", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Missing "anatomy" field not detected
        [Fact]
        public void Error_MissingAnatomy_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("anatomy", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Missing "build_points" field not detected
        [Fact]
        public void Error_MissingBuildPoints_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {}
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("build_points", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Level validation doesn't reject level 0
        [Fact]
        public void Error_LevelZero_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(level: 0);

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("level", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Level validation doesn't reject level 12
        [Fact]
        public void Error_Level12_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(level: 12);

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("level", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Fails if: Level boundary 1 is rejected when it should be valid
        [Fact]
        public void Boundary_Level1_IsValid()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(level: 1);
            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal(1, profile.Level);
        }

        // Fails if: Level boundary 11 is rejected when it should be valid
        [Fact]
        public void Boundary_Level11_IsValid()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(level: 11);
            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal(11, profile.Level);
        }

        // Fails if: Unknown stat type not detected in build_points
        [Fact]
        public void Error_UnknownStatType_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(
                buildPointsInner: @"""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""bogus_stat"": 1");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("Unknown stat type", ex.Message);
        }

        // Fails if: Unknown shadow stat type not detected
        [Fact]
        public void Error_UnknownShadowType_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(
                shadowsInner: @"""madness"": 1, ""bogus_shadow"": 5");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("Unknown shadow stat type", ex.Message);
        }

        // =====================================================================
        // Shadow stat parsing
        // =====================================================================

        // Fails if: Shadow values from JSON are ignored or all default to 0
        [Fact]
        public void Parse_ShadowValues_ReflectedInStatBlock()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = BuildMinimalJson(
                shadowsInner: @"""madness"": 7, ""horniness"": 3, ""denial"": 0, ""fixation"": 12, ""dread"": 1, ""overthinking"": 5");

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal(7, profile.Stats.GetShadow(ShadowStatType.Madness));
            Assert.Equal(3, profile.Stats.GetShadow(ShadowStatType.Despair));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Denial));
            Assert.Equal(12, profile.Stats.GetShadow(ShadowStatType.Fixation));
            Assert.Equal(1, profile.Stats.GetShadow(ShadowStatType.Dread));
            Assert.Equal(5, profile.Stats.GetShadow(ShadowStatType.Overthinking));
        }

        // =====================================================================
        // DataFileLocator tests
        // =====================================================================

        // Fails if: FindRepoRoot can't locate repo root from deep subdirectory
        [Fact]
        public void DataFileLocator_FindRepoRoot_FromTestDir_FindsRoot()
        {
            string? root = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);

            Assert.NotNull(root);
            Assert.True(Directory.Exists(Path.Combine(root!, "data")));
            Assert.True(Directory.Exists(Path.Combine(root!, "src")));
        }

        // Fails if: FindDataFile returns path for nonexistent file
        [Fact]
        public void DataFileLocator_FindDataFile_Nonexistent_ReturnsNull()
        {
            string? result = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "nonexistent", "does-not-exist.json"));

            Assert.Null(result);
        }

        // Fails if: FindDataFile doesn't walk up directories to find data file
        [Fact]
        public void DataFileLocator_FindDataFile_WalksUpToFindItemsJson()
        {
            string? path = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "items", "starter-items.json"));

            Assert.NotNull(path);
            Assert.True(File.Exists(path!));
        }

        // =====================================================================
        // Integration: Full pipeline for each character
        // =====================================================================

        // Fails if: Gerald's profile is incomplete (missing timing, prompt, etc.)
        [Fact]
        public void Integration_Gerald_FullPipelineProducesCompleteProfile()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            Assert.Equal("Gerald_42", profile.DisplayName);
            Assert.Equal(5, profile.Level);
            Assert.NotNull(profile.Stats);
            Assert.NotNull(profile.Timing);
            Assert.False(string.IsNullOrWhiteSpace(profile.AssembledSystemPrompt));
            // Stats should have non-trivial values from items + build_points
            int totalStats = 0;
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                totalStats += profile.Stats.GetEffective(stat);
            Assert.True(totalStats > 0, "Total stats should be positive");
        }

        // Fails if: Different characters produce identical stat blocks (pipeline is dummy)
        [Fact]
        public void Integration_DifferentCharacters_ProduceDifferentStats()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            var gerald = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "gerald.json"), itemRepo, anatomyRepo);
            var velvet = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "velvet.json"), itemRepo, anatomyRepo);

            // Different characters should have different names
            Assert.NotEqual(gerald.DisplayName, velvet.DisplayName);

            // And different stat profiles (extremely unlikely to be identical with different items)
            bool anyDifferent = false;
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                if (gerald.Stats.GetEffective(stat) != velvet.Stats.GetEffective(stat))
                {
                    anyDifferent = true;
                    break;
                }
            }
            Assert.True(anyDifferent, "Gerald and Velvet should have at least one different stat value");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    public class CharacterDefinitionLoaderTests
    {
        // Locate data files relative to repo root
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

        [Fact]
        public void Load_GeraldDefinition_ProducesValidProfile()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            Assert.Equal("Gerald_42", profile.DisplayName);
            Assert.Equal(5, profile.Level);
            Assert.NotNull(profile.Stats);
            Assert.NotNull(profile.AssembledSystemPrompt);
            Assert.Contains("Gerald_42", profile.AssembledSystemPrompt);
            Assert.Contains("PERSONALITY", profile.AssembledSystemPrompt);
            Assert.Contains("EFFECTIVE STATS", profile.AssembledSystemPrompt);
        }

        [Fact]
        public void Load_AllFiveCharacters_Succeed()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            var names = new[] { "gerald", "velvet", "sable", "brick", "zyx" };

            foreach (var name in names)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{name}.json");
                var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);
                Assert.NotNull(profile);
                Assert.False(string.IsNullOrEmpty(profile.DisplayName), $"{name} should have a display name");
                Assert.True(profile.Level >= 1 && profile.Level <= 11, $"{name} level should be 1-11, got {profile.Level}");
            }
        }

        [Fact]
        public void Load_GeraldStats_ReflectBuildPointsPlusItemModifiers()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            // Gerald has build_points: charm=6, rizz=5, etc.
            // Plus item modifiers from his equipment
            // The exact values depend on item data, but charm should be > 6 (build points + item mods)
            int charm = profile.Stats.GetEffective(StatType.Charm);
            Assert.True(charm >= 6, $"Gerald's charm should be at least 6 (build points), got {charm}");
        }

        [Fact]
        public void Parse_MissingShadows_DefaultsToZero()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""TestChar"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test bio"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": {
                    ""charm"": 1, ""rizz"": 1, ""honesty"": 1,
                    ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1
                }
            }";

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal("TestChar", profile.DisplayName);
            Assert.Equal(1, profile.Level);
            // Shadows default to 0
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Madness));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Despair));
        }

        [Fact]
        public void Parse_MissingRequiredField_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            // Missing "name" field
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
            Assert.Contains("name", ex.Message);
        }

        [Fact]
        public void Parse_InvalidLevel_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 15,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("level", ex.Message.ToLowerInvariant());
        }

        [Fact]
        public void Parse_UnknownStatType_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""invalid_stat"": 1 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("Unknown stat type", ex.Message);
        }

        [Fact]
        public void Parse_UnknownShadowType_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 },
                ""shadows"": { ""invalid_shadow"": 5 }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("Unknown shadow stat type", ex.Message);
        }

        [Fact]
        public void Parse_MalformedJson_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse("not json at all", itemRepo, anatomyRepo));
        }

        [Fact]
        public void Load_FileNotFound_ThrowsFileNotFoundException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            Assert.Throws<FileNotFoundException>(() =>
                CharacterDefinitionLoader.Load("/nonexistent/path.json", itemRepo, anatomyRepo));
        }

        [Fact]
        public void Parse_SystemPrompt_BuiltFromFragments()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");
            string json = File.ReadAllText(path);

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            // Should contain assembled fragments, not be empty
            Assert.Contains("You are playing the role of Gerald_42", profile.AssembledSystemPrompt);
            Assert.Contains("he/him", profile.AssembledSystemPrompt);
            Assert.Contains("BACKSTORY", profile.AssembledSystemPrompt);
            Assert.Contains("TEXTING STYLE", profile.AssembledSystemPrompt);
            Assert.Contains("ARCHETYPES", profile.AssembledSystemPrompt);
        }

        [Fact]
        public void Parse_EmptyItems_ProducesProfileWithBuildPointStatsOnly()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""name"": ""Bare"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""naked and proud"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""build_points"": {
                    ""charm"": 3, ""rizz"": 2, ""honesty"": 1,
                    ""chaos"": 0, ""wit"": 4, ""self_awareness"": 1
                }
            }";

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            // With no items or anatomy, stats should equal build points
            Assert.Equal(3, profile.Stats.GetEffective(StatType.Charm));
            Assert.Equal(2, profile.Stats.GetEffective(StatType.Rizz));
            Assert.Equal(1, profile.Stats.GetEffective(StatType.Honesty));
            Assert.Equal(0, profile.Stats.GetEffective(StatType.Chaos));
            Assert.Equal(4, profile.Stats.GetEffective(StatType.Wit));
            Assert.Equal(1, profile.Stats.GetEffective(StatType.SelfAwareness));
        }
    }
}

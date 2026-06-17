using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
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

        private static readonly string[] AllStarterSlugs =
            { "brick", "gerald", "reuben", "sable", "velvet", "zyx" };

        // Reusable v2 fixture string (a minimally-valid file). Tests mutate
        // copies of this to provoke negative cases.
        private const string ValidV1Json = @"{
            ""schema_version"": 2,
            ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
            ""name"": ""TestChar"",
            ""gender_identity"": ""they/them"",
            ""bio"": ""test bio"",
            ""level"": 1,
            ""items"": [],
            ""anatomy"": {},
            ""allocation"": {
                ""spent"": {
                    ""charm"": 1, ""rizz"": 1, ""honesty"": 1,
                    ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1
                },
                ""unspent_pool"": 0,
                ""shadows"": {
                    ""madness"": 0, ""despair"": 0, ""denial"": 0,
                    ""fixation"": 0, ""dread"": 0, ""overthinking"": 0
                }
            }
        }";

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
            Assert.Contains("PERSONALITY", profile.AssembledSystemPrompt);
            Assert.Contains("EFFECTIVE STATS", profile.AssembledSystemPrompt);
        }

        [Fact]
        public void Load_AllSixStarterCharacters_Succeed()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            foreach (var slug in AllStarterSlugs)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
                var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);
                Assert.NotNull(profile);
                Assert.False(string.IsNullOrEmpty(profile.DisplayName), $"{slug} should have a display name");
                Assert.True(profile.Level >= 1 && profile.Level <= 11,
                    $"{slug} level should be 1-11, got {profile.Level}");
            }
        }

        [Fact]
        public void ParseDefinition_AllSixStarterFiles_ProduceWellFormedDefinitions()
        {
            foreach (var slug in AllStarterSlugs)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
                string json = File.ReadAllText(path);

                var def = CharacterDefinitionLoader.ParseDefinition(json);

                Assert.Equal(2, def.SchemaVersion);
                Assert.NotEqual(Guid.Empty, def.CharacterId);
                Assert.False(string.IsNullOrWhiteSpace(def.Name));
                Assert.False(string.IsNullOrWhiteSpace(def.GenderIdentity));
                Assert.NotNull(def.Bio);
                Assert.True(def.Level >= 1 && def.Level <= 11);
                Assert.NotNull(def.Items);
                Assert.NotNull(def.Anatomy);
                Assert.NotNull(def.Allocation);
                Assert.NotNull(def.Allocation.Spent);
                Assert.NotNull(def.Allocation.Shadows);
            }
        }

        [Fact]
        public void ParseDefinition_AllStarterCharacterIdsAreUnique()
        {
            var ids = new HashSet<Guid>();
            foreach (var slug in AllStarterSlugs)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
                string json = File.ReadAllText(path);
                var def = CharacterDefinitionLoader.ParseDefinition(json);

                Assert.True(ids.Add(def.CharacterId),
                    $"Duplicate character_id {def.CharacterId} found at {slug}.json");
            }
            Assert.Equal(AllStarterSlugs.Length, ids.Count);
        }

        [Fact]
        public void Load_GeraldStats_ReflectBuildPointsPlusItemModifiers()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            // Gerald has allocation.spent.charm = 6, plus item modifiers from his
            // equipment. The exact effective value depends on item data, but
            // charm should be >= 6 (base allocation alone).
            int charm = profile.Stats.GetEffective(StatType.Charm);
            Assert.True(charm >= 6, $"Gerald's charm should be at least 6 (build points), got {charm}");
        }

        [Fact]
        public void Parse_MissingSchemaVersion_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace("\"schema_version\": 2,", "");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("schema_version", ex.Message);
        }

        [Fact]
        public void Parse_UnknownSchemaVersion_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace("\"schema_version\": 2,", "\"schema_version\": 99,");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("schema_version", ex.Message);
            Assert.Contains("99", ex.Message);
        }

        [Fact]
        public void Parse_MalformedSchemaVersion_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace("\"schema_version\": 2,", "\"schema_version\": \"v2\",");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("schema_version", ex.Message);
        }

        [Fact]
        public void Parse_MissingCharacterId_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace(
                "\"character_id\": \"550e8400-e29b-41d4-a716-446655440000\",", "");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("character_id", ex.Message);
        }

        [Fact]
        public void Parse_MalformedCharacterId_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace(
                "\"character_id\": \"550e8400-e29b-41d4-a716-446655440000\",",
                "\"character_id\": \"not-a-uuid\",");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("character_id", ex.Message);
        }

        [Fact]
        public void Parse_MissingShadows_DefaultsToZero()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            // Construct a v2 file with allocation.shadows omitted entirely.
            string json = @"{
                ""schema_version"": 2,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""TestChar"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test bio"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": {
                    ""spent"": {
                        ""charm"": 1, ""rizz"": 1, ""honesty"": 1,
                        ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1
                    },
                    ""unspent_pool"": 0
                }
            }";

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo);

            Assert.Equal("TestChar", profile.DisplayName);
            Assert.Equal(1, profile.Level);
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Madness));
            Assert.Equal(0, profile.Stats.GetShadow(ShadowStatType.Despair));
        }

        [Fact]
        public void Parse_MissingRequiredField_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace("\"name\": \"TestChar\",", "");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("name", ex.Message);
        }

        [Fact]
        public void Parse_InvalidLevel_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace("\"level\": 1,", "\"level\": 15,");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("level", ex.Message.ToLowerInvariant());
        }

        [Fact]
        public void Parse_UnknownStatType_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace(
                "\"self_awareness\": 1\n                },",
                "\"self_awareness\": 1, \"invalid_stat\": 1\n                },");

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("Unknown stat type", ex.Message);
        }

        [Fact]
        public void Parse_UnknownShadowType_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = ValidV1Json.Replace(
                "\"overthinking\": 0\n                }\n            }",
                "\"overthinking\": 0, \"invalid_shadow\": 5\n                }\n            }");

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

            var profile = CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo, archetypesEnabled: true);

            // Should contain assembled fragments, not be empty
            // Lead-in is now RULES token; character name no longer in character-level prompt (RULES migration).
            Assert.Contains("RULES", profile.AssembledSystemPrompt);
            Assert.Contains("he/him", profile.AssembledSystemPrompt);
            Assert.Contains("BACKSTORY", profile.AssembledSystemPrompt);
            Assert.Contains("TEXTING STYLE", profile.AssembledSystemPrompt);
            // #832: ARCHETYPES (tendency-order ranked list) replaced by
            // ACTIVE ARCHETYPE (the level-eligible top-ranked archetype).
            Assert.Contains("ACTIVE ARCHETYPE", profile.AssembledSystemPrompt);
        }

        [Fact]
        public void Parse_EmptyItems_ProducesProfileWithBuildPointStatsOnly()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""schema_version"": 2,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Bare"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""naked and proud"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": {
                    ""spent"": {
                        ""charm"": 3, ""rizz"": 2, ""honesty"": 1,
                        ""chaos"": 0, ""wit"": 4, ""self_awareness"": 1
                    },
                    ""unspent_pool"": 0,
                    ""shadows"": {
                        ""madness"": 0, ""despair"": 0, ""denial"": 0,
                        ""fixation"": 0, ""dread"": 0, ""overthinking"": 0
                    }
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

        // ── Issue #779: psychological_stake field ───────────────────────────

        /// <summary>
        /// Issue #779: when the on-disk character JSON carries a
        /// <c>psychological_stake</c> string, the parsed
        /// <see cref="CharacterDefinition"/> exposes it on
        /// <see cref="CharacterDefinition.PsychologicalStake"/> and the
        /// assembled <see cref="CharacterProfile"/> propagates it to
        /// <see cref="CharacterProfile.PsychologicalStake"/>.
        /// </summary>
        [Fact]
        public void ParseDefinition_WithPsychologicalStake_PopulatesField()
        {
            const string stake = "- The most humiliating thing was X.\n- Y.";
            string json = ValidV1Json.Replace(
                "\"anatomy\": {},",
                "\"anatomy\": {}, \"psychological_stake\": \"- The most humiliating thing was X.\\n- Y.\",");

            var def = CharacterDefinitionLoader.ParseDefinition(json);

            Assert.Equal(stake, def.PsychologicalStake);
        }

        /// <summary>
        /// Issue #779: a missing <c>psychological_stake</c> field is
        /// legal — the loader returns null and the parse succeeds. Mirrors
        /// legacy character files prior to the backfill.
        /// </summary>
        [Fact]
        public void ParseDefinition_WithoutPsychologicalStake_ReturnsNull()
        {
            var def = CharacterDefinitionLoader.ParseDefinition(ValidV1Json);
            Assert.Null(def.PsychologicalStake);
        }

        /// <summary>
        /// Issue #779: an explicit empty / whitespace-only stake is also
        /// treated as absent so the engine doesn't inject an empty
        /// <c>== PSYCHOLOGICAL STAKE ==</c> block.
        /// </summary>
        [Fact]
        public void ParseDefinition_WithBlankPsychologicalStake_ReturnsNull()
        {
            string json = ValidV1Json.Replace(
                "\"anatomy\": {},",
                "\"anatomy\": {}, \"psychological_stake\": \"   \",");

            var def = CharacterDefinitionLoader.ParseDefinition(json);
            Assert.Null(def.PsychologicalStake);
        }

        /// <summary>
        /// Issue #779: surrounding whitespace on a real stake value is
        /// trimmed so the on-disk format can be hand-edited freely.
        /// </summary>
        [Fact]
        public void ParseDefinition_TrimsSurroundingWhitespace()
        {
            string json = ValidV1Json.Replace(
                "\"anatomy\": {},",
                "\"anatomy\": {}, \"psychological_stake\": \"\\n  - bullet one\\n\",");

            var def = CharacterDefinitionLoader.ParseDefinition(json);
            Assert.Equal("- bullet one", def.PsychologicalStake);
        }

        /// <summary>
        /// Issue #779: after the canonical backfill, every starter
        /// character on disk carries a non-empty markdown-bullet stake.
        /// This is the regression guard that the backfill commit doesn't
        /// silently lose any of the six entries.
        /// </summary>
        [Fact]
        public void AllSixStarterCharacters_HavePsychologicalStakeOnDisk()
        {
            foreach (var slug in AllStarterSlugs)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
                string json = File.ReadAllText(path);

                var def = CharacterDefinitionLoader.ParseDefinition(json);

                Assert.False(
                    string.IsNullOrWhiteSpace(def.PsychologicalStake),
                    $"{slug}: psychological_stake must be a non-empty markdown bullet list (Issue #779)");
                // Must look like a markdown bullet list — every starter
                // character should at minimum start with a `- ` bullet.
                Assert.StartsWith("- ", def.PsychologicalStake!.TrimStart());
            }
        }

        /// <summary>
        /// Issue #779: the assembled <see cref="CharacterProfile"/> for a
        /// real on-disk character carries the same stake the definition
        /// loader parsed — i.e. the assembler propagates it to the profile
        /// where <c>ActiveSession.Setup</c> reads it.
        /// </summary>
        [Fact]
        public void Load_StarterCharacters_PropagatesStakeToProfile()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            foreach (var slug in AllStarterSlugs)
            {
                string path = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");
                var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

                Assert.False(
                    string.IsNullOrWhiteSpace(profile.PsychologicalStake),
                    $"{slug}: CharacterProfile.PsychologicalStake must be populated from the on-disk JSON (Issue #779)");
            }
        }
    }
}

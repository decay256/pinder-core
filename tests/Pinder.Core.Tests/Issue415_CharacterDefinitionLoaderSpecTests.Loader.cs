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
    public partial class Issue415_CharacterDefinitionLoaderSpecTests
    {
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

            // v1 file with name field elided.
            string json = @"{
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": { ""spent"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }, ""unspent_pool"": 0, ""shadows"": { ""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0 } }
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
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Test"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": { ""spent"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }, ""unspent_pool"": 0, ""shadows"": { ""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0 } }
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
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": { ""spent"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }, ""unspent_pool"": 0, ""shadows"": { ""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0 } }
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
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""items"": [],
                ""anatomy"": {},
                ""allocation"": { ""spent"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }, ""unspent_pool"": 0, ""shadows"": { ""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0 } }
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
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""anatomy"": {},
                ""allocation"": { ""spent"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }, ""unspent_pool"": 0, ""shadows"": { ""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0 } }
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
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""allocation"": { ""spent"": { ""charm"": 1, ""rizz"": 1, ""honesty"": 1, ""chaos"": 1, ""wit"": 1, ""self_awareness"": 1 }, ""unspent_pool"": 0, ""shadows"": { ""madness"": 0, ""despair"": 0, ""denial"": 0, ""fixation"": 0, ""dread"": 0, ""overthinking"": 0 } }
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("anatomy", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // v1: "build_points" was renamed to "allocation.spent". Missing the
        // entire allocation block now triggers the corresponding format error.
        [Fact]
        public void Error_MissingAllocation_ThrowsFormatException()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            string json = @"{
                ""schema_version"": 1,
                ""character_id"": ""550e8400-e29b-41d4-a716-446655440000"",
                ""name"": ""Test"",
                ""gender_identity"": ""they/them"",
                ""bio"": ""test"",
                ""level"": 1,
                ""items"": [],
                ""anatomy"": {}
            }";

            var ex = Assert.Throws<FormatException>(() =>
                CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo));
            Assert.Contains("allocation", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    // A test-only model representing the expected migrated schema with backstory_categories.
    public class MigratedCharacterDefinition
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("gender_identity")]
        public string GenderIdentity { get; set; } = string.Empty;

        [JsonPropertyName("bio")]
        public string Bio { get; set; } = string.Empty;

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new();

        [JsonPropertyName("anatomy")]
        public Dictionary<string, float> Anatomy { get; set; } = new();

        [JsonPropertyName("allocation")]
        public MigratedAllocationBlock Allocation { get; set; } = new();

        [JsonPropertyName("psychological_stake")]
        public string? PsychologicalStake { get; set; }

        [JsonPropertyName("backstory_categories")]
        public Dictionary<string, BackstoryFact>? BackstoryCategories { get; set; }
    }

    public class MigratedAllocationBlock
    {
        [JsonPropertyName("spent")]
        public Dictionary<string, int> Spent { get; set; } = new();

        [JsonPropertyName("unspent_pool")]
        public int UnspentPool { get; set; }

        [JsonPropertyName("shadows")]
        public Dictionary<string, int> Shadows { get; set; } = new();
    }

    public class BackstoryValidatorTests
    {
        private static readonly string[] CharacterFiles = new[]
        {
            "gerald.json",
            "brick.json",
            "reuben.json",
            "sable.json",
            "velvet.json",
            "zyx.json"
        };

        private static string GetCharacterPath(string filename)
        {
            // Resolve relative to the repository root where 'dotnet test' runs.
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../data/characters", filename);
        }

        [Theory]
        [InlineData("gerald.json")]
        [InlineData("brick.json")]
        [InlineData("reuben.json")]
        [InlineData("sable.json")]
        [InlineData("velvet.json")]
        [InlineData("zyx.json")]
        public void BackstoryValidator_PassesWith100PercentSuccess(string filename)
        {
            string path = GetCharacterPath(filename);
            string json = File.ReadAllText(path);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var character = JsonSerializer.Deserialize<MigratedCharacterDefinition>(json, options);

            Assert.NotNull(character);

            // This will fail because the JSON files have not been migrated yet.
            Assert.True(Pinder.Core.Characters.BackstoryValidator.Validate(character.BackstoryCategories!), $"Validation failed for {filename}. Ensure backstory_categories is present and has 20 items.");
        }

        [Theory]
        [InlineData("gerald.json")]
        [InlineData("brick.json")]
        [InlineData("reuben.json")]
        [InlineData("sable.json")]
        [InlineData("velvet.json")]
        [InlineData("zyx.json")]
        public void BackstorySchema_DeserializesAndSerializesCorrectly(string filename)
        {
            string path = GetCharacterPath(filename);
            string json = File.ReadAllText(path);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
            var character = JsonSerializer.Deserialize<MigratedCharacterDefinition>(json, options);

            Assert.NotNull(character);

            // Should be correctly populated (fails here because files aren't migrated)
            Assert.NotNull(character.BackstoryCategories);
            Assert.Equal(20, character.BackstoryCategories.Count);

            // Verify it serializes back without throwing
            string serialized = JsonSerializer.Serialize(character, options);
            Assert.Contains("backstory_categories", serialized);
        }

        [Theory]
        [InlineData("gerald.json")]
        [InlineData("brick.json")]
        [InlineData("reuben.json")]
        [InlineData("sable.json")]
        [InlineData("velvet.json")]
        [InlineData("zyx.json")]
        public void StandardCharacterSheetLoading_ContinuesToExecuteWithoutExceptions(string filename)
        {
            // Verify that the existing Pinder.Core logic can load the migrated characters without issues
            string path = GetCharacterPath(filename);
            string json = File.ReadAllText(path);

            // CharacterDefinitionLoader parses the file. If backstory_categories breaks it, this will throw.
            // Pinder.SessionSetup is in another assembly but we can use the parser if referenced, 
            // or we can test parsing directly. 
            // Wait, Pinder.SessionSetup.CharacterDefinitionLoader is internal or public? Let's check.
            
            // Actually, we can just use Pinder.SessionSetup.CharacterDefinitionLoader.ParseDefinition(json)
            var ex = Record.Exception(() => Pinder.SessionSetup.CharacterDefinitionLoader.ParseDefinition(json));
            Assert.Null(ex);
        }
    }
}

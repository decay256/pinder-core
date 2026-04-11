using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class GameDefinitionTests
    {
        private const string ValidYaml = @"
name: ""TestGame""
vision: |
  A test game vision.
world_description: |
  A test world description.
player_role_description: |
  Player role.
opponent_role_description: |
  Opponent role.
meta_contract: |
  Meta contract text.
writing_rules: |
  Writing rules text.
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";

        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var gd = new GameDefinition("N", "V", "W", "P", "O", "M", "WR");
            Assert.Equal("N", gd.Name);
            Assert.Equal("V", gd.Vision);
            Assert.Equal("W", gd.WorldDescription);
            Assert.Equal("P", gd.PlayerRoleDescription);
            Assert.Equal("O", gd.OpponentRoleDescription);
            Assert.Equal("M", gd.MetaContract);
            Assert.Equal("WR", gd.WritingRules);
        }

        [Fact]
        public void Constructor_ThrowsOnNullName()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition(null!, "V", "W", "P", "O", "M", "WR"));
        }

        [Fact]
        public void Constructor_ThrowsOnNullVision()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", null!, "W", "P", "O", "M", "WR"));
        }

        [Fact]
        public void Constructor_AllowsEmptyStrings()
        {
            var gd = new GameDefinition("", "", "", "", "", "", "");
            Assert.Equal("", gd.Name);
        }

        [Fact]
        public void LoadFrom_ValidYaml_ParsesAllFields()
        {
            var gd = GameDefinition.LoadFrom(ValidYaml);
            Assert.Equal("TestGame", gd.Name);
            Assert.Contains("test game vision", gd.Vision);
            Assert.Contains("test world description", gd.WorldDescription);
            Assert.Contains("Player role", gd.PlayerRoleDescription);
            Assert.Contains("Opponent role", gd.OpponentRoleDescription);
            Assert.Contains("Meta contract", gd.MetaContract);
            Assert.Contains("Writing rules", gd.WritingRules);
        }

        [Fact]
        public void LoadFrom_NullContent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GameDefinition.LoadFrom(null!));
        }

        [Fact]
        public void LoadFrom_InvalidYaml_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom("{{invalid yaml"));
            Assert.Contains("YAML", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadFrom_MissingKey_ThrowsFormatException()
        {
            var yaml = "name: Test\n";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("vision", ex.Message);
        }

        [Fact]
        public void LoadFrom_NullValue_ThrowsFormatException()
        {
            var yaml = @"
name: Test
vision: ~
world_description: wd
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: w
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("vision", ex.Message);
        }

        [Fact]
        public void LoadFrom_ExtraKeys_AreIgnored()
        {
            var yaml = @"
name: Test
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
extra_field: should be ignored
another: also ignored
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal("Test", gd.Name);
        }

        [Fact]
        public void PinderDefaults_HasAllFields()
        {
            var gd = GameDefinition.PinderDefaults;
            Assert.Equal("Pinder", gd.Name);
            Assert.Contains("comedy dating RPG", gd.Vision);
            Assert.Contains("sentient penis", gd.Vision);
            Assert.Contains("dating server", gd.WorldDescription);
            Assert.Contains("player character", gd.PlayerRoleDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("opponent", gd.OpponentRoleDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("break character", gd.MetaContract, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("texting register", gd.WritingRules, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadFrom_RealGameDefinitionYaml_Parses()
        {
            // Load the actual game-definition.yaml from the repo
            var yamlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "data", "game-definition.yaml");
            if (!System.IO.File.Exists(yamlPath))
                return; // Skip if file not available in test environment

            var content = System.IO.File.ReadAllText(yamlPath);
            var gd = GameDefinition.LoadFrom(content);
            Assert.Equal("Pinder", gd.Name);
            Assert.Contains("comedy dating RPG", gd.Vision);
        }
    }
}

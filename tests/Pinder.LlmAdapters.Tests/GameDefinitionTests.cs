using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class GameDefinitionTests
    {
        private const string ValidYaml = @"
name: ""TestGame""
game_master_prompt: |
  A test game master prompt.
  With multiple lines of guidance.
player_avatar_role_description: |
  Player role.
datee_role_description: |
  Datee role.
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
            var gd = new GameDefinition("N", "GM", "P", "O");
            Assert.Equal("N", gd.Name);
            Assert.Equal("GM", gd.GameMasterPrompt);
            Assert.Equal("P", gd.PlayerAvatarRoleDescription);
            Assert.Equal("O", gd.DateeRoleDescription);
        }

        [Fact]
        public void Constructor_ThrowsOnNullName()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition(null!, "GM", "P", "O"));
        }

        [Fact]
        public void Constructor_ThrowsOnNullGameMasterPrompt()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", null!, "P", "O"));
        }

        [Fact]
        public void Constructor_AllowsEmptyStrings()
        {
            var gd = new GameDefinition("", "", "", "");
            Assert.Equal("", gd.Name);
        }

        [Fact]
        public void LoadFrom_ValidYaml_ParsesAllFields()
        {
            var gd = GameDefinition.LoadFrom(ValidYaml);
            Assert.Equal("TestGame", gd.Name);
            Assert.Contains("test game master prompt", gd.GameMasterPrompt);
            Assert.Contains("multiple lines of guidance", gd.GameMasterPrompt);
            Assert.Contains("Player role", gd.PlayerAvatarRoleDescription);
            Assert.Contains("Datee role", gd.DateeRoleDescription);
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
            var ex = Assert.Throws<InvalidOperationException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("horniness_time_modifiers", ex.Message);
        }

        [Fact]
        public void LoadFrom_NullValue_ThrowsFormatException()
        {
            var yaml = @"
name: Test
game_master_prompt: ~
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("game_master_prompt", ex.Message);
        }

        [Fact]
        public void LoadFrom_ExtraYamlKeys_AreIgnored()
        {
            var yaml = @"
name: Test
game_master_prompt: gm
player_avatar_role_description: p
datee_role_description: o
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
            Assert.Contains("== GAME MASTER ==", gd.GameMasterPrompt);
            Assert.Contains("comedy dating RPG", gd.GameMasterPrompt);
            Assert.Contains("player character", gd.PlayerAvatarRoleDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("datee", gd.DateeRoleDescription, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("dating RPG", gd.GameMasterPrompt);
        }

        [Fact]
        public void LoadFrom_MissingNewKeyPlayerAvatarRoleDescription_ThrowsFormatException_Regression1165()
        {
            // 1. Old key alone should throw (fallback removed)
            var yamlOldOnly = @"
name: Test
game_master_prompt: gm
player_role_description: old_role
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() => GameDefinition.LoadFrom(yamlOldOnly));
            Assert.Contains("player_avatar_role_description", ex.Message);

            // 2. New key alone parses
            var yamlNewOnly = @"
name: Test
game_master_prompt: gm
player_avatar_role_description: new_role
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var gdNew = GameDefinition.LoadFrom(yamlNewOnly);
            Assert.Equal("new_role", gdNew.PlayerAvatarRoleDescription);
        }
    }
}

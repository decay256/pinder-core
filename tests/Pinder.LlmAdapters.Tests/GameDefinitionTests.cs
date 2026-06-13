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
player_avatar_role_description: |
  Player role.
datee_role_description: |
  Datee role.
narrative_doctrine: |
  Meta contract text.
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
            var gd = new GameDefinition("N", "V", "W", "P", "O", "ND");
            Assert.Equal("N", gd.Name);
            Assert.Equal("V", gd.Vision);
            Assert.Equal("W", gd.WorldDescription);
            Assert.Equal("P", gd.PlayerAvatarRoleDescription);
            Assert.Equal("O", gd.DateeRoleDescription);
            Assert.Equal("ND", gd.NarrativeDoctrine);
        }

        [Fact]
        public void Constructor_ThrowsOnNullName()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition(null!, "V", "W", "P", "O", "ND"));
        }

        [Fact]
        public void Constructor_ThrowsOnNullVision()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", null!, "W", "P", "O", "ND"));
        }

        [Fact]
        public void Constructor_AllowsEmptyStrings()
        {
            var gd = new GameDefinition("", "", "", "", "", "");
            Assert.Equal("", gd.Name);
        }

        [Fact]
        public void LoadFrom_ValidYaml_ParsesAllFields()
        {
            var gd = GameDefinition.LoadFrom(ValidYaml);
            Assert.Equal("TestGame", gd.Name);
            Assert.Contains("test game vision", gd.Vision);
            Assert.Contains("test world description", gd.WorldDescription);
            Assert.Contains("Player role", gd.PlayerAvatarRoleDescription);
            Assert.Contains("Datee role", gd.DateeRoleDescription);
            Assert.Contains("Meta contract", gd.NarrativeDoctrine);
            Assert.Contains("Writing rules", gd.NarrativeDoctrine);
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
vision: ~
world_description: wd
player_avatar_role_description: p
datee_role_description: o
narrative_doctrine: m
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
        public void LoadFrom_ExtraYamlKeys_AreIgnored()
        {
            var yaml = @"
name: Test
vision: v
world_description: w
player_avatar_role_description: p
datee_role_description: o
narrative_doctrine: nd
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
            Assert.Contains("player character", gd.PlayerAvatarRoleDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("datee", gd.DateeRoleDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("break character", gd.NarrativeDoctrine, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("texting register", gd.NarrativeDoctrine, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("RPG similar to D&D", gd.Vision);
        }

        [Fact]
        public void LoadFrom_LegacyYamlKeys_BackwardCompatibility_Regression1133()
        {
            // 1. Only old keys
            var yamlOld = @"
name: Test
vision: v
world_description: w
player_role_description: old_role
datee_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
player_probing: old_probing
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var gdOld = GameDefinition.LoadFrom(yamlOld);
            Assert.Equal("old_role", gdOld.PlayerAvatarRoleDescription);
            Assert.Equal("old_probing", gdOld.PlayerAvatarProbing);

            // 2. Only new keys
            var yamlNew = @"
name: Test
vision: v
world_description: w
player_avatar_role_description: new_role
datee_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
player_avatar_probing: new_probing
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var gdNew = GameDefinition.LoadFrom(yamlNew);
            Assert.Equal("new_role", gdNew.PlayerAvatarRoleDescription);
            Assert.Equal("new_probing", gdNew.PlayerAvatarProbing);

            // 3. Both keys present - prefers new
            var yamlBoth = @"
name: Test
vision: v
world_description: w
player_role_description: old_role
player_avatar_role_description: new_role
datee_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
player_probing: old_probing
player_avatar_probing: new_probing
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var gdBoth = GameDefinition.LoadFrom(yamlBoth);
            Assert.Equal("new_role", gdBoth.PlayerAvatarRoleDescription);
            Assert.Equal("new_probing", gdBoth.PlayerAvatarProbing);
            
            // 4. Fallback behavior handles missing key gracefully
            var yamlMissing = @"
name: Test
vision: v
world_description: w
datee_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() => GameDefinition.LoadFrom(yamlMissing));
            Assert.Contains("player_avatar_role_description", ex.Message);
        }
    }
}

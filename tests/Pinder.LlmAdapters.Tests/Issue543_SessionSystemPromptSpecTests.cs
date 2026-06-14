using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #543: Build session system prompt.
    /// Tests verify behavior from docs/specs/issue-543-spec.md.
    ///
    /// #1153: the GM base collapsed into a single <c>game_master_prompt</c>
    /// field, so the constructor/parser shape is (name, gameMasterPrompt,
    /// playerAvatarRoleDescription, dateeRoleDescription, ...).
    /// </summary>
    public partial class Issue543_SessionSystemPromptSpecTests
    {
        #region AC1: GameDefinition class with required fields

        // What: AC1 — GameDefinition class with read-only string properties
        // Mutation: would catch if any property was omitted or mapped to wrong constructor param
        [Fact]
        public void GameDefinition_Constructor_SetsAllProperties()
        {
            var gd = new GameDefinition(
                "MyGame", "GmPrompt1", "Player1", "Datee1");

            Assert.Equal("MyGame", gd.Name);
            Assert.Equal("GmPrompt1", gd.GameMasterPrompt);
            Assert.Equal("Player1", gd.PlayerAvatarRoleDescription);
            Assert.Equal("Datee1", gd.DateeRoleDescription);
        }

        // What: AC1 — constructor throws ArgumentNullException for null name
        // Mutation: would catch if null check on name was removed
        [Fact]
        public void GameDefinition_Constructor_NullName_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition(null!, "GM", "P", "O"));
            Assert.Equal("name", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null gameMasterPrompt
        // Mutation: would catch if null check on gameMasterPrompt was removed
        [Fact]
        public void GameDefinition_Constructor_NullGameMasterPrompt_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", null!, "P", "O"));
            Assert.Equal("gameMasterPrompt", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null playerAvatarRoleDescription
        // Mutation: would catch if null check on playerAvatarRoleDescription was removed
        [Fact]
        public void GameDefinition_Constructor_NullPlayerAvatarRoleDescription_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", "GM", null!, "O"));
            Assert.Equal("playerAvatarRoleDescription", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null dateeRoleDescription
        // Mutation: would catch if null check on dateeRoleDescription was removed
        [Fact]
        public void GameDefinition_Constructor_NullDateeRoleDescription_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", "GM", "P", null!));
            Assert.Equal("dateeRoleDescription", ex.ParamName);
        }

        // What: Edge case — empty strings are allowed (not null)
        // Mutation: would catch if empty string was rejected like null
        [Fact]
        public void GameDefinition_Constructor_AllowsEmptyStrings()
        {
            var gd = new GameDefinition("", "", "", "");
            Assert.Equal("", gd.Name);
            Assert.Equal("", gd.GameMasterPrompt);
            Assert.Equal("", gd.PlayerAvatarRoleDescription);
            Assert.Equal("", gd.DateeRoleDescription);
        }

        #endregion

        #region AC2: GameDefinition.LoadFrom(yamlContent) parses YAML

        private const string FullValidYaml = @"
name: ""Pinder""
game_master_prompt: |
  A comedy dating RPG where players are sentient penises
  swiping on a Tinder-like app called Pinder.
  Never break character. Write in texting register.
player_avatar_role_description: |
  You are the player's character.
datee_role_description: |
  You are the datee.
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";

        // What: AC2 — LoadFrom parses valid YAML and maps the keys correctly
        // Mutation: would catch if any key mapping was wrong
        [Fact]
        public void LoadFrom_ValidYaml_MapsAllKeysToProperties()
        {
            var gd = GameDefinition.LoadFrom(FullValidYaml);

            Assert.Equal("Pinder", gd.Name);
            Assert.Contains("comedy dating RPG", gd.GameMasterPrompt);
            Assert.Contains("sentient penises", gd.GameMasterPrompt);
            Assert.Contains("break character", gd.GameMasterPrompt);
            Assert.Contains("texting register", gd.GameMasterPrompt);
            Assert.Contains("player's character", gd.PlayerAvatarRoleDescription);
            Assert.Contains("datee", gd.DateeRoleDescription);
        }

        // What: AC2 — LoadFrom throws ArgumentNullException for null input
        // Mutation: would catch if null guard was removed
        [Fact]
        public void LoadFrom_NullContent_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                GameDefinition.LoadFrom(null!));
        }

        // What: Error condition — unparseable YAML throws FormatException
        // Mutation: would catch if invalid YAML was silently handled
        [Fact]
        public void LoadFrom_InvalidYaml_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom("{{invalid yaml content"));
            Assert.True(
                ex.Message.Contains("YAML", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase),
                $"FormatException message should mention YAML or parse, got: {ex.Message}");
        }

        // What: Error condition — missing required key throws FormatException naming the key
        // Mutation: would catch if missing key validation was skipped
        [Fact]
        public void LoadFrom_MissingGameMasterPromptKey_ThrowsFormatExceptionNamingKey()
        {
            var yaml = @"
name: Test
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
            Assert.Contains("game_master_prompt", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Error condition — missing name key throws FormatException naming the key
        // Mutation: would catch if only some missing keys were validated
        [Fact]
        public void LoadFrom_MissingNameKey_ThrowsFormatExceptionNamingKey()
        {
            var yaml = @"
game_master_prompt: gm
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
            Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Error condition — null YAML value throws FormatException
        // Mutation: would catch if null values were coerced to empty string instead of rejected
        [Fact]
        public void LoadFrom_NullYamlValue_ThrowsFormatException()
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
            Assert.Contains("game_master_prompt", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Edge case — extra/unknown YAML keys are tolerated
        // Mutation: would catch if extra keys caused a parse error
        [Fact]
        public void LoadFrom_ExtraYamlKeys_AreIgnored()
        {
            var yaml = @"
name: TestGame
game_master_prompt: gm
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
extra_unknown_key: should be silently ignored
another_one: also ignored
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal("TestGame", gd.Name);
            Assert.Equal("gm", gd.GameMasterPrompt);
        }

        // What: Edge case — YAML block scalar preserves newlines (no trimming)
        // Mutation: would catch if implementation trimmed block scalar output
        [Fact]
        public void LoadFrom_BlockScalar_PreservesNewlines()
        {
            var yaml = @"
name: Test
game_master_prompt: |
  Line one.
  Line two.
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Contains("Line one.", gd.GameMasterPrompt);
            Assert.Contains("Line two.", gd.GameMasterPrompt);
            // Block scalar | preserves newlines between lines
            Assert.Contains("\n", gd.GameMasterPrompt);
        }

        #endregion
    }
}

using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #543: Build session system prompt.
    /// Tests verify behavior from docs/specs/issue-543-spec.md.
    /// </summary>
    public partial class Issue543_SessionSystemPromptSpecTests
    {
        #region AC1: GameDefinition class with all fields

        // What: AC1 — GameDefinition sealed class with read-only string properties
        // Mutation: would catch if any property was omitted or mapped to wrong constructor param
        [Fact]
        public void GameDefinition_Constructor_SetsAll7Properties()
        {
            var gd = new GameDefinition(
                "MyGame", "Vision1", "World1", "Player1", "Opponent1", "Doctrine1");

            Assert.Equal("MyGame", gd.Name);
            Assert.Equal("Vision1", gd.Vision);
            Assert.Equal("World1", gd.WorldDescription);
            Assert.Equal("Player1", gd.PlayerRoleDescription);
            Assert.Equal("Opponent1", gd.OpponentRoleDescription);
            Assert.Equal("Doctrine1", gd.NarrativeDoctrine);
        }

        // What: AC1 — constructor throws ArgumentNullException for null name
        // Mutation: would catch if null check on name was removed
        [Fact]
        public void GameDefinition_Constructor_NullName_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition(null!, "V", "W", "P", "O", "ND"));
            Assert.Equal("name", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null vision
        // Mutation: would catch if null check on vision was removed
        [Fact]
        public void GameDefinition_Constructor_NullVision_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", null!, "W", "P", "O", "ND"));
            Assert.Equal("vision", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null worldDescription
        // Mutation: would catch if null check on worldDescription was removed
        [Fact]
        public void GameDefinition_Constructor_NullWorldDescription_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", "V", null!, "P", "O", "ND"));
            Assert.Equal("worldDescription", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null playerRoleDescription
        // Mutation: would catch if null check on playerRoleDescription was removed
        [Fact]
        public void GameDefinition_Constructor_NullPlayerRoleDescription_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", "V", "W", null!, "O", "ND"));
            Assert.Equal("playerRoleDescription", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null opponentRoleDescription
        // Mutation: would catch if null check on opponentRoleDescription was removed
        [Fact]
        public void GameDefinition_Constructor_NullOpponentRoleDescription_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", "V", "W", "P", null!, "ND"));
            Assert.Equal("opponentRoleDescription", ex.ParamName);
        }

        // What: AC1 — constructor throws ArgumentNullException for null narrativeDoctrine
        // Mutation: would catch if null check on narrativeDoctrine was removed
        [Fact]
        public void GameDefinition_Constructor_NullMetaContract_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new GameDefinition("N", "V", "W", "P", "O", null!));
            Assert.Equal("narrativeDoctrine", ex.ParamName);
        }

        // What: Edge case — empty strings are allowed (not null)
        // Mutation: would catch if empty string was rejected like null
        [Fact]
        public void GameDefinition_Constructor_AllowsEmptyStrings()
        {
            var gd = new GameDefinition("", "", "", "", "", "");
            Assert.Equal("", gd.Name);
            Assert.Equal("", gd.Vision);
            Assert.Equal("", gd.WorldDescription);
            Assert.Equal("", gd.PlayerRoleDescription);
            Assert.Equal("", gd.OpponentRoleDescription);
            Assert.Equal("", gd.NarrativeDoctrine);
        }

        #endregion

        #region AC2: GameDefinition.LoadFrom(yamlContent) parses YAML

        private const string FullValidYaml = @"
name: ""Pinder""
vision: |
  A comedy dating RPG where players are sentient penises
  swiping on a Tinder-like app called Pinder.
world_description: |
  The world of Pinder is absurdist. Characters are anatomical
  beings navigating modern dating culture.
player_role_description: |
  You are the player's character.
opponent_role_description: |
  You are the opponent.
narrative_doctrine: |
  Never break character.
  Write in texting register.
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";

        // What: AC2 — LoadFrom parses valid YAML and maps all 7 keys correctly
        // Mutation: would catch if any key mapping (e.g. world_description → WorldDescription) was wrong
        [Fact]
        public void LoadFrom_ValidYaml_MapsAllKeysToProperties()
        {
            var gd = GameDefinition.LoadFrom(FullValidYaml);

            Assert.Equal("Pinder", gd.Name);
            Assert.Contains("comedy dating RPG", gd.Vision);
            Assert.Contains("sentient penises", gd.Vision);
            Assert.Contains("absurdist", gd.WorldDescription);
            Assert.Contains("player's character", gd.PlayerRoleDescription);
            Assert.Contains("opponent", gd.OpponentRoleDescription);
            Assert.Contains("break character", gd.NarrativeDoctrine);
            Assert.Contains("texting register", gd.NarrativeDoctrine);
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
            // Spec says message contains "YAML" or "parse"
            Assert.True(
                ex.Message.Contains("YAML", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase),
                $"FormatException message should mention YAML or parse, got: {ex.Message}");
        }

        // What: Error condition — missing required key throws FormatException naming the key
        // Mutation: would catch if missing key validation was skipped
        [Fact]
        public void LoadFrom_MissingVisionKey_ThrowsFormatExceptionNamingKey()
        {
            var yaml = @"
name: Test
world_description: w
player_role_description: p
opponent_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("vision", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Error condition — missing name key throws FormatException naming the key
        // Mutation: would catch if only some missing keys were validated
        [Fact]
        public void LoadFrom_MissingNameKey_ThrowsFormatExceptionNamingKey()
        {
            var yaml = @"
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
narrative_doctrine: nd
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
vision: ~
world_description: wd
player_role_description: p
opponent_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var ex = Assert.Throws<FormatException>(() =>
                GameDefinition.LoadFrom(yaml));
            Assert.Contains("vision", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: Edge case — extra/unknown YAML keys are tolerated
        // Mutation: would catch if extra keys caused a parse error
        [Fact]
        public void LoadFrom_ExtraYamlKeys_AreIgnored()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
narrative_doctrine: nd
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
            Assert.Equal("v", gd.Vision);
        }

        // What: Edge case — YAML block scalar preserves newlines (no trimming)
        // Mutation: would catch if implementation trimmed block scalar output
        [Fact]
        public void LoadFrom_BlockScalar_PreservesNewlines()
        {
            var yaml = @"
name: Test
vision: |
  Line one.
  Line two.
world_description: w
player_role_description: p
opponent_role_description: o
narrative_doctrine: nd
global_dc_bias: 0
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Contains("Line one.", gd.Vision);
            Assert.Contains("Line two.", gd.Vision);
            // Block scalar | preserves newlines between lines
            Assert.Contains("\n", gd.Vision);
        }

        #endregion
    }
}
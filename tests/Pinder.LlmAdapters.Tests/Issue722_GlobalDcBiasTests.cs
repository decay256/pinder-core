using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue722_GlobalDcBiasTests
    {
        private const string BaseYaml = @"
name: ""TestGame""
game_master_prompt: |
  A test game master prompt.
  With writing rules text.
player_avatar_role_description: |
  Player role.
datee_role_description: |
  Datee role.
horniness_time_modifiers:
  morning: 3
  afternoon: 0
  evening: 2
  overnight: 5
";

        [Fact]
        public void LoadFrom_GlobalDcBias_LoadsCorrectly()
        {
            var yaml = BaseYaml + "global_dc_bias: 5\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(5, gd.GlobalDcBias);
        }

        [Fact]
        public void LoadFrom_GlobalDcBias_Zero_IsStandardDifficulty()
        {
            var yaml = BaseYaml + "global_dc_bias: 0\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(0, gd.GlobalDcBias);
        }

        [Fact]
        public void LoadFrom_GlobalDcBias_PositiveValues_LowerDc()
        {
            var yaml = BaseYaml + "global_dc_bias: 3\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(3, gd.GlobalDcBias);
            Assert.True(gd.GlobalDcBias > 0, "Positive global_dc_bias should make the game easier (lowers DC)");
        }

        [Fact]
        public void LoadFrom_ShadowAndHorninessDcBias_LoadsCorrectly()
        {
            var yaml = BaseYaml + "global_dc_bias: 0\nshadow_dc_bias: 4\nhorniness_dc_bias: -2\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(4, gd.ShadowDcBias);
            Assert.Equal(-2, gd.HorninessDcBias);
        }

        [Fact]
        public void LoadFrom_ShadowAndHorninessDcBias_Absent_DefaultsToZero()
        {
            var yaml = BaseYaml + "global_dc_bias: 0\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(0, gd.ShadowDcBias);
            Assert.Equal(0, gd.HorninessDcBias);
        }

        [Fact]
        public void LoadFrom_ShadowDcBias_NonInteger_ThrowsInvalidOperationException()
        {
            var yaml = BaseYaml + "global_dc_bias: 0\nshadow_dc_bias: abc\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var ex = Assert.Throws<InvalidOperationException>(() => GameDefinition.LoadFrom(yaml));
            Assert.Contains("shadow_dc_bias must be an integer", ex.Message);
        }

        [Fact]
        public void LoadFrom_HorninessDcBias_NonInteger_ThrowsInvalidOperationException()
        {
            var yaml = BaseYaml + "global_dc_bias: 0\nhorniness_dc_bias: xyz\n" + GameDefinitionYamlTestFixtures.RequiredParserBlocks;
            var ex = Assert.Throws<InvalidOperationException>(() => GameDefinition.LoadFrom(yaml));
            Assert.Contains("horniness_dc_bias must be an integer", ex.Message);
        }

        [Fact]
        public void LoadFrom_MissingGlobalDcBias_ThrowsInvalidOperationException()
        {
            // BaseYaml does not include global_dc_bias
            var ex = Assert.Throws<InvalidOperationException>(() =>
                GameDefinition.LoadFrom(BaseYaml + GameDefinitionYamlTestFixtures.RequiredParserBlocks));
            Assert.Contains("global_dc_bias", ex.Message);
        }

        [Fact]
        public void Constructor_DefaultGlobalDcBias_IsZero()
        {
            var gd = new GameDefinition("N", "V", "W", "P", "O", "ND");
            Assert.Equal(0, gd.GlobalDcBias);
        }

        [Fact]
        public void Constructor_ExplicitGlobalDcBias_IsSet()
        {
            var gd = new GameDefinition("N", "V", "W", "P", "O", "ND", globalDcBias: 7);
            Assert.Equal(7, gd.GlobalDcBias);
        }
    }
}

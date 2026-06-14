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
            var yaml = BaseYaml + "global_dc_bias: 5\n";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(5, gd.GlobalDcBias);
        }

        [Fact]
        public void LoadFrom_GlobalDcBias_Zero_IsStandardDifficulty()
        {
            var yaml = BaseYaml + "global_dc_bias: 0\n";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(0, gd.GlobalDcBias);
        }

        [Fact]
        public void LoadFrom_GlobalDcBias_PositiveValues_MakeGameHarder()
        {
            var yaml = BaseYaml + "global_dc_bias: 3\n";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(3, gd.GlobalDcBias);
            Assert.True(gd.GlobalDcBias > 0, "Positive global_dc_bias should make the game harder");
        }

        [Fact]
        public void LoadFrom_MissingGlobalDcBias_ThrowsInvalidOperationException()
        {
            // BaseYaml does not include global_dc_bias
            var ex = Assert.Throws<InvalidOperationException>(() =>
                GameDefinition.LoadFrom(BaseYaml));
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

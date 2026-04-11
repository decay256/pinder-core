using System;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #711: GameDefinition loading of horniness_time_modifiers from YAML.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue711_GameDefinitionHorninessTests
    {
        private const string MinimalValidYaml = @"
name: TestGame
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
";

        // ===== LoadFrom parses horniness_time_modifiers =====

        [Fact]
        public void LoadFrom_WithHorninessTimeModifiers_ParsesMorning()
        {
            var gd = GameDefinition.LoadFrom(MinimalValidYaml);
            Assert.NotNull(gd.HorninessTimeModifiers);
            Assert.Equal(3, gd.HorninessTimeModifiers.Morning);
        }

        [Fact]
        public void LoadFrom_WithHorninessTimeModifiers_ParsesAfternoon()
        {
            var gd = GameDefinition.LoadFrom(MinimalValidYaml);
            Assert.Equal(0, gd.HorninessTimeModifiers.Afternoon);
        }

        [Fact]
        public void LoadFrom_WithHorninessTimeModifiers_ParsesEvening()
        {
            var gd = GameDefinition.LoadFrom(MinimalValidYaml);
            Assert.Equal(2, gd.HorninessTimeModifiers.Evening);
        }

        [Fact]
        public void LoadFrom_WithHorninessTimeModifiers_ParsesOvernight()
        {
            var gd = GameDefinition.LoadFrom(MinimalValidYaml);
            Assert.Equal(5, gd.HorninessTimeModifiers.Overnight);
        }

        [Fact]
        public void LoadFrom_WithHorninessTimeModifiers_AllFourValues()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
horniness_time_modifiers:
  morning: 10
  afternoon: 20
  evening: 30
  overnight: 40
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(10, gd.HorninessTimeModifiers.Morning);
            Assert.Equal(20, gd.HorninessTimeModifiers.Afternoon);
            Assert.Equal(30, gd.HorninessTimeModifiers.Evening);
            Assert.Equal(40, gd.HorninessTimeModifiers.Overnight);
        }

        // ===== Missing key throws InvalidOperationException (no silent fallback) =====

        [Fact]
        public void LoadFrom_MissingHorninessTimeModifiers_ThrowsInvalidOperationException()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
";
            var ex = Assert.Throws<InvalidOperationException>(
                () => GameDefinition.LoadFrom(yaml));
            Assert.Equal("game-definition.yaml is missing required key: horniness_time_modifiers", ex.Message);
        }

        [Fact]
        public void LoadFrom_MissingHorninessTimeModifiers_ExceptionMessageNamesKey()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
";
            var ex = Assert.Throws<InvalidOperationException>(
                () => GameDefinition.LoadFrom(yaml));
            Assert.Contains("horniness_time_modifiers", ex.Message);
        }

        [Fact]
        public void LoadFrom_NullHorninessTimeModifiers_ThrowsInvalidOperationException()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
horniness_time_modifiers: ~
";
            Assert.Throws<InvalidOperationException>(
                () => GameDefinition.LoadFrom(yaml));
        }

        // ===== Negative values are allowed =====

        [Fact]
        public void LoadFrom_NegativeModifierValues_AreAllowed()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
horniness_time_modifiers:
  morning: -3
  afternoon: -1
  evening: -2
  overnight: -5
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(-3, gd.HorninessTimeModifiers.Morning);
            Assert.Equal(-5, gd.HorninessTimeModifiers.Overnight);
        }

        // ===== Zero values are allowed =====

        [Fact]
        public void LoadFrom_AllZeroModifiers_AreAllowed()
        {
            var yaml = @"
name: TestGame
vision: v
world_description: w
player_role_description: p
opponent_role_description: o
meta_contract: m
writing_rules: wr
global_dc_bias: 0
horniness_time_modifiers:
  morning: 0
  afternoon: 0
  evening: 0
  overnight: 0
";
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(0, gd.HorninessTimeModifiers.Morning);
            Assert.Equal(0, gd.HorninessTimeModifiers.Afternoon);
            Assert.Equal(0, gd.HorninessTimeModifiers.Evening);
            Assert.Equal(0, gd.HorninessTimeModifiers.Overnight);
        }

        // ===== HorninessTimeModifiers class (GameDefinition namespace) =====

        [Fact]
        public void HorninessTimeModifiers_Constructor_SetsAllProperties()
        {
            var m = new HorninessTimeModifiers(morning: 3, afternoon: 0, evening: 2, overnight: 5);
            Assert.Equal(3, m.Morning);
            Assert.Equal(0, m.Afternoon);
            Assert.Equal(2, m.Evening);
            Assert.Equal(5, m.Overnight);
        }
    }
}

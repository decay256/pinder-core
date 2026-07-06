using System;
using Xunit;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    public class StatDeliveryInstructionsValidationTests
    {
        [Fact]
        public void LoadFrom_EmptyContent_ThrowsConfigurationException()
        {
            Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom(""));
            Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom("   "));
            Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom(null!));
        }

        [Fact]
        public void LoadFrom_MissingDeliveryInstructions_ThrowsConfigurationException()
        {
            var yaml = @"
shadow_corruption:
  madness:
    fumble: ""test""
horniness_overlay:
  fumble: ""test""
";
            var ex = Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom(yaml));
            Assert.Contains("delivery_instructions", ex.Message);
        }

        [Fact]
        public void LoadFrom_MissingShadowCorruption_ThrowsConfigurationException()
        {
            var yaml = @"
delivery_instructions:
  charm:
    strong: ""test""
horniness_overlay:
  fumble: ""test""
";
            var ex = Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom(yaml));
            Assert.Contains("shadow_corruption", ex.Message);
        }

        [Fact]
        public void LoadFrom_MissingHorninessOverlay_ThrowsConfigurationException()
        {
            var yaml = @"
delivery_instructions:
  charm:
    strong: ""test""
shadow_corruption:
  madness:
    fumble: ""test""
";
            var ex = Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom(yaml));
            Assert.Contains("horniness_overlay", ex.Message);
        }

        [Fact]
        public void LoadFrom_ValidNestedHorniness_Succeeds()
        {
            var yaml = @"
delivery_instructions:
  charm:
    strong: ""test""
  horniness_overlay:
    fumble: ""test""
shadow_corruption:
  madness:
    fumble: ""test""
";
            var instructions = StatDeliveryInstructions.LoadFrom(yaml);
            Assert.NotNull(instructions);
        }

        [Fact]
        public void LoadFrom_ValidTopLevelHorniness_Succeeds()
        {
            var yaml = @"
delivery_instructions:
  charm:
    strong: ""test""
shadow_corruption:
  madness:
    fumble: ""test""
horniness_overlay:
  fumble: ""test""
";
            var instructions = StatDeliveryInstructions.LoadFrom(yaml);
            Assert.NotNull(instructions);
        }

        [Fact]
        public void LoadFrom_MalformedYaml_ThrowsConfigurationException()
        {
            var yaml = @"
delivery_instructions: [invalid yaml
";
            Assert.Throws<ConfigurationException>(() => StatDeliveryInstructions.LoadFrom(yaml));
        }
    }
}

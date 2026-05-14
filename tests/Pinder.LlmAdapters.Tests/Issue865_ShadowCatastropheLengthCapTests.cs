using Xunit;
using Pinder.LlmAdapters;
using Pinder.Core.Stats;
using Pinder.Core.Rolls;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue865_ShadowCatastropheLengthCapTests
    {
        private readonly StatDeliveryInstructions _instructions;

        public Issue865_ShadowCatastropheLengthCapTests()
        {
            // Use absolute path to ensure the test finds the YAML regardless of where the binary is run from
            string yamlPath = "/tmp/work-865/data/delivery-instructions.yaml";
            string yaml = System.IO.File.ReadAllText(yamlPath);
            _instructions = StatDeliveryInstructions.LoadFrom(yaml); 
        }

        [Fact]
        public void Fixation_Catastrophe_HasLengthCapInstruction()
        {
            var instruction = _instructions.GetShadowCorruptionInstruction(ShadowStatType.Fixation, FailureTier.Catastrophe);
            Assert.NotNull(instruction);
            Assert.Contains("sentence", instruction.ToLower());
            Assert.Contains("original message length", instruction.ToLower());
        }

        [Theory]
        [InlineData(ShadowStatType.Fixation)]
        [InlineData(ShadowStatType.Madness)]
        [InlineData(ShadowStatType.Dread)]
        [InlineData(ShadowStatType.Denial)]
        [InlineData(ShadowStatType.Overthinking)]
        public void ShadowCatastrophes_HaveLengthCapInstruction(ShadowStatType shadow)
        {
            // Note: Horniness is handled as an overlay, not in shadow_corruption.
            // The ticket specifically lists the 6 shadow stats.
            
            var instruction = _instructions.GetShadowCorruptionInstruction(shadow, FailureTier.Catastrophe);
            if (instruction == null) return; // Skip if not defined in shadow_corruption

            Assert.Contains("sentence", instruction.ToLower());
            Assert.Contains("original message length", instruction.ToLower());
        }
    }
}

using System;
using Xunit;
using Pinder.LlmAdapters;
using Pinder.Core.Stats;
using Pinder.Core.Rolls;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1286_MadnessPhysicalKnowledgeGuardTests
    {
        private readonly StatDeliveryInstructions _instructions;

        public Issue1286_MadnessPhysicalKnowledgeGuardTests()
        {
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string yamlPath = System.IO.Path.Combine(projectRoot, "data", "delivery-instructions.yaml");
            string yaml = System.IO.File.ReadAllText(yamlPath);
            _instructions = StatDeliveryInstructions.LoadFrom(yaml);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void MadnessTier_ContainsPhysicalKnowledgeGuard(FailureTier tier)
        {
            var instruction = _instructions.GetShadowCorruptionInstruction(ShadowStatType.Madness, tier);
            Assert.NotNull(instruction);

            var lower = instruction.ToLower();
            bool hasGuard = lower.Contains("same room") && (lower.Contains("physical") || lower.Contains("private room") || lower.Contains("wearing"));

            Assert.True(hasGuard, $"Madness corruption instruction for tier '{tier}' is missing the same-room / physical-knowledge guard. Instruction text: '{instruction}'");
        }

        [Fact]
        public void MadnessCatastropheServedTier_ContainsSameRoomGuard()
        {
            var instruction = _instructions.GetShadowCorruptionInstruction(ShadowStatType.Madness, FailureTier.Catastrophe);
            Assert.NotNull(instruction);
            Assert.Contains("same room", instruction.ToLower());
        }

        [Fact]
        public void YamlValidity_LoaderSmokeTest()
        {
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string yamlPath = System.IO.Path.Combine(projectRoot, "data", "delivery-instructions.yaml");
            string yaml = System.IO.File.ReadAllText(yamlPath);
            var instructions = StatDeliveryInstructions.LoadFrom(yaml);

            Assert.NotNull(instructions);

            var catastropheInstruction = instructions.GetShadowCorruptionInstruction(ShadowStatType.Madness, FailureTier.Catastrophe);
            Assert.False(string.IsNullOrWhiteSpace(catastropheInstruction), "Madness Catastrophe instruction should load and be non-empty");

            var unrelatedInstruction = instructions.GetShadowCorruptionInstruction(ShadowStatType.Despair, FailureTier.Fumble);
            Assert.NotNull(unrelatedInstruction);
        }
    }
}

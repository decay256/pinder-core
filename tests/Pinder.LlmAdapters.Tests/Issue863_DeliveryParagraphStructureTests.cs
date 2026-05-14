using System;
using System.Linq;
using Xunit;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue863_DeliveryParagraphStructureTests
    {
        [Fact]
        public void SuccessDeliveryInstruction_HardRule_ProhibitsParagraphSplit()
        {
            // Arrange
            var rules = new DeliveryRules(
                clean: "", strong: "", critical: "", exceptional: "", 
                test: "", registerInstruction: "", mediumRule: "");
            
            // Act
            var instruction = PromptTemplates.BuildSuccessDeliveryInstruction(rules);
            
            // Assert
            Assert.Contains("paragraph", instruction, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not split a single-paragraph message into multiple paragraphs", instruction);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(12)]
        [InlineData(16)]
        [InlineData(20)]
        public void SuccessDeliveryInstruction_HardRule_IsPresent_ForAllTiers(int beatDcBy)
        {
            // Arrange
            var rules = new DeliveryRules(
                clean: "", strong: "", critical: "", exceptional: "", 
                test: "", registerInstruction: "", mediumRule: "");

            // Act
            var instruction = PromptTemplates.BuildSuccessDeliveryInstruction(rules);
            
            // Assert
            Assert.Contains("HARD RULE:", instruction);
        }

    }
}

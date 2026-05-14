using Xunit;
using Pinder.LlmAdapters;
using System.Linq;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue870_OpponentVoiceIsolationTests
    {
        [Fact]
        public void OpponentResponseInstruction_ContainsContextBoundary()
        {
            // Arrange
            var catalog = PromptCatalog.LoadFromDirectory("/tmp/work-870/data/prompts");
            var prompt = catalog.Get("opponent-response-instruction");

            // Act & Assert
            Assert.Contains("CONTEXT BOUNDARY", prompt.SystemPrompt);
        }

        [Fact]
        public void OpponentResponseInstruction_ContextBoundary_NamesPsychologicalStake()
        {
            // Arrange
            var catalog = PromptCatalog.LoadFromDirectory("/tmp/work-870/data/prompts");
            var prompt = catalog.Get("opponent-response-instruction");

            // Act & Assert
            Assert.Contains("psychological stake", prompt.SystemPrompt);
            Assert.Contains("shadow state", prompt.SystemPrompt);
        }
    }
}

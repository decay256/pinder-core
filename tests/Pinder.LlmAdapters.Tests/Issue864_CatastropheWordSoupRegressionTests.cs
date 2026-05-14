using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using System.Net.Http;
using Moq;
using Moq.Protected;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue864_CatastropheWordSoupRegressionTests
    {
        [Fact]
        public async Task ApplyHorninessOverlayAsync_ShortMessage_ReturnsOriginalWithoutLlmCall()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new System.Exception("LLM should not have been called"));

            var httpClient = new HttpClient(handlerMock.Object);
            var client = new AnthropicClient("fake-key", httpClient);
            var options = new AnthropicOptions { Model = "claude-3-haiku" };
            var message = "This is a short message."; // 5 words
            var instruction = "Apply horniness catastrophe tier";

            // Act
            var result = await AnthropicOverlayApplier.ApplyHorninessOverlayAsync(
                client,
                options,
                message,
                instruction,
                ct: CancellationToken.None);

            // Assert
            Assert.Equal(message, result);
            // If SendAsync was called, the mock throws. Since it didn't, we passed.
        }

        [Fact]
        public void CatastropheTierPrompt_ContainsAbstractNounEscape()
        {
            // Arrange
            // Use absolute path to the data file relative to project root
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
            var yamlPath = Path.Combine(projectRoot, "data", "delivery-instructions.yaml");
            
            // Fallback for different build environments
            if (!File.Exists(yamlPath))
            {
                yamlPath = Path.Combine(projectRoot, "src", "Pinder.LlmAdapters", "data", "delivery-instructions.yaml");
            }
            if (!File.Exists(yamlPath))
            {
                // Absolute path for the worktree we know it's in
                yamlPath = "/tmp/work-864/data/delivery-instructions.yaml";
            }

            var content = File.ReadAllText(yamlPath);

            // Assert
            Assert.True(content.Contains("abstract concepts", System.StringComparison.OrdinalIgnoreCase), 
                "The delivery instructions should contain the abstract concepts escape hatch.");
            Assert.True(content.Contains("synonym remains physically or semantically plausible", System.StringComparison.OrdinalIgnoreCase),
                "The delivery instructions should contain the plausibility constraint.");
        }
    }
}

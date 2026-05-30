using System;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public sealed class AnthropicRequestBuildersTests
    {
        [Theory]
        [InlineData("claude-opus-4.8")]
        [InlineData("claude-opus-4.8-thinking-low")]
        [InlineData("claude-opus-4.8-thinking-mid")]
        [InlineData("claude-opus-4.8-thinking-high")]
        public void BuildMessagesRequest_MapsOpus48ToDashedApiModelId(string modelSpec)
        {
            // Arrange
            var systemBlocks = Array.Empty<ContentBlock>();
            string userContent = "hello";

            // Act
            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                modelSpec,
                maxTokens: 1024,
                systemBlocks,
                userContent,
                temperature: 0.7);

            // Assert
            Assert.Equal("claude-opus-4-8", request.Model);
            Assert.NotEqual("claude-opus-4.8", request.Model);
            Assert.NotEqual("claude-3-opus-20240229", request.Model);
        }

        [Theory]
        [InlineData("claude-opus-4.8-thinking-low", 2048)]
        [InlineData("claude-opus-4.8-thinking-mid", 4096)]
        [InlineData("claude-opus-4.8-thinking-high", 8192)]
        public void BuildMessagesRequest_ConfiguresThinkingBudgetCorrectly(string modelSpec, int expectedBudget)
        {
            // Arrange
            var systemBlocks = Array.Empty<ContentBlock>();
            string userContent = "hello";

            // Act
            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                modelSpec,
                maxTokens: 1024,
                systemBlocks,
                userContent,
                temperature: 0.7);

            // Assert
            Assert.NotNull(request.Thinking);
            Assert.Equal(expectedBudget, request.Thinking.BudgetTokens);
            Assert.Equal(1.0, request.Temperature);
            Assert.True(request.MaxTokens >= expectedBudget + 1024);
        }
    }
}

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
            // opus-4-8 rejects `temperature`; even the thinking path must omit it.
            Assert.Null(request.Temperature);
            Assert.True(request.MaxTokens >= expectedBudget + 1024);
        }

        [Theory]
        [InlineData("claude-opus-4.8")]
        [InlineData("anthropic/claude-opus-4.8")]
        [InlineData("claude-opus-4.8-thinking-mid")]
        public void BuildMessagesRequest_OmitsTemperature_ForOpus48(string modelSpec)
        {
            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                modelSpec, maxTokens: 1024, Array.Empty<ContentBlock>(), "hello", temperature: 0.7);

            // Temperature must be null so it serializes out of the request body entirely
            // (claude-opus-4-8 returns HTTP 400 `temperature is deprecated for this model`).
            Assert.Null(request.Temperature);
        }

        [Theory]
        [InlineData("claude-sonnet-4.6")]
        [InlineData("anthropic/claude-opus-4.7")]
        public void BuildMessagesRequest_KeepsTemperature_ForModelsThatAcceptIt(string modelSpec)
        {
            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                modelSpec, maxTokens: 1024, Array.Empty<ContentBlock>(), "hello", temperature: 0.7);

            Assert.Equal(0.7, request.Temperature);
        }

        [Theory]
        [InlineData("claude-opus-4.8", true)]
        [InlineData("anthropic/claude-opus-4.8", true)]
        [InlineData("claude-opus-4-8", true)]
        [InlineData("claude-opus-4.7", false)]
        [InlineData("claude-sonnet-4.6", false)]
        [InlineData("", false)]
        public void RejectsTemperature_ClassifiesModelsCorrectly(string modelSpec, bool expected)
        {
            Assert.Equal(expected, AnthropicModelIds.RejectsTemperature(modelSpec));
        }
    }
}

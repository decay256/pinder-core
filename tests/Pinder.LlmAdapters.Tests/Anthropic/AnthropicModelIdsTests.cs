using System;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public sealed class AnthropicModelIdsTests
    {
        [Theory]
        [InlineData("claude-opus-4.8", "claude-opus-4-8")]
        [InlineData("claude-opus-4.7", "claude-opus-4-7")]
        [InlineData("claude-sonnet-4.6", "claude-sonnet-4-6")]
        [InlineData("claude-sonnet-4-20250514", "claude-sonnet-4-20250514")]
        public void ToApiId_MapsPlainAliasesCorrectly(string internalSpec, string expected)
        {
            // Act
            string result = AnthropicModelIds.ToApiId(internalSpec);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("anthropic/claude-opus-4.8", "claude-opus-4-8")]
        [InlineData("anthropic/claude-sonnet-4.6", "claude-sonnet-4-6")]
        [InlineData("ANTHROPIC/claude-opus-4.7", "claude-opus-4-7")]
        public void ToApiId_StripsProviderPrefixCorrectly(string internalSpec, string expected)
        {
            // Act
            string result = AnthropicModelIds.ToApiId(internalSpec);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("claude-opus-4.8-thinking-low", "claude-opus-4-8")]
        [InlineData("claude-opus-4.8-thinking-mid", "claude-opus-4-8")]
        [InlineData("claude-opus-4.8-thinking-high", "claude-opus-4-8")]
        [InlineData("anthropic/claude-opus-4.8-thinking-mid", "claude-opus-4-8")]
        [InlineData("ANTHROPIC/claude-opus-4.8-THINKING-HIGH", "claude-opus-4-8")]
        public void ToApiId_StripsThinkingSuffixCorrectly(string internalSpec, string expected)
        {
            // Act
            string result = AnthropicModelIds.ToApiId(internalSpec);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("claude-opus-4-8", "claude-opus-4-8")]
        [InlineData("claude-sonnet-20241022", "claude-sonnet-20241022")]
        [InlineData("custom-model-without-dots", "custom-model-without-dots")]
        public void ToApiId_PassesThroughAlreadyDashedOrGenericUnchanged(string internalSpec, string expected)
        {
            // Act
            string result = AnthropicModelIds.ToApiId(internalSpec);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("claude-generic-1.2", "claude-generic-1-2")]
        [InlineData("some.dotted.model.name", "some-dotted-model-name")]
        public void ToApiId_ReplacesDotsWithDashesForUnknownModels(string internalSpec, string expected)
        {
            // Act
            string result = AnthropicModelIds.ToApiId(internalSpec);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToApiId_HandlesNullAndEmptyString()
        {
            Assert.Null(AnthropicModelIds.ToApiId(null!));
            Assert.Equal(string.Empty, AnthropicModelIds.ToApiId(string.Empty));
        }
    }
}

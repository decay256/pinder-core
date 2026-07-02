using System;
using Xunit;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1293_DeadOverlaySymbolsRemovedTests
    {
        [Fact]
        public void GroqOverlayApplier_DoesNotExist()
        {
            var type = typeof(PinderLlmAdapterOptions).Assembly.GetType("Pinder.LlmAdapters.Groq.GroqOverlayApplier");
            Assert.Null(type);
        }

        [Fact]
        public void AnthropicOverlayApplier_DoesNotExist()
        {
            var type = typeof(PinderLlmAdapterOptions).Assembly.GetType("Pinder.LlmAdapters.Anthropic.AnthropicOverlayApplier");
            Assert.Null(type);
        }

        [Fact]
        public void PinderLlmAdapterOptions_OverlayProperties_DoNotExist()
        {
            var modelProp = typeof(PinderLlmAdapterOptions).GetProperty("OverlayGroqModel");
            var apiKeyProp = typeof(PinderLlmAdapterOptions).GetProperty("OverlayGroqApiKey");

            Assert.Null(modelProp);
            Assert.Null(apiKeyProp);
        }

        [Fact]
        public void AnthropicOptions_OverlayProperties_DoNotExist()
        {
            var modelProp = typeof(AnthropicOptions).GetProperty("OverlayGroqModel");
            var apiKeyProp = typeof(AnthropicOptions).GetProperty("OverlayGroqApiKey");

            Assert.Null(modelProp);
            Assert.Null(apiKeyProp);
        }
    }
}

using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class AnthropicOptionsTests
    {
        [Fact]
        public void Defaults_AreCorrect()
        {
            var options = new AnthropicOptions();

            Assert.Equal("", options.ApiKey);
            Assert.Equal("claude-sonnet-4-20250514", options.Model);
            Assert.Equal(1024, options.MaxTokens);
            Assert.Equal(0.9, options.Temperature);
            Assert.Null(options.DialogueOptionsTemperature);
            Assert.Null(options.DeliveryTemperature);
            Assert.Null(options.OpponentResponseTemperature);
            Assert.Null(options.InterestChangeBeatTemperature);
        }

        [Fact]
        public void Properties_AreSettable()
        {
            var options = new AnthropicOptions
            {
                ApiKey = "sk-test",
                Model = "claude-opus-4-20250514",
                MaxTokens = 2048,
                Temperature = 0.7,
                DialogueOptionsTemperature = 1.0,
                DeliveryTemperature = 0.5,
                OpponentResponseTemperature = 0.8,
                InterestChangeBeatTemperature = 0.6
            };

            Assert.Equal("sk-test", options.ApiKey);
            Assert.Equal("claude-opus-4-20250514", options.Model);
            Assert.Equal(2048, options.MaxTokens);
            Assert.Equal(0.7, options.Temperature);
            Assert.Equal(1.0, options.DialogueOptionsTemperature);
            Assert.Equal(0.5, options.DeliveryTemperature);
            Assert.Equal(0.8, options.OpponentResponseTemperature);
            Assert.Equal(0.6, options.InterestChangeBeatTemperature);
        }
    }
}

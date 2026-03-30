namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Configuration carrier for the Anthropic LLM adapter.
    /// Per-method temperature overrides fall back to <see cref="Temperature"/> when null.
    /// </summary>
    public sealed class AnthropicOptions
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "claude-sonnet-4-20250514";
        public int MaxTokens { get; set; } = 1024;
        public double Temperature { get; set; } = 0.9;
        public double? DialogueOptionsTemperature { get; set; }
        public double? DeliveryTemperature { get; set; }
        public double? OpponentResponseTemperature { get; set; }
        public double? InterestChangeBeatTemperature { get; set; }
    }
}

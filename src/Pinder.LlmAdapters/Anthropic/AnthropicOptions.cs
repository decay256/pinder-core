namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Configuration carrier for the Anthropic LLM adapter.
    /// Per-method temperature overrides fall back to <see cref="Temperature"/> when null.
    /// </summary>
    public sealed class AnthropicOptions
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = AnthropicModelIds.DefaultModel;
        public int MaxTokens { get; set; } = 1024;
        public double Temperature { get; set; } = 0.9;
        public double? DialogueOptionsTemperature { get; set; }
        public double? DeliveryTemperature { get; set; }
        public double? DateeResponseTemperature { get; set; }
        public double? InterestChangeBeatTemperature { get; set; }
        
        /// <summary>
        /// Game definition used for building system prompts.
        /// Production composition should provide the YAML-loaded definition explicitly.
        /// </summary>
        public GameDefinition? GameDefinition { get; set; }

        public string? DebugDirectory { get; set; }

        /// <summary>
        /// Per-stat, per-tier delivery instructions loaded from delivery-instructions.yaml.
        /// When set, overrides the hardcoded tier instructions in SessionDocumentBuilder.
        /// </summary>
        public StatDeliveryInstructions? StatDeliveryInstructions { get; set; }
    }
}

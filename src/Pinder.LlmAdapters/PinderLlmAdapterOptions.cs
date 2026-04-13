namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Configuration for PinderLlmAdapter. Provider-agnostic — the transport
    /// (AnthropicTransport, OpenAiTransport, etc.) handles API-specific settings.
    /// </summary>
    public sealed class PinderLlmAdapterOptions
    {
        /// <summary>
        /// Game definition containing vision, world rules, prompts, steering prompt, etc.
        /// When null, GameDefinition.PinderDefaults is used.
        /// </summary>
        public GameDefinition? GameDefinition { get; set; }

        /// <summary>
        /// Per-stat, per-tier delivery instructions loaded from delivery-instructions.yaml.
        /// When set, overrides hardcoded tier instructions in SessionDocumentBuilder.
        /// </summary>
        public StatDeliveryInstructions? StatDeliveryInstructions { get; set; }

        /// <summary>
        /// Directory for debug transcript output. When null, debug logging is disabled.
        /// </summary>
        public string? DebugDirectory { get; set; }

        /// <summary>Default sampling temperature for all calls (overridable per method).</summary>
        public double Temperature { get; set; } = 0.9;

        /// <summary>Maximum tokens for all calls.</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>Per-method temperature override for GetDialogueOptionsAsync.</summary>
        public double? DialogueOptionsTemperature { get; set; }

        /// <summary>Per-method temperature override for DeliverMessageAsync.</summary>
        public double? DeliveryTemperature { get; set; }

        /// <summary>Per-method temperature override for GetOpponentResponseAsync.</summary>
        public double? OpponentResponseTemperature { get; set; }

        /// <summary>
        /// Optional Groq API key for routing horniness overlay calls to Groq.
        /// When null, overlay calls use the primary transport.
        /// </summary>
        public string? OverlayGroqApiKey { get; set; }

        /// <summary>
        /// Optional Groq model name for horniness overlay routing.
        /// When null, overlay calls use the primary transport.
        /// </summary>
        public string? OverlayGroqModel { get; set; }
    }
}

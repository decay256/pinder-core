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
        /// Production REQUIRES this to be set explicitly. A null value will cause the production adapter
        /// to throw an InvalidOperationException at call time (no silent PinderDefaults fallbacks in production).
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

        /// <summary>
        /// Sampling-temperature override for the deterministic overlay rewrite
        /// calls (horniness/shadow/trap overlays).
        ///
        /// <para>
        /// #1125 — the standalone "delivery" creative LLM call this option once
        /// tuned was collapsed into the non-LLM <c>DeliveryOverlay</c>, so this
        /// no longer governs a delivery prompt. The name is retained because the
        /// adapters (Anthropic/OpenAi/Pinder overlay appliers) still read this as
        /// the temperature for the text-overlay rewrites that DO remain. Renaming
        /// it would be a parallel-field churn out of this child's scope; it is
        /// kept as the overlay-rewrite temperature knob.
        /// </para>
        /// </summary>
        public double? DeliveryTemperature { get; set; }

        /// <summary>Per-method temperature override for GetDateeResponseAsync.</summary>
        public double? DateeResponseTemperature { get; set; }

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

        /// <summary>
        /// #950: optional callback invoked when the option generator returns N options with
        /// zero references to the active stake content. Receives a diagnostic string of the
        /// form "option_generator_skipped_stake turn={N} stake_lines={M} stake_hits=0".
        /// Use for alerting, telemetry, or test assertions. When null, no callback fires
        /// (a Trace warning is still emitted regardless).
        /// </summary>
        public System.Action<string>? OnStakeSkipWarning { get; set; }
    }
}

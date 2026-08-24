namespace Pinder.LlmAdapters
{
    using Pinder.Core.Conversation;
    using Pinder.Core.Diagnostics.AgentJournals;

    /// <summary>
    /// Configuration for PinderLlmAdapter. Provider-agnostic — the transport
    /// Pi's provider composition handles API-specific settings.
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
        /// Immutable prompt catalog captured by the host for this adapter's
        /// operation or session generation.
        /// </summary>
        public PromptCatalog? PromptCatalog { get; set; }

        /// <summary>
        /// Directory for debug transcript output. When null, debug logging is disabled.
        /// </summary>
        public string? DebugDirectory { get; set; }

        /// <summary>Default sampling temperature for all calls (overridable per method).</summary>
        public double Temperature { get; set; } = LlmPhaseTemperatures.Default;

        /// <summary>Maximum tokens for all calls (null for unconstrained).</summary>
        public int? MaxTokens { get; set; } = null;

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
        /// #950: optional callback invoked when the option generator returns N options with
        /// zero references to the active stake content. Receives a diagnostic string of the
        /// form "option_generator_skipped_stake turn={N} stake_lines={M} stake_hits=0".
        /// Use for alerting, telemetry, or test assertions. When null, no callback fires
        /// (a Trace warning is still emitted regardless).
        /// </summary>
        public System.Action<string>? OnStakeSkipWarning { get; set; }

        /// <summary>
        /// Optional callback invoked when an LLM contract violation is detected.
        /// Receives structured metadata about the violation.
        /// </summary>
        public System.Action<LlmContractViolation>? OnLlmContractViolation { get; set; }

        /// <summary>
        /// Optional durable host sink for Game Run one-shot and conversational journal records.
        /// </summary>
        public IAgentJournalSink? AgentJournalHostSink { get; set; }

        /// <summary>
        /// Controls whether host sink persistence failures fail gameplay calls.
        /// </summary>
        public AgentJournalSinkFailureMode AgentJournalSinkFailureMode { get; set; } =
            AgentJournalSinkFailureMode.FailClosed;

        /// <summary>
        /// Optional clock for deterministic journal tests and host replay evidence.
        /// </summary>
        public System.Func<System.DateTimeOffset>? AgentJournalClock { get; set; }

        private System.TimeSpan _agentJournalWriteTimeout = System.TimeSpan.FromSeconds(2);

        /// <summary>
        /// Bounded write timeout for Pi projection and host sink persistence.
        /// </summary>
        public System.TimeSpan AgentJournalWriteTimeout
        {
            get => _agentJournalWriteTimeout;
            set
            {
                if (value <= System.TimeSpan.Zero)
                    throw new System.ArgumentOutOfRangeException(nameof(value));
                _agentJournalWriteTimeout = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of stateless retries after the initial
        /// LLM call when a contract/parsing violation occurs.
        /// Default is 3. Set to 0 to disable retries.
        /// </summary>
        public int MaxContractViolationRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the base millisecond delay to wait before retrying on a contract violation.
        /// Uses exponential backoff. Default is 100ms.
        /// </summary>
        public int ContractViolationBackoffMs { get; set; } = 100;

        /// <summary>
        /// Optional callback invoked when an overlay or steering rewrite degraded, failed, or was skipped.
        /// </summary>
        public System.Action<OverlayDegradedEvent>? OnOverlayDegraded { get; set; }

        /// <summary>
        /// Optional host-controlled sink for adapter diagnostics. When null,
        /// diagnostics are no-op unless <see cref="DefaultOnDiagnostic"/> is
        /// configured by the host process.
        /// </summary>
        public System.Action<OperationalDiagnosticEvent>? OnDiagnostic { get; set; }

        /// <summary>
        /// A default callback used when OnOverlayDegraded is null.
        /// </summary>
        public static System.Action<OverlayDegradedEvent>? DefaultOnOverlayDegraded { get; set; }

        /// <summary>
        /// A default callback used when OnDiagnostic is null.
        /// </summary>
        public static System.Action<OperationalDiagnosticEvent>? DefaultOnDiagnostic { get; set; }
    }
}

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Canonical default sampling temperatures for game-level LLM phases.
    /// Host applications should reference these values instead of copying
    /// per-phase literals into their adapter wiring.
    /// </summary>
    public static class LlmPhaseTemperatures
    {
        /// <summary>Default temperature for generic calls without a phase-specific override.</summary>
        public const double Default = 0.9;

        /// <summary>Default temperature for dialogue-option generation.</summary>
        public const double DialogueOptions = 0.9;

        /// <summary>Default temperature for overlay rewrite and corruption calls.</summary>
        public const double OverlayRewrite = 0.7;

        /// <summary>Default temperature for datee response generation.</summary>
        public const double DateeResponse = 0.85;
    }
}

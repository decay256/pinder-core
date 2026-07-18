using System;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Represents the privacy-safe structured outcome of a session setup generator.
    /// Never stores raw LLM prompts, raw responses, secrets, or conversation histories.
    /// Only contains structured metadata and the produced value.
    /// </summary>
    public sealed class SetupGenerationResult
    {
        /// <summary>
        /// The generated text value (e.g. the trimmed content or empty string).
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Whether the generation was degraded (e.g. failed and fell back, or empty output).
        /// </summary>
        public bool Degraded { get; }

        /// <summary>
        /// Whether the generation was skipped.
        /// </summary>
        public bool Skipped { get; }

        /// <summary>
        /// Structured error code representing the degradation reason (e.g. 'transport_error', 'empty_output', or null for success).
        /// </summary>
        public string? ErrorCode { get; }

        /// <summary>
        /// The source of the generation (e.g. 'llm', 'fallback').
        /// </summary>
        public string? Source { get; }

        /// <summary>
        /// The name of the generator / phase that produced this result (e.g. 'stake', 'outfit', 'dramatic_arc').
        /// </summary>
        public string GeneratorName { get; }

        public SetupGenerationResult(
            string value,
            bool degraded,
            bool skipped,
            string? errorCode,
            string? source,
            string generatorName)
        {
            Value = value ?? string.Empty;
            Degraded = degraded;
            Skipped = skipped;
            ErrorCode = errorCode;
            Source = source;
            GeneratorName = generatorName ?? throw new ArgumentNullException(nameof(generatorName));
        }

        public static SetupGenerationResult Ok(string value, string generatorName, string source = "llm")
        {
            return new SetupGenerationResult(value, false, false, null, source, generatorName);
        }

        public static SetupGenerationResult DegradedFailure(string generatorName, string errorCode, string source = "fallback")
        {
            return new SetupGenerationResult(string.Empty, true, false, errorCode, source, generatorName);
        }
    }
}

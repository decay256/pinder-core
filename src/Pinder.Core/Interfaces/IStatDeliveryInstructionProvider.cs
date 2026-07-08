using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction for per-stat, per-tier LLM delivery instructions, injected into
    /// GameSession via <see cref="Conversation.GameSessionConfig.StatDeliveryInstructions"/>.
    /// Implemented by the adapter-layer <c>Pinder.LlmAdapters.StatDeliveryInstructions</c>
    /// class (which is loaded from delivery-instructions.yaml). Defined here in
    /// Pinder.Core — which Pinder.LlmAdapters already references — so the engine can
    /// call these members directly instead of doing string-based reflection against
    /// an untyped <c>object?</c> (see #709 / audit finding on HorninessEngine).
    /// All methods return null when no matching instruction is configured; callers
    /// use hardcoded fallback behavior in that case.
    /// </summary>
    public interface IStatDeliveryInstructionProvider
    {
        /// <summary>
        /// Returns the horniness overlay instruction for the given failure tier, or null if not found.
        /// </summary>
        string? GetHorninessOverlayInstruction(FailureTier tier);

        /// <summary>
        /// Returns the stat-specific failure instruction for the given stat and failure tier, or null if not found.
        /// </summary>
        string? GetStatFailureInstruction(StatType stat, FailureTier tier);

        /// <summary>
        /// Returns the shadow corruption instruction for the given shadow stat and failure tier, or null if not found.
        /// </summary>
        string? GetShadowCorruptionInstruction(ShadowStatType shadow, FailureTier tier);
    }
}

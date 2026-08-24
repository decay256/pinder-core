namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Structured response from the datee, replacing a plain string return.
    /// Carries the message text plus optional gameplay-relevant signals
    /// (tells, weakness windows) detected by the LLM.
    /// </summary>
    public sealed class DateeResponse
    {
        /// <summary>The datee's message text.</summary>
        public string MessageText { get; }

        /// <summary>A tell detected in the datee's response, or null if none.</summary>
        public Tell? DetectedTell { get; }

        /// <summary>A weakness window opened by the datee's response, or null if none.</summary>
        public WeaknessWindow? WeaknessWindow { get; }

        /// <summary>
        /// Accepted private emotional direction for trusted diagnostics. Callers must
        /// keep this out of player-visible history and public wire contracts.
        /// </summary>
        public CharacterEmotionalDebugInfo? EmotionalReactionDebug { get; }

        public DateeResponse(
            string messageText,
            Tell? detectedTell = null,
            WeaknessWindow? weaknessWindow = null,
            CharacterEmotionalDebugInfo? emotionalReactionDebug = null)
        {
            MessageText = messageText ?? throw new System.ArgumentNullException(nameof(messageText));
            DetectedTell = detectedTell;
            WeaknessWindow = weaknessWindow;
            EmotionalReactionDebug = emotionalReactionDebug;
        }
    }
}

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

        public DateeResponse(
            string messageText,
            Tell? detectedTell = null,
            WeaknessWindow? weaknessWindow = null)
        {
            MessageText = messageText ?? throw new System.ArgumentNullException(nameof(messageText));
            DetectedTell = detectedTell;
            WeaknessWindow = weaknessWindow;
        }
    }
}

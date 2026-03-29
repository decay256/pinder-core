namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of evaluating a player's response delay.
    /// Contains the interest penalty and an optional conversational test trigger.
    /// </summary>
    public sealed class DelayPenalty
    {
        /// <summary>
        /// The interest change to apply. Always ≤ 0 (penalty) or 0 (no penalty).
        /// </summary>
        public int InterestDelta { get; }

        /// <summary>
        /// True when the delay is long enough to trigger a conversational test
        /// (e.g. opponent sends "thought you ghosted me").
        /// </summary>
        public bool TriggerTest { get; }

        /// <summary>
        /// Optional prompt hint for the LLM when TriggerTest is true.
        /// Null when TriggerTest is false.
        /// </summary>
        public string? TestPrompt { get; }

        public DelayPenalty(int interestDelta, bool triggerTest, string? testPrompt = null)
        {
            InterestDelta = interestDelta;
            TriggerTest = triggerTest;
            TestPrompt = testPrompt;
        }
    }
}

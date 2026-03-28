namespace Pinder.Core.Conversation
{
    /// <summary>
    /// A callback opportunity — a conversation topic that can be referenced later for a bonus.
    /// Stub type — feature PRs will flesh out usage.
    /// </summary>
    public sealed class CallbackOpportunity
    {
        /// <summary>Key identifying the topic (e.g. "pizza_story", "childhood_fear").</summary>
        public string TopicKey { get; }

        /// <summary>The turn number when this topic was introduced.</summary>
        public int TurnIntroduced { get; }

        public CallbackOpportunity(string topicKey, int turnIntroduced)
        {
            TopicKey = topicKey ?? throw new System.ArgumentNullException(nameof(topicKey));
            TurnIntroduced = turnIntroduced;
        }
    }
}

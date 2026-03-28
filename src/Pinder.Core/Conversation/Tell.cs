using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// A behavioural tell detected in an opponent's response.
    /// Indicates which stat the opponent revealed vulnerability in.
    /// Stub type — feature PRs will flesh out usage.
    /// </summary>
    public sealed class Tell
    {
        /// <summary>The stat the tell relates to.</summary>
        public StatType Stat { get; }

        /// <summary>Human-readable description of the tell.</summary>
        public string Description { get; }

        public Tell(StatType stat, string description)
        {
            Stat = stat;
            Description = description ?? throw new System.ArgumentNullException(nameof(description));
        }
    }
}

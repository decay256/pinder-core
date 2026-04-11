using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Decision-making interface for sim agents. Takes a TurnStart and PlayerAgentContext,
    /// returns a PlayerDecision with the chosen option index, reasoning, and score breakdowns.
    /// </summary>
    public interface IPlayerAgent
    {
        /// <summary>
        /// Given a TurnStart (options + game state snapshot) and additional agent context,
        /// returns a decision: which option to pick, why, and score breakdowns for all options.
        /// </summary>
        Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);

        /// <summary>
        /// The last explanation produced by the agent (e.g. LLM reasoning).
        /// Null or empty when the agent does not produce explanations.
        /// </summary>
        string LastExplanation { get; }
    }
}

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Extended LLM adapter interface that supports persistent conversation sessions.
    /// The opponent session accumulates context across turns so the opponent character
    /// maintains real memory continuity. Options and delivery calls remain stateless
    /// to prevent voice bleed between player and opponent roles.
    /// </summary>
    public interface IStatefulLlmAdapter : ILlmAdapter
    {
        /// <summary>
        /// Initializes a persistent opponent session with the given system prompt.
        /// Subsequent GetOpponentResponseAsync calls will accumulate messages
        /// in this session instead of being stateless.
        /// </summary>
        /// <param name="opponentSystemPrompt">The assembled system prompt for the opponent character.</param>
        void StartOpponentSession(string opponentSystemPrompt);

        /// <summary>
        /// Whether a persistent opponent session is currently active.
        /// </summary>
        bool HasOpponentSession { get; }
    }
}

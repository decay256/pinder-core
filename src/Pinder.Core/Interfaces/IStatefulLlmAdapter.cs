namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Extends ILlmAdapter with stateful conversation support.
    /// When implemented, GameSession creates a persistent conversation
    /// at construction and routes all LLM calls through accumulated
    /// message history.
    /// </summary>
    public interface IStatefulLlmAdapter : ILlmAdapter
    {
        /// <summary>
        /// Start a new conversation session with the given system prompt.
        /// The adapter internally tracks the active session.
        /// Subsequent ILlmAdapter method calls use the accumulated
        /// message history from this session.
        /// Call once per GameSession lifetime.
        /// Calling when a session is already active replaces it (no error).
        /// </summary>
        /// <param name="systemPrompt">
        /// Full system prompt string (both character profiles + game context).
        /// Must not be null or empty.
        /// </param>
        void StartConversation(string systemPrompt);

        /// <summary>
        /// Whether a conversation session is currently active.
        /// Returns true after StartConversation has been called.
        /// </summary>
        bool HasActiveConversation { get; }
    }
}

using System.Collections.Generic;
using System.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Accumulates messages for a persistent conversation session with the Anthropic API.
    /// Each user/assistant exchange is appended so the model retains full context
    /// across multiple turns within a single game session.
    /// </summary>
    public sealed class ConversationSession
    {
        private readonly List<Message> _messages = new List<Message>();

        /// <summary>
        /// Appends a user message to the session history.
        /// </summary>
        public void AppendUser(string content)
        {
            _messages.Add(new Message { Role = "user", Content = content });
        }

        /// <summary>
        /// Appends an assistant message to the session history.
        /// </summary>
        public void AppendAssistant(string content)
        {
            _messages.Add(new Message { Role = "assistant", Content = content });
        }

        /// <summary>
        /// Builds a MessagesRequest containing the full accumulated conversation history.
        /// </summary>
        public MessagesRequest BuildRequest(
            string model,
            int maxTokens,
            double temperature,
            ContentBlock[] systemBlocks)
        {
            return new MessagesRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Temperature = temperature,
                System = systemBlocks,
                Messages = _messages.ToArray()
            };
        }

        /// <summary>
        /// The number of messages accumulated in this session.
        /// </summary>
        public int MessageCount => _messages.Count;
    }
}

using System;
using System.Collections.Generic;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Holds accumulated user/assistant messages for a stateful conversation
    /// with the Anthropic Messages API. System blocks are set once at construction;
    /// messages grow unbounded as turns are played.
    /// </summary>
    public sealed class ConversationSession
    {
        private readonly List<Message> _messages = new List<Message>();

        /// <summary>
        /// The system prompt blocks (with cache_control: ephemeral) persisted for the session.
        /// Set once at construction, immutable thereafter.
        /// </summary>
        public ContentBlock[] SystemBlocks { get; }

        /// <summary>
        /// All accumulated messages in conversation order (user/assistant alternating).
        /// Grows unbounded within a session. Read-only view of internal list.
        /// </summary>
        public IReadOnlyList<Message> Messages => _messages;

        /// <summary>
        /// Creates a new conversation session with the given system prompt text.
        /// The system prompt is wrapped in a single ContentBlock with
        /// cache_control: { type: "ephemeral" } for Anthropic prompt caching.
        /// </summary>
        /// <param name="systemPrompt">
        /// Full system prompt text (character bibles, game vision, etc.).
        /// Must not be null or empty.
        /// </param>
        /// <exception cref="ArgumentException">If systemPrompt is null or whitespace.</exception>
        public ConversationSession(string systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt))
                throw new ArgumentException("System prompt must not be null, empty, or whitespace.", nameof(systemPrompt));

            SystemBlocks = new[]
            {
                new ContentBlock
                {
                    Type = "text",
                    Text = systemPrompt,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };
        }

        /// <summary>
        /// Append a user-role message to the conversation history.
        /// </summary>
        /// <param name="content">Message text. Must not be null.</param>
        /// <exception cref="ArgumentNullException">If content is null.</exception>
        public void AppendUser(string content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            _messages.Add(new Message { Role = "user", Content = content });
        }

        /// <summary>
        /// Append an assistant-role message to the conversation history.
        /// </summary>
        /// <param name="content">Message text. Must not be null.</param>
        /// <exception cref="ArgumentNullException">If content is null.</exception>
        public void AppendAssistant(string content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            _messages.Add(new Message { Role = "assistant", Content = content });
        }

        /// <summary>
        /// Build a MessagesRequest using accumulated state:
        /// system blocks + all messages + specified parameters.
        /// Returns a snapshot — subsequent appends do not affect returned requests.
        /// </summary>
        /// <param name="model">Anthropic model identifier (e.g. "claude-sonnet-4-20250514").</param>
        /// <param name="maxTokens">Maximum tokens in the response.</param>
        /// <param name="temperature">Sampling temperature (0.0–1.0).</param>
        /// <returns>
        /// A MessagesRequest with SystemBlocks as system, all Messages as messages array,
        /// and the given model/maxTokens/temperature.
        /// </returns>
        public MessagesRequest BuildRequest(string model, int maxTokens, double temperature)
        {
            // Return a snapshot (array copy) so future appends don't mutate the request
            return new MessagesRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Temperature = temperature,
                System = SystemBlocks,
                Messages = _messages.ToArray()
            };
        }
    }
}

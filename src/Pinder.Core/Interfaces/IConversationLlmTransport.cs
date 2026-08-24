using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Provider-neutral ordered-message transport. Prior semantic messages stay
    /// typed and the current engine instruction remains a transient user turn.
    /// </summary>
    public interface IConversationLlmTransport : ILlmTransport
    {
        /// <summary>
        /// True only when ordered typed messages can traverse the complete
        /// transport/decorator chain without flattening or rejection.
        /// </summary>
        bool SupportsConversationMessages { get; }

        Task<string> SendConversationAsync(
            string systemPrompt,
            IReadOnlyList<ConversationMessage> priorMessages,
            string userMessage,
            double temperature = 0.9,
            int? maxTokens = null,
            string? phase = null,
            CancellationToken cancellationToken = default);
    }
}

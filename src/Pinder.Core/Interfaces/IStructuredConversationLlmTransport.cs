using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>Structured-output counterpart to <see cref="IConversationLlmTransport"/>.</summary>
    public interface IStructuredConversationLlmTransport : IStructuredLlmTransport
    {
        /// <summary>
        /// True only when structured output and ordered typed messages can both
        /// traverse the complete transport/decorator chain.
        /// </summary>
        bool SupportsStructuredConversationMessages { get; }

        Task<StructuredLlmResponse> SendStructuredConversationAsync(
            StructuredLlmRequest request,
            IReadOnlyList<ConversationMessage> priorMessages,
            CancellationToken cancellationToken = default);
    }
}

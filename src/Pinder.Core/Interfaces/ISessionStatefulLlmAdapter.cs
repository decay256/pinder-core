using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Pi-session-aware extension used during the dual-write migration window.
    /// Existing adapters remain valid through <see cref="IStatefulLlmAdapter"/>.
    /// </summary>
    public interface ISessionStatefulLlmAdapter : IStatefulLlmAdapter
    {
        bool SupportsConversationSessions { get; }

        Task<DialogueOption[]> GetDialogueOptionsAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage> avatarHistory,
            LlmConversationSessionSnapshot? avatarSession,
            CancellationToken cancellationToken = default);

        Task<StatefulDateeResult> GetDateeResponseAsync(
            DateeContext context,
            IReadOnlyList<ConversationMessage> dateeHistory,
            IReadOnlyList<ConversationMessage> avatarHistory,
            LlmConversationSessionSnapshot? dateeSession,
            LlmConversationSessionSnapshot? avatarSession,
            CancellationToken cancellationToken = default);
    }
}

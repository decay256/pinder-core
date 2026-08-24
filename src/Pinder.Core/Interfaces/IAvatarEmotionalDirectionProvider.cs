using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Optional LLM capability that determines the avatar's private emotional
    /// writing posture before candidate dialogue options are generated.
    /// </summary>
    public interface IAvatarEmotionalDirectionProvider
    {
        bool SupportsAvatarEmotionalDirection { get; }

        Task<CharacterEmotionalDirection> GetAvatarEmotionalDirectionAsync(
            DialogueContext context,
            IReadOnlyList<ConversationMessage> avatarHistory,
            LlmConversationSessionSnapshot? avatarSession,
            CancellationToken cancellationToken = default);
    }
}

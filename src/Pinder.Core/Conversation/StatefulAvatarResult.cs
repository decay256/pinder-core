using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a stateful avatar (delivery) call (#1123). The symmetric
    /// sibling of <see cref="StatefulDateeResult"/>. Wraps the delivered message
    /// text plus the conversation entries the adapter wants the engine to append
    /// to its <c>AvatarHistory</c>.
    ///
    /// <para>
    /// As with the datee session, the adapter is the source of truth for "what
    /// content went on the wire this turn" (it builds the user prompt from the
    /// <see cref="DeliveryContext"/> and knows what the assistant returned); the
    /// engine is the source of truth for "where that history is stored." This
    /// struct lets the adapter hand the engine exactly the entries it needs to
    /// append, in order, without leaking adapter-internal prompt shape into the
    /// engine.
    /// </para>
    /// </summary>
    public sealed class StatefulAvatarResult
    {
        /// <summary>The delivered message text (post-degradation).</summary>
        public string DeliveredMessage { get; }

        /// <summary>
        /// New entries the engine should append to its avatar history.
        /// Typically one user-role entry (the prompt this turn) followed by one
        /// assistant-role entry (the delivered message). May be empty when the
        /// call short-circuited.
        /// </summary>
        public IReadOnlyList<ConversationMessage> NewHistoryEntries { get; }

        public StatefulAvatarResult(
            string deliveredMessage,
            IReadOnlyList<ConversationMessage> newHistoryEntries)
        {
            DeliveredMessage = deliveredMessage ?? string.Empty;
            NewHistoryEntries = newHistoryEntries ?? throw new System.ArgumentNullException(nameof(newHistoryEntries));
        }
    }
}

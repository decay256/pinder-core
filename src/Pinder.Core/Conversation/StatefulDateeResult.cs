using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a stateful datee call (#788). Wraps the parsed
    /// <see cref="DateeResponse"/> plus the conversation entries the
    /// adapter wants the engine to append to its <c>_dateeHistory</c>.
    ///
    /// <para>
    /// The adapter is the source of truth for "what content went on the wire
    /// this turn" \u2014 it builds the user prompt from the
    /// <see cref="DateeContext"/> and knows what the assistant returned.
    /// The engine is the source of truth for "where that history is stored."
    /// This struct lets the adapter hand the engine exactly the entries it
    /// needs to append, in order, without leaking adapter-internal prompt
    /// shape into the engine.
    /// </para>
    /// </summary>
    public sealed class StatefulDateeResult
    {
        /// <summary>The parsed datee response (text + signals).</summary>
        public DateeResponse Response { get; }

        /// <summary>
        /// New entries the engine should append to its datee history.
        /// Typically one user-role entry (the delivered player dialogue) followed by
        /// one assistant-role entry (the response). May be empty when the
        /// call short-circuited (e.g. the parsed response failed validation
        /// and the adapter chose not to record the turn).
        /// </summary>
        public IReadOnlyList<ConversationMessage> NewHistoryEntries { get; }

        public StatefulDateeResult(
            DateeResponse response,
            IReadOnlyList<ConversationMessage> newHistoryEntries)
        {
            Response = response ?? throw new System.ArgumentNullException(nameof(response));
            NewHistoryEntries = newHistoryEntries ?? throw new System.ArgumentNullException(nameof(newHistoryEntries));
        }
    }
}

using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a stateful opponent call (#788). Wraps the parsed
    /// <see cref="OpponentResponse"/> plus the conversation entries the
    /// adapter wants the engine to append to its <c>_opponentHistory</c>.
    ///
    /// <para>
    /// The adapter is the source of truth for "what content went on the wire
    /// this turn" \u2014 it builds the user prompt from the
    /// <see cref="OpponentContext"/> and knows what the assistant returned.
    /// The engine is the source of truth for "where that history is stored."
    /// This struct lets the adapter hand the engine exactly the entries it
    /// needs to append, in order, without leaking adapter-internal prompt
    /// shape into the engine.
    /// </para>
    /// </summary>
    public sealed class StatefulOpponentResult
    {
        /// <summary>The parsed opponent response (text + signals).</summary>
        public OpponentResponse Response { get; }

        /// <summary>
        /// New entries the engine should append to its opponent history.
        /// Typically one user-role entry (the prompt this turn) followed by
        /// one assistant-role entry (the response). May be empty when the
        /// call short-circuited (e.g. the parsed response failed validation
        /// and the adapter chose not to record the turn).
        /// </summary>
        public IReadOnlyList<ConversationMessage> NewHistoryEntries { get; }

        public StatefulOpponentResult(
            OpponentResponse response,
            IReadOnlyList<ConversationMessage> newHistoryEntries)
        {
            Response = response ?? throw new System.ArgumentNullException(nameof(response));
            NewHistoryEntries = newHistoryEntries ?? throw new System.ArgumentNullException(nameof(newHistoryEntries));
        }
    }
}

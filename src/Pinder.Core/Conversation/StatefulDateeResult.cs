using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a stateful datee call (#788). Wraps the parsed
    /// <see cref="DateeResponse"/> plus the conversation entries the
    /// adapter exposes for direct callers.
    ///
    /// <para>
    /// The adapter is the source of truth for "what content went on the wire
    /// this turn" \u2014 it builds the user prompt from the
    /// <see cref="DateeContext"/> and knows what the assistant returned.
    /// The engine is the source of truth for semantic history commits. During
    /// <see cref="GameSession"/> turn resolution it appends a canonical pair
    /// from the delivered player message and parsed visible DATEE response, so
    /// failed attempts, private direction, and provider-only signal blocks cannot
    /// enter committed session history.
    /// </para>
    /// </summary>
    public sealed class StatefulDateeResult
    {
        /// <summary>The parsed datee response (text + signals).</summary>
        public DateeResponse Response { get; }

        /// <summary>
        /// Canonical visible entries for direct adapter callers. Typically one
        /// user-role entry (the delivered player dialogue) followed by one
        /// assistant-role entry (the parsed visible response). <see cref="GameSession"/>
        /// does not trust these entries for its semantic commit.
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

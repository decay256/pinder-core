using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// LLM adapter interface that supports a multi-turn opponent conversation by
    /// accepting the conversation history as a parameter on each call.
    ///
    /// <para>
    /// As of #788 (Phase 1 of the #393 fast-gameplay refactor) the adapter is
    /// pure-stateless: the <em>engine</em> owns the opponent conversation history
    /// inside <see cref="Pinder.Core.Conversation.GameSession"/>. The adapter no
    /// longer holds session state. This makes the adapter safe to share across
    /// concurrent sessions and removes a class of accidental cross-session
    /// context bleed.
    /// </para>
    ///
    /// <para>
    /// The base <see cref="ILlmAdapter.GetOpponentResponseAsync(OpponentContext)"/>
    /// remains as the single-turn fallback (used by callers that don't carry an
    /// opponent history yet). Stateful callers MUST go through the
    /// history-passing overload below.
    /// </para>
    /// </summary>
    public interface IStatefulLlmAdapter : ILlmAdapter
    {
        /// <summary>
        /// Generate the opponent's next response, given the prior conversation
        /// history accumulated by the engine. Stateless: implementations MUST NOT
        /// retain any opponent-session field across calls.
        /// </summary>
        /// <param name="context">Opponent context for the current turn (system prompt, etc.).</param>
        /// <param name="history">
        /// Prior opponent conversation, in chronological order. Each entry's
        /// <see cref="ConversationMessage.Role"/> is one of <c>"user"</c> or
        /// <c>"assistant"</c>. The current-turn user prompt is NOT included —
        /// the implementation derives it from <paramref name="context"/> and
        /// appends it after the supplied history.
        /// </param>
        /// <param name="cancellationToken">Cooperative cancellation (#794 alignment).</param>
        /// <returns>
        /// A <see cref="StatefulOpponentResult"/> bundling the parsed response with
        /// the new history entries the engine should append to its opponent
        /// history (typically one user-role entry followed by one assistant-role
        /// entry). The engine appends them verbatim and passes the grown list
        /// in on the next call.
        /// </returns>
        Task<StatefulOpponentResult> GetOpponentResponseAsync(
            OpponentContext context,
            IReadOnlyList<ConversationMessage> history,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate a steering question to append to the player's delivered message.
        /// Called after a successful steering roll. The question should reference
        /// specifics from the conversation and nudge toward meeting up.
        /// </summary>
        /// <param name="ct">
        /// Cancellation token forwarded from <see cref="GameSession.ResolveTurnAsync(int, System.IProgress{TurnProgressEvent}?, CancellationToken)"/>
        /// (#794). Implementations MUST pass this through to the underlying
        /// transport so a mid-turn cancel halts the in-flight HTTP call.
        /// </param>
        Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default);
    }
}

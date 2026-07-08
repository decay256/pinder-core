using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Generates a "psychological stake" as a 15-item markdown bullet list
    /// for a single character, derived from their assembled system prompt.
    /// One call per character.
    /// </summary>
    /// <remarks>
    /// #826 (setup-trim phase 3): the previous novella-style 6-point
    /// 2-3-paragraphs-each character bible was replaced with the slim
    /// fragment shape because the stake is injected into every turn's
    /// system prompt and the bigger shape was costing ~1500 input tokens
    /// per turn per character with no demonstrated lift.
    /// Per #949, the current output contract is exactly 15 <c>- </c>-
    /// prefixed markdown bullets, one per stem-completion; the SPA renders
    /// those bullets and the sanitizer preserves their prefixes.
    /// </remarks>
    public interface IStakeGenerator
    {
        /// <summary>
        /// Generate a psychological-stake markdown bullet list for
        /// <paramref name="characterName"/>. The returned text contains
        /// exactly 15 lines, each starting with <c>- </c>, one per
        /// stem-completion, with the stem prefix included in the bullet body
        /// (see <c>Pinder.SessionSetup/README.md</c> and #949). Returns an
        /// empty string on transport failure; never throws.
        /// </summary>
        Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream the psychological-stake markdown bullet list as raw text
        /// fragments (tokens or token groups) as they arrive from the
        /// provider. The concatenation of all yielded fragments is equivalent
        /// to the string returned by <see cref="GenerateAsync"/> (modulo
        /// trailing whitespace trimming).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Output follows the #949 stake contract: a 15-item markdown bullet
        /// list, one <c>- </c>-prefixed bullet per stem-completion. Headings,
        /// nested bullets, numbered lists, emphasis, blockquotes, and code
        /// formatting remain forbidden (see
        /// <c>Pinder.SessionSetup/README.md</c>).
        /// </para>
        /// <para>
        /// <b>Error semantics differ from <see cref="GenerateAsync"/>.</b>
        /// On transport failure, this method throws
        /// <see cref="LlmTransportException"/> rather than swallowing; the
        /// caller (the web tier's <c>ActiveSession</c>) needs to set
        /// <c>setup_error</c> correctly when the stream collapses mid-way.
        /// Cancellation propagates as <see cref="System.OperationCanceledException"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="LlmTransportException">Thrown from the enumerator on transport failure.</exception>
        IAsyncEnumerable<string> StreamStakeAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default);
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Generates a "psychological stake" novella-style character bible for a
    /// single character, derived from their assembled system prompt.
    /// One call per character.
    /// </summary>
    public interface IStakeGenerator
    {
        /// <summary>
        /// Generate a psychological-stake paragraph block (plain prose,
        /// paragraph breaks only — see <c>Pinder.SessionSetup/README.md</c>)
        /// for <paramref name="characterName"/>. Returns an empty string on
        /// transport failure — never throws.
        /// </summary>
        Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream the psychological-stake paragraph block as raw text
        /// fragments (tokens or token groups) as they arrive from the
        /// provider. The concatenation of all yielded fragments is
        /// equivalent to the string returned by
        /// <see cref="GenerateAsync"/> (modulo trailing whitespace trimming).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Output is plain prose — paragraph breaks only, no markdown
        /// markers (see <c>Pinder.SessionSetup/README.md</c>).
        /// </para>
        /// <para>
        /// <b>Error semantics differ from <see cref="GenerateAsync"/>.</b>
        /// On transport failure, this method throws
        /// <see cref="LlmTransportException"/> rather than swallowing — the
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

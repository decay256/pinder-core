using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Produces a human-readable matchup analysis for a pair of characters.
    /// Usually a single LLM call, but implementations may cache.
    /// </summary>
    public interface IMatchupAnalyzer
    {
        /// <summary>
        /// Return a plain-text matchup analysis (paragraph breaks only — no
        /// markdown markers; see <c>Pinder.SessionSetup/README.md</c>), or
        /// <c>null</c> on any transport failure (never throws on LLM errors —
        /// errors are the caller's concern and should not crash a session).
        /// </summary>
        Task<string?> AnalyzeMatchupAsync(
            CharacterProfile player,
            CharacterProfile opponent,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stream the matchup analysis as raw text fragments (tokens or
        /// token groups) as they arrive from the provider. The concatenation
        /// of all yielded fragments is equivalent to the string returned by
        /// <see cref="AnalyzeMatchupAsync"/> (modulo trailing whitespace
        /// trimming, which is a non-streaming concern).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Output is plain prose — paragraph breaks only, no markdown
        /// markers (see <c>Pinder.SessionSetup/README.md</c>).
        /// </para>
        /// <para>
        /// <b>Error semantics differ from <see cref="AnalyzeMatchupAsync"/>.</b>
        /// On transport failure, this method throws
        /// <see cref="LlmTransportException"/> rather than swallowing — the
        /// caller (the web tier's <c>ActiveSession</c>) needs to set
        /// <c>setup_error</c> correctly when the stream collapses mid-way.
        /// Cancellation propagates as <see cref="System.OperationCanceledException"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="LlmTransportException">Thrown from the enumerator on transport failure.</exception>
        IAsyncEnumerable<string> StreamMatchupAsync(
            CharacterProfile player,
            CharacterProfile opponent,
            CancellationToken cancellationToken = default);
    }
}

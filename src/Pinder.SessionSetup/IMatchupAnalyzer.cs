using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Produces a human-readable matchup analysis for a pair of characters.
    /// Usually a single LLM call, but implementations may cache.
    /// </summary>
    public interface IMatchupAnalyzer
    {
        /// <summary>
        /// Return a markdown-formatted matchup analysis, or <c>null</c> on any
        /// transport failure (never throws on LLM errors — errors are the caller's
        /// concern and should not crash a session).
        /// </summary>
        Task<string?> AnalyzeMatchupAsync(
            CharacterProfile player,
            CharacterProfile opponent,
            CancellationToken cancellationToken = default);
    }
}

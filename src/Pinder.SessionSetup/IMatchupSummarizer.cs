using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Issue #332: produces a short (1-2 paragraph), human-readable summary
    /// of a matchup. Display-only — the result is surfaced to the UI in the
    /// Player Sheet "Background" tab alongside the full matchup analysis,
    /// and is NOT used as input to any subsequent LLM prompt in the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Output contract (mirrors <see cref="IMatchupAnalyzer"/> and
    /// <see cref="IOutfitDescriber"/>): plain prose, paragraph breaks
    /// only — no markdown, no bullets, no headings, no code formatting.
    /// The web tier's <c>MarkdownSanitizer</c> runs as defence in depth.
    /// </para>
    /// <para>
    /// One LLM call per session, run after / alongside the matchup-analysis
    /// stage. Short — target ~120 tokens — so the round-trip is cheap.
    /// </para>
    /// <para>
    /// Failure semantics: returns an empty string on any transport failure
    /// (mirrors <see cref="IOutfitDescriber"/>). The summary is non-critical
    /// to setup — when generation fails the UI simply hides the section, so
    /// throwing here would be a needlessly heavy hammer.
    /// </para>
    /// </remarks>
    public interface IMatchupSummarizer
    {
        /// <summary>
        /// Generate a 1-2 paragraph plain-prose summary of the matchup
        /// between <paramref name="player"/> and <paramref name="opponent"/>.
        /// Returns the empty string on transport failure — never throws on
        /// LLM errors.
        /// </summary>
        /// <param name="player">Player profile (display name, level, stats, shadows).</param>
        /// <param name="opponent">Opponent profile.</param>
        /// <param name="cancellationToken">Cancellation token tied to the session lifetime.</param>
        Task<string> SummarizeAsync(
            CharacterProfile player,
            CharacterProfile opponent,
            CancellationToken cancellationToken = default);
    }
}

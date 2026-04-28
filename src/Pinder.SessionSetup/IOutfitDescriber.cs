using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Issue #333: produces a brief, human-readable paragraph describing
    /// what each character is wearing for the turn-0 scene-setting entry.
    /// One LLM call per session, run in parallel with matchup analysis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Output contract (mirroring <see cref="IMatchupAnalyzer"/> and
    /// <see cref="IStakeGenerator"/>): plain prose, paragraph breaks
    /// only \u2014 no markdown, no bullets, no headings. The web tier's
    /// <c>MarkdownSanitizer</c> runs as defence in depth.
    /// </para>
    /// <para>
    /// The shape is deliberately item-list-based rather than
    /// <see cref="Pinder.Core.Characters.CharacterProfile"/>-based:
    /// callers pass display strings for each character's equipped
    /// items so the implementation has no dependency on the wider
    /// character-assembly pipeline. Each item description string is
    /// of the form <c>"&lt;display name&gt;: &lt;description&gt;"</c>.
    /// </para>
    /// </remarks>
    public interface IOutfitDescriber
    {
        /// <summary>
        /// Generate a brief outfit-description paragraph covering both
        /// characters' equipped items. Returns an empty string on any
        /// transport failure (mirrors <see cref="IStakeGenerator"/>) \u2014
        /// never throws on LLM errors. The caller must therefore tolerate
        /// an empty string and decide whether to short-circuit setup or
        /// continue without the scene entry.
        /// </summary>
        /// <param name="playerName">Player display name.</param>
        /// <param name="playerItems">
        /// One entry per equipped item on the player; each entry combines
        /// the item display name and its description, e.g.
        /// <c>"Battered jeans: faded indigo, knee-blown, smells of pine."</c>.
        /// </param>
        /// <param name="opponentName">Opponent display name.</param>
        /// <param name="opponentItems">Same shape as <paramref name="playerItems"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<string> GenerateAsync(
            string playerName,
            IReadOnlyList<string> playerItems,
            string opponentName,
            IReadOnlyList<string> opponentItems,
            CancellationToken cancellationToken = default);
    }
}

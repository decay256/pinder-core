using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Issue #821: produces a brief dramatic arc (3-5 sentences) providing
    /// narrative direction for a conversation session. The arc outlines
    /// setup, escalation, a turning point, and possible resolution as soft
    /// guardrails — NOT a script. One LLM call per session, run in parallel
    /// with other setup tasks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Output contract: plain prose only — no markdown, no bullets, no
    /// headings. The web tier's <c>MarkdownSanitizer</c> runs as defence
    /// in depth.
    /// </para>
    /// <para>
    /// The arc is appended to the datee system prompt after the
    /// psychological stake and provides soft narrative direction. It does
    /// NOT hard-gate turns; the interest/stake simulation remains
    /// authoritative. The arc may describe mood or tension arc but must
    /// never dictate specific lines or outcomes.
    /// </para>
    /// </remarks>
    public interface IDramaticArcGenerator
    {
        /// <summary>
        /// Generate a brief dramatic-arc paragraph (3-5 sentences) for the
        /// session. Returns an empty string on any transport failure — never
        /// throws on LLM errors. The caller must therefore tolerate an empty
        /// string and decide whether to continue without the arc.
        /// </summary>
        /// <param name="playerName">Player display name.</param>
        /// <param name="playerStake">Player's psychological stake (plain text).</param>
        /// <param name="playerBio">Player's bio (plain text).</param>
        /// <param name="dateeName">Datee display name.</param>
        /// <param name="dateeStake">Datee's psychological stake (plain text).</param>
        /// <param name="dateeBio">Datee's bio (plain text).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<string> GenerateAsync(
            string playerName,
            string playerStake,
            string playerBio,
            string dateeName,
            string dateeStake,
            string dateeBio,
            CancellationToken cancellationToken = default);
    }
}

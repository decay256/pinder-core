using System.Threading;
using System.Threading.Tasks;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// Generates a cohesive narrative background story from a character's
    /// assembled background fragments. Called once per character at creation time.
    /// </summary>
    /// <remarks>
    /// Issue #820: mirrors the <see cref="IStakeGenerator"/> pattern.
    /// Output is 3-5 sentences of third-person past-tense prose synthesizing
    /// the character's background fragments into a single narrative paragraph.
    /// </remarks>
    public interface IBackgroundGenerator
    {
        /// <summary>
        /// Generate a narrative background story (3-5 sentence prose, third
        /// person past tense) for <paramref name="characterName"/>. Returns
        /// an empty string on transport failure — never throws.
        /// </summary>
        Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default);
    }
}

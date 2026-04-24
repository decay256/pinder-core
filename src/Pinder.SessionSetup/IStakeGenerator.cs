using System.Threading;
using System.Threading.Tasks;

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
        /// Generate a psychological-stake paragraph block for <paramref name="characterName"/>.
        /// Returns an empty string on transport failure — never throws.
        /// </summary>
        Task<string> GenerateAsync(
            string characterName,
            string assembledSystemPrompt,
            CancellationToken cancellationToken = default);
    }
}

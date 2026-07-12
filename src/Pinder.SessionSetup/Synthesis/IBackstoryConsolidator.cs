using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.SessionSetup
{
    public interface IBackstoryConsolidator
    {
        Task<string> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            string gameSystemPrompt,
            IReadOnlyList<string> backstoryFragments,
            IReadOnlyList<string> textingStyleSignals,
            string stats,
            CancellationToken cancellationToken = default);
    }
}

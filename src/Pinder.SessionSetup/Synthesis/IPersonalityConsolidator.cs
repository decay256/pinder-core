using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.SessionSetup
{
    public interface IPersonalityConsolidator
    {
        Task<string> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            string gameSystemPrompt,
            IReadOnlyList<string> personalityFragments,
            IReadOnlyList<string> textingStyleSignals,
            string stats,
            CancellationToken cancellationToken = default);
    }
}

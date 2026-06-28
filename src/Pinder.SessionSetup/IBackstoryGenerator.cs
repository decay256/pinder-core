using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    public interface IBackstoryGenerator
    {
        Task<Dictionary<string, BackstoryFact>> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            IReadOnlyList<string> backstoryFragments,
            CancellationToken cancellationToken = default);
    }
}

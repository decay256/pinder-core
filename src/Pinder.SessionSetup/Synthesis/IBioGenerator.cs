using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    public interface IBioGenerator
    {
        Task<string> GenerateAsync(
            string characterName,
            string genderIdentity,
            Dictionary<string, BackstoryFact> backstory,
            List<string> stakeLines,
            Dictionary<string, string> diagnosis,
            CancellationToken cancellationToken = default);
    }
}

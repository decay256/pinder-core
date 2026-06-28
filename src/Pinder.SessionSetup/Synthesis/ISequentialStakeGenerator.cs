using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    public interface ISequentialStakeGenerator
    {
        Task<List<string>> GenerateAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            Dictionary<string, BackstoryFact> backstory, 
            CancellationToken cancellationToken = default);
    }
}

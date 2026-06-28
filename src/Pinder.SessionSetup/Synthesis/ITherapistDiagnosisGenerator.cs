using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    public interface ITherapistDiagnosisGenerator
    {
        Task<Dictionary<string, string>> GenerateAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            Dictionary<string, BackstoryFact> backstory, 
            List<string> stakeLines, 
            CancellationToken cancellationToken = default);
    }
}

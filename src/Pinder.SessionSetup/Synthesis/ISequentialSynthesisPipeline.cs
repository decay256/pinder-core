using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.SessionSetup
{
    public interface ISequentialSynthesisPipeline
    {
        Task<CharacterSynthesisResult> SynthesizeAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            IReadOnlyList<string> looksAndAssetFragments, 
            CancellationToken cancellationToken = default);
    }
}

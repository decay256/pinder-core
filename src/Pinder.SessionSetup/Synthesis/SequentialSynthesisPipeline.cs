using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    public class SequentialSynthesisPipeline : ISequentialSynthesisPipeline
    {
        private readonly IBackstoryGenerator _backstoryGenerator;
        private readonly ISequentialStakeGenerator _stakeGenerator;
        private readonly ITherapistDiagnosisGenerator _diagnosisGenerator;

        public SequentialSynthesisPipeline(
            IBackstoryGenerator backstoryGenerator,
            ISequentialStakeGenerator stakeGenerator,
            ITherapistDiagnosisGenerator diagnosisGenerator)
        {
            _backstoryGenerator = backstoryGenerator;
            _stakeGenerator = stakeGenerator;
            _diagnosisGenerator = diagnosisGenerator;
        }

        public async Task<CharacterSynthesisResult> SynthesizeAsync(
            string characterName, 
            string genderIdentity, 
            string bio, 
            IReadOnlyList<string> looksAndAssetFragments, 
            CancellationToken cancellationToken = default)
        {
            // Stage 1: Generate Backstory
            var backstory = await _backstoryGenerator.GenerateAsync(
                characterName, genderIdentity, bio, looksAndAssetFragments, cancellationToken);

            // Stage 2: Generate Stakes
            var stakes = await _stakeGenerator.GenerateAsync(
                characterName, genderIdentity, bio, backstory, cancellationToken);

            // Stage 3: Generate Therapist Diagnosis
            // NOTE: A genuinely empty-but-valid diagnosis (character has no
            // notable psychiatric traits) comes back as an empty dictionary
            // from the generator and is treated as a normal, successful
            // result below. A malformed/unparseable LLM response is a
            // different thing entirely — the generator throws in that case
            // (see LlmTherapistDiagnosisGenerator) instead of masquerading
            // as an empty diagnosis, and this pipeline deliberately does not
            // catch that exception: it must propagate to the caller
            // (CharacterRepository's regeneration flow) so the failure is
            // recorded as a genuine failure rather than a completed
            // regeneration with silently-missing diagnosis data.
            var diagnosis = await _diagnosisGenerator.GenerateAsync(
                characterName, genderIdentity, bio, backstory, stakes, cancellationToken);

            return new CharacterSynthesisResult
            {
                Backstory = backstory,
                StakeLines = stakes,
                PsychiatricDiagnosis = diagnosis
            };
        }
    }
}

using System;
using System.Collections.Generic;

namespace Pinder.LlmAdapters
{
    public sealed class PromptCompilationInput
    {
        public string CharacterProfile { get; }
        public IReadOnlyList<string> BackstoryFacts { get; }
        public IReadOnlyList<string> PsychologicalStakes { get; }

        public PromptCompilationInput(
            string characterProfile,
            IReadOnlyList<string> backstoryFacts,
            IReadOnlyList<string> psychologicalStakes)
        {
            CharacterProfile = characterProfile ?? throw new ArgumentNullException(nameof(characterProfile));
            BackstoryFacts = backstoryFacts ?? throw new ArgumentNullException(nameof(backstoryFacts));
            PsychologicalStakes = psychologicalStakes ?? throw new ArgumentNullException(nameof(psychologicalStakes));
        }
    }
}

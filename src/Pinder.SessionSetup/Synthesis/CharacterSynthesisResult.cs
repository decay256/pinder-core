using System.Collections.Generic;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    public class CharacterSynthesisResult
    {
        public Dictionary<string, BackstoryFact> Backstory { get; set; } = new Dictionary<string, BackstoryFact>();
        public List<string> StakeLines { get; set; } = new List<string>();
        public Dictionary<string, string> PsychiatricDiagnosis { get; set; } = new Dictionary<string, string>();
        public string Bio { get; set; } = string.Empty;
        public string ConsolidatedPersonality { get; set; } = string.Empty;
        public string ConsolidatedBackstory { get; set; } = string.Empty;
    }
}

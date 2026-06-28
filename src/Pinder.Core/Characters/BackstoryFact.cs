using System.Text.Json.Serialization;

namespace Pinder.Core.Characters
{
    public sealed class BackstoryFact
    {
        public BackstoryFact() { }
        public BackstoryFact(string bioLie, string tragicReality, string? optional = null)
        {
            BioLie = bioLie;
            TragicReality = tragicReality;
        }

        [JsonPropertyName("bio_lie")]
        public string BioLie { get; set; } = string.Empty;
        [JsonPropertyName("tragic_reality")]
        public string TragicReality { get; set; } = string.Empty;
    }
}

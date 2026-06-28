using System.Text.Json.Serialization;

namespace Pinder.Core.Characters
{
    public sealed class BackstoryFact
    {
        [JsonPropertyName("bio_lie")]
        public string BioLie { get; set; } = string.Empty;
        [JsonPropertyName("tragic_reality")]
        public string TragicReality { get; set; } = string.Empty;
    }
}

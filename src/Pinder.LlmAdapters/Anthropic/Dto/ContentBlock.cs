using Newtonsoft.Json;

namespace Pinder.LlmAdapters.Anthropic.Dto
{
    /// <summary>
    /// Represents a content block in the Anthropic Messages API system prompt.
    /// Supports both plain text and cached text blocks.
    /// </summary>
    public sealed class ContentBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text")]
        public string Text { get; set; } = "";

        [JsonProperty("cache_control", NullValueHandling = NullValueHandling.Ignore)]
        public CacheControl? CacheControl { get; set; }
    }

    /// <summary>
    /// Cache control directive for Anthropic prompt caching.
    /// </summary>
    public sealed class CacheControl
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "ephemeral";
    }

    /// <summary>
    /// A message in the Anthropic Messages API conversation.
    /// </summary>
    public sealed class Message
    {
        [JsonProperty("role")]
        public string Role { get; set; } = "";

        [JsonProperty("content")]
        public string Content { get; set; } = "";
    }
}

using System;
using Newtonsoft.Json;

namespace Pinder.LlmAdapters.Anthropic.Dto
{
    /// <summary>
    /// Deserializes an Anthropic Messages API response.
    /// </summary>
    public sealed class MessagesResponse
    {
        [JsonProperty("content")]
        public ResponseContent[] Content { get; set; } = Array.Empty<ResponseContent>();

        [JsonProperty("usage")]
        public UsageStats? Usage { get; set; }

        /// <summary>
        /// Returns the text of the first content block, or empty string if no content blocks exist.
        /// </summary>
        public string GetText() => Content.Length > 0 ? Content[0].Text : "";
    }

    /// <summary>
    /// A single content block in an Anthropic response.
    /// </summary>
    public sealed class ResponseContent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("text")]
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// Token usage statistics from the Anthropic API, including prompt caching metrics.
    /// </summary>
    public sealed class UsageStats
    {
        [JsonProperty("input_tokens")]
        public int InputTokens { get; set; }

        [JsonProperty("output_tokens")]
        public int OutputTokens { get; set; }

        [JsonProperty("cache_creation_input_tokens")]
        public int CacheCreationInputTokens { get; set; }

        [JsonProperty("cache_read_input_tokens")]
        public int CacheReadInputTokens { get; set; }
    }
}

using System;
using Newtonsoft.Json;

namespace Pinder.LlmAdapters.Anthropic.Dto
{
    /// <summary>
    /// Serializes to the Anthropic Messages API request body.
    /// </summary>
    public sealed class MessagesRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; } = "";

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; } = 1024;

        [JsonProperty("temperature")]
        public double Temperature { get; set; } = 0.9;

        [JsonProperty("system")]
        public ContentBlock[] System { get; set; } = Array.Empty<ContentBlock>();

        [JsonProperty("messages")]
        public Message[] Messages { get; set; } = Array.Empty<Message>();
    }
}

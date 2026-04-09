using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        public string GetText() => Content.Length > 0 ? Content[0].Text ?? "" : "";

        /// <summary>
        /// Returns the tool input from the first tool_use content block, or null if none.
        /// </summary>
        public JObject GetToolInput()
        {
            if (Content == null) return null;
            for (int i = 0; i < Content.Length; i++)
            {
                if (string.Equals(Content[i].Type, "tool_use", StringComparison.OrdinalIgnoreCase)
                    && Content[i].Input != null)
                {
                    return Content[i].Input;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns true if the response contains a tool_use content block.
        /// </summary>
        public bool HasToolUse
        {
            get
            {
                if (Content == null) return false;
                for (int i = 0; i < Content.Length; i++)
                {
                    if (string.Equals(Content[i].Type, "tool_use", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// A single content block in an Anthropic response.
    /// Handles both "text" and "tool_use" block types.
    /// </summary>
    public sealed class ResponseContent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("text")]
        public string Text { get; set; } = "";

        /// <summary>Tool use ID (present when type is "tool_use").</summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>Tool name (present when type is "tool_use").</summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>Tool input JSON object (present when type is "tool_use").</summary>
        [JsonProperty("input", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Input { get; set; }
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

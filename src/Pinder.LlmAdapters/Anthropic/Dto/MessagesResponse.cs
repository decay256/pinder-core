using System;
using System.Text;
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
        /// Returns the user-visible assistant text from the response.
        /// Concatenates every <c>type: "text"</c> content block in order and skips
        /// non-text blocks such as <c>thinking</c> / <c>redacted_thinking</c>
        /// (Anthropic extended thinking — issue #320) and <c>tool_use</c> (which is
        /// surfaced separately via <see cref="GetToolInput"/>). Returns the empty
        /// string when no text blocks are present.
        /// </summary>
        /// <remarks>
        /// Pre-#320 this method returned <c>Content[0].Text</c>. That was correct
        /// only when the model emitted text as the very first block; with extended
        /// thinking enabled the first block is <c>type: "thinking"</c> and the
        /// real answer sits in a later <c>type: "text"</c> block, so the old
        /// behaviour returned the empty string and downstream parsers reported
        /// <c>matchup_llm_failed</c> / similar empty-output errors.
        /// </remarks>
        public string GetText()
        {
            if (Content == null || Content.Length == 0) return "";
            StringBuilder? sb = null;
            for (int i = 0; i < Content.Length; i++)
            {
                var block = Content[i];
                if (block == null) continue;
                // Only "text" blocks contribute to the user-visible answer. Thinking
                // and redacted_thinking blocks are deliberately suppressed; tool_use
                // is exposed separately via GetToolInput().
                if (!string.Equals(block.Type, "text", StringComparison.OrdinalIgnoreCase))
                    continue;
                var text = block.Text;
                if (string.IsNullOrEmpty(text)) continue;
                if (sb == null) sb = new StringBuilder(text.Length);
                sb.Append(text);
            }
            return sb?.ToString() ?? "";
        }

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

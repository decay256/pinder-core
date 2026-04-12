using Newtonsoft.Json;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Data Transfer Objects specific to the Anthropic LLM adapter.
    /// </summary>
    public class CallSummaryStat
    {
        [JsonProperty("turn")] public int Turn { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("cache_creation_input_tokens")] public int CacheCreationInputTokens { get; set; }
        [JsonProperty("cache_read_input_tokens")] public int CacheReadInputTokens { get; set; }
        [JsonProperty("input_tokens")] public int InputTokens { get; set; }
        [JsonProperty("output_tokens")] public int OutputTokens { get; set; }
    }
}

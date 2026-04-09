using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pinder.LlmAdapters.Anthropic.Dto
{
    /// <summary>
    /// Defines a tool for the Anthropic Messages API tool_use feature.
    /// </summary>
    public sealed class ToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("input_schema")]
        public JObject InputSchema { get; set; } = new JObject();
    }

    /// <summary>
    /// Controls which tool the model should use in its response.
    /// </summary>
    public sealed class ToolChoiceOption
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "auto";

        /// <summary>
        /// When type is "tool", specifies which tool to force.
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }
}

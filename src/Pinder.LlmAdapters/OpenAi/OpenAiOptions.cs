namespace Pinder.LlmAdapters.OpenAi
{
    /// <summary>
    /// Configuration carrier for the OpenAI-compatible LLM adapter.
    /// Supports any provider with an OpenAI-compatible chat completions API
    /// (OpenAI, Groq, Together, OpenRouter, Ollama, etc.).
    /// </summary>
    public sealed class OpenAiOptions
    {
        public string ApiKey { get; set; } = "";
        public string BaseUrl { get; set; } = "https://api.openai.com";
        public string Model { get; set; } = "gpt-4o-mini";
        public int MaxTokens { get; set; } = 1024;
        public double Temperature { get; set; } = 0.9;

        /// <summary>
        /// Game definition used for building system prompts.
        /// If null, GameDefinition.PinderDefaults is used.
        /// </summary>
        public GameDefinition? GameDefinition { get; set; }

        public string? DebugDirectory { get; set; }
    }
}

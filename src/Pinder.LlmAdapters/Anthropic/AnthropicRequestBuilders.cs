using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Encapsulates the logic for constructing Anthropic API MessagesRequest objects
    /// and attaching tool definitions.
    /// </summary>
    internal static class AnthropicRequestBuilders
    {
        /// <summary>
        /// Builds a single-user-message MessagesRequest with the given system blocks,
        /// user content, model, max tokens, and temperature.
        /// </summary>
        public static MessagesRequest BuildMessagesRequest(
            string model,
            int maxTokens,
            ContentBlock[] systemBlocks,
            string userContent,
            double temperature)
        {
            return new MessagesRequest
            {
                Model = model,
                MaxTokens = maxTokens,
                Temperature = temperature,
                System = systemBlocks,
                Messages = new[]
                {
                    new Message { Role = "user", Content = userContent }
                }
            };
        }

        /// <summary>
        /// Attaches a single tool definition with forced tool_choice to a request.
        /// </summary>
        public static void AttachTool(MessagesRequest request, ToolDefinition tool)
        {
            request.Tools = new[] { tool };
            request.ToolChoice = ToolSchemas.ForceAny();
        }
    }
}

using System.Threading.Tasks;
using Pinder.LlmAdapters.Anthropic.Dto;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Applies specific textual overlays to messages by leveraging an LLM for
    /// transformation, such as the 'horniness' overlay.
    /// </summary>
    internal static class AnthropicOverlayApplier
    {
        /// <summary>
        /// Apply a horniness overlay to a delivered message by calling the LLM.
        /// Returns the original message if inputs are empty or the LLM call fails.
        /// </summary>
        public static async Task<string> ApplyHorninessOverlayAsync(
            AnthropicClient client,
            AnthropicOptions options,
            string message,
            string instruction)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            string systemPrompt = "You are a message editor. Apply the overlay instruction to the message. Return ONLY the modified message text, nothing else.";
            var systemBlocks = new ContentBlock[]
            {
                new ContentBlock { Type = "text", Text = systemPrompt }
            };

            string userContent = $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

            var request = AnthropicRequestBuilders.BuildMessagesRequest(
                options.Model,
                options.MaxTokens,
                systemBlocks,
                userContent,
                options.DeliveryTemperature ?? 0.7);

            try
            {
                var response = await client.SendMessagesAsync(request).ConfigureAwait(false);
                string result = response?.Content?[0]?.Text;
                return string.IsNullOrWhiteSpace(result) ? message : result.Trim();
            }
            catch
            {
                return message;
            }
        }
    }
}

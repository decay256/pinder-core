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
            string instruction,
            string? opponentContext = null,
            string? archetypeDirective = null)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "Apply the overlay instruction to rewrite the message with the requested tonal shift. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";
            var systemBlocks = new ContentBlock[]
            {
                new ContentBlock { Type = "text", Text = systemPrompt }
            };

            // Inject the speaker's active archetype directive (#372) so the
            // overlay rewrite stays in the character's voice.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nOVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay (preserving the archetype voice above) and return the modified message."
                : $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

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
                if (string.IsNullOrWhiteSpace(result)) return message;
                string trimmed = result.Trim();
                // Detect refusal — fall back to original message silently
                if (trimmed.StartsWith("I can't", System.StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", System.StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;
                return trimmed;
            }
            catch
            {
                return message;
            }
        }
    }
}

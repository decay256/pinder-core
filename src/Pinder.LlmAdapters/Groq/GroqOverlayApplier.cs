using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pinder.LlmAdapters.Groq
{
    public static class GroqOverlayApplier
    {
        private static readonly HttpClient _http = new HttpClient();

        public static async Task<string> ApplyHorninessOverlayAsync(
            string groqApiKey,
            string model,
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

            // Inject the speaker's active archetype directive (#372) so the
            // overlay rewrite stays in the character's voice.
            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nOVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay (preserving the archetype voice above) and return the modified message."
                : $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

            var payload = new
            {
                model = model,
                max_tokens = 400,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {groqApiKey}");
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _http.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return message;

                var json = JObject.Parse(body);
                var text = json["choices"]?[0]?["message"]?["content"]?.Value<string>()?.Trim();

                if (string.IsNullOrWhiteSpace(text)) return message;

                // Refusal detection
                if (text!.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    text.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return message;

                return text;
            }
            catch
            {
                return message;
            }
        }
    }
}

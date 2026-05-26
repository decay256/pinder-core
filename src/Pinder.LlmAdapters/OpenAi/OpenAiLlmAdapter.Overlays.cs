using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters.OpenAi
{
    public sealed partial class OpenAiLlmAdapter
    {
        // ── Overlay / corruption methods (#373, formerly pinder-core #751) ──
        //
        // These were no-op stubs (Task.FromResult(message)) until #373 —
        // production routes overlays through PinderLlmAdapter so the stubs
        // were never hit, but they were still latent bugs for any code path
        // that instantiates OpenAiLlmAdapter directly. The implementations
        // below mirror PinderLlmAdapter's prompt + refusal-detection
        // fallback so they fail safely when the model refuses to comply.
        //
        // The same #351 inline-thinking stripper runs before refusal
        // detection so a `<thinking>...</thinking>` block can't trigger a
        // false fallback to the un-overlaid message.

        /// <inheritdoc />
        public async Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "Apply the overlay instruction to rewrite the message with the requested tonal shift. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";

            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nOVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay (preserving the archetype voice above) and return the modified message."
                : $"OVERLAY INSTRUCTION:\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the overlay and return the modified message.";

            return await SendOverlayWithRefusalFallbackAsync(systemPrompt, userContent, message, DefaultDeliveryTemperature, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(instruction))
                return message;

            string systemPrompt = "You are editing a text message for Pinder, a satirical comedy dating app. " +
                "Apply the shadow corruption instruction to rewrite the delivered message. " +
                "The message has already been through its normal delivery transformation. " +
                "Now the character's shadow stat is corrupting it further. " +
                "Return ONLY the corrupted message text. No commentary, no preamble, no refusals.";

            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nSHADOW CORRUPTION INSTRUCTION ({shadow}):\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the corruption (preserving the archetype voice above) and return the modified message."
                : $"SHADOW CORRUPTION INSTRUCTION ({shadow}):\n{instruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the corruption and return the modified message.";

            return await SendOverlayWithRefusalFallbackAsync(systemPrompt, userContent, message, DefaultDeliveryTemperature, ct)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(trapInstruction))
                return message;

            string systemPrompt = "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. " +
                "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. " +
                "A trap is currently corrupting the character's voice. " +
                "Apply the trap instruction to rewrite the message so the trap's signature taint is visible. " +
                "Return ONLY the rewritten message text. No commentary, no refusals, no preamble.";

            if (!string.IsNullOrWhiteSpace(opponentContext))
                systemPrompt += $"\n\nThe message being sent is directed at this character:\n{opponentContext}";

            string userContent = !string.IsNullOrWhiteSpace(archetypeDirective)
                ? $"{archetypeDirective}\n\nTRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint (preserving the archetype voice above) and return the modified message."
                : $"TRAP INSTRUCTION ({trapName}):\n{trapInstruction}\n\nORIGINAL MESSAGE:\n{message}\n\nApply the trap taint and return the modified message.";

            return await SendOverlayWithRefusalFallbackAsync(systemPrompt, userContent, message, DefaultDeliveryTemperature, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Shared overlay send path: builds the request, sends via OpenAiClient, and
        /// applies the inline-thinking stripper (#351) + refusal-detection fallback.
        /// On any exception or refusal-shaped output, returns the unmodified
        /// <paramref name="originalMessage"/> so a safety refusal never propagates
        /// through to the player.
        /// </summary>
        private async Task<string> SendOverlayWithRefusalFallbackAsync(
            string systemPrompt, string userContent, string originalMessage, double temperature,
            CancellationToken ct = default)
        {
            try
            {
                var requestJson = BuildRequestJson(systemPrompt, userContent, temperature);
                var result = await _client.SendChatCompletionAsync(requestJson, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(result)) return originalMessage;

                // #351: strip inline <thinking>/<reasoning> blocks before
                // refusal-detection so a thinking-block that mentions an
                // apology phrase can't trigger a spurious fallback.
                string trimmed = InlineThinkingStripper.Strip(result).Trim();

                if (trimmed.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.IndexOf("inappropriate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("I'd be happy to help", StringComparison.OrdinalIgnoreCase) >= 0)
                    return originalMessage;

                return trimmed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // #794: cancellation must propagate.
            }
            catch
            {
                return originalMessage;
            }
        }
    }
}

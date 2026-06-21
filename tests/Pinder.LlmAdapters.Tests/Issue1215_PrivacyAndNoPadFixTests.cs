using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Focused regression tests verifying that the strict LLM contract path
    /// prevents privacy leaks (never exposes raw LLM input/lines in exceptions/violations)
    /// and performs zero option padding or remapping fabrication.
    /// </summary>
    public class Issue1215_PrivacyAndNoPadFixTests
    {
        private static DialogueContext MakeDialogueContext(StatType[] availableStats)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("O", "Hi"), ("P", "Hey there") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerName: "P",
                dateeName: "O",
                currentTurn: 2,
                availableStats: availableStats
            );
        }

        private static DateeContext MakeDateeContext()
        {
            return new DateeContext(
                dateePrompt: "datee system prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 15,
                playerDeliveredMessage: "hello",
                interestBefore: 14,
                interestAfter: 15,
                responseDelayMinutes: 2.0,
                playerName: "Velvet",
                dateeName: "Sable"
            );
        }

        private sealed class FixedResponseTransport : ILlmTransport
        {
            private readonly string _response;
            public FixedResponseTransport(string response) => _response = response;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(_response);
        }

        [Fact]
        public async Task Test_PrivacyLeak_MalformedSignals_DoesNotLeakRawText()
        {
            var context = MakeDateeContext();
            const string secretMarker = "SECRETLEAKMARKER12345";
            var transport = new FixedResponseTransport(
                "This is a message.\n" +
                "[SIGNALS]\n" +
                $"TELL: NotAStat ({secretMarker})"
            );
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAsync<LlmContractException>(async () => await adapter.GetDateeResponseAsync(context));

            Assert.NotNull(ex);
            var msg = ex.Message;
            var strRepresentation = ex.ToString();

            // Verify no raw leak
            Assert.DoesNotContain(secretMarker, msg, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secretMarker, strRepresentation, StringComparison.OrdinalIgnoreCase);

            // Verify message still contains structured indicators
            Assert.Contains("datee", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("malformed", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tell_invalid_stat", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Test_DialogueOptions_NoFabrication_ThrowsOnPartialOptions()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            // AvailableStats.Length = 3, but the LLM only returned 2 valid options.
            // Under strict mode, it must throw LlmContractException (partial_options) rather than returning padded placeholders.
            var transport = new FixedResponseTransport(
                "OPTION_1\n[STAT: CHARM]\n\"This is options 1 text.\"\n\n" +
                "OPTION_2\n[STAT: RIZZ]\n\"This is options 2 text.\""
            );
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAsync<LlmContractException>(async () => await adapter.GetDialogueOptionsAsync(context));

            Assert.NotNull(ex);
            Assert.Equal("partial_options", ex.Reason);
            Assert.Contains("option", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("partial", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

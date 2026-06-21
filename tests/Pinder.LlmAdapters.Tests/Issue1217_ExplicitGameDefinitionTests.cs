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
    [Collection("PromptTraceSingleton")]
    public class Issue1217_ExplicitGameDefinitionTests
    {
        // ── 1. PRODUCTION FAIL-LOUD ────────────────────────────────────────

        [Fact]
        public async Task Production_GetDialogueOptions_ThrowsInvalidOperationException_WhenGameDefinitionIsNull()
        {
            // Arrange
            var transport = new FixedResponseTransport("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n\"Hi\"");
            var options = new PinderLlmAdapterOptions { GameDefinition = null };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "player spec",
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                dateeName: "O",
                availableStats: new[] { StatType.Charm }
            );

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDialogueOptionsAsync(context));
            Assert.Contains("GameDefinition", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Production_GetDateeResponse_ThrowsInvalidOperationException_WhenGameDefinitionIsNull()
        {
            // Arrange
            var transport = new FixedResponseTransport("some datee response");
            var options = new PinderLlmAdapterOptions { GameDefinition = null };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0.5,
                playerName: "P",
                dateeName: "O"
            );

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDateeResponseAsync(context));
            Assert.Contains("GameDefinition", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 2. TEST/DEV FALLBACK PARITY ────────────────────────────────────

        [Fact]
        public void PinderDefaults_GameMasterPrompt_DoesNotContainStaleRizzHorniness_AndContainsDespair()
        {
            var gmPrompt = GameDefinition.PinderDefaults.GameMasterPrompt;

            // Assert the stale token 'Rizz/Horniness' is not present
            Assert.DoesNotContain("Rizz/Horniness", gmPrompt);

            // Assert that 'Despair' is present in the Rizz-pairing context
            Assert.Contains("Rizz/Despair", gmPrompt);
            Assert.Contains("Despair", gmPrompt);
        }

        // ── 3. GUARD (provided definition still works) ──────────────────────

        [Fact]
        public async Task Production_GetDialogueOptions_DoesNotThrow_WhenGameDefinitionIsProvided()
        {
            // Arrange
            var transport = new FixedResponseTransport("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n\"Hi\"");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "player spec",
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                dateeName: "O",
                availableStats: new[] { StatType.Charm }
            );

            // Act & Assert
            var exception = await Record.ExceptionAsync(() => adapter.GetDialogueOptionsAsync(context));
            Assert.Null(exception);
        }

        [Fact]
        public async Task Production_GetDateeResponse_DoesNotThrow_WhenGameDefinitionIsProvided()
        {
            // Arrange
            var transport = new FixedResponseTransport("some datee response");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0.5,
                playerName: "P",
                dateeName: "O"
            );

            // Act & Assert
            var exception = await Record.ExceptionAsync(() => adapter.GetDateeResponseAsync(context));
            Assert.Null(exception);
        }

        // ── Fake Transport ─────────────────────────────────────────────────

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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class Issue1216_ExplicitOverlayFallbackTests
    {
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

        // 1. STEERING HARDCODED FALLBACK REMOVED (RED)
        [Fact]
        public async Task GetSteeringQuestionAsync_WhenTransportReturnsEmpty_DoesNotReturnHardcodedFallback()
        {
            // Arrange
            var transport = new FixedResponseTransport("");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);
            var context = new SteeringContext(
                playerAvatarPrompt: "player spec",
                dateeName: "O",
                playerName: "P",
                deliveredMessage: "Hello",
                conversationHistory: new List<(string, string)> { ("O", "Hi") }
            );

            // Act
            var result = await adapter.GetSteeringQuestionAsync(context);

            // Assert
            Assert.NotEqual("so... when are we doing this?", result);
            Assert.True(string.IsNullOrWhiteSpace(result));
        }

        // 2. OVERLAY-DEGRADATION CALLBACK EXISTS (RED via reflection)
        [Fact]
        public void PinderLlmAdapterOptions_ExposesPublicSettablePropertyForOverlayDegradation()
        {
            // Arrange
            var properties = typeof(PinderLlmAdapterOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Act
            var matchingProperty = properties.FirstOrDefault(p =>
            {
                var name = p.Name;
                bool containsOverlay = name.Contains("Overlay", StringComparison.OrdinalIgnoreCase);
                bool containsOneOf = name.Contains("Degrad", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Outcome", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Fallback", StringComparison.OrdinalIgnoreCase);
                bool hasSetter = p.CanWrite && p.GetSetMethod() != null;
                return containsOverlay && containsOneOf && hasSetter;
            });

            // Assert
            Assert.NotNull(matchingProperty);
        }

        // 3. GUARD — SUCCESSFUL OVERLAY UNCHANGED (must PASS before & after)
        [Fact]
        public async Task ApplyHorninessOverlayAsync_WhenTransportReturnsValidMessage_ReturnsRewrittenMessage()
        {
            // Arrange
            var transport = new FixedResponseTransport("rewritten horny text");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyHorninessOverlayAsync(message: "orig", instruction: "make it horny");

            // Assert
            Assert.Equal("rewritten horny text", result.Trim());
        }

        // 4. GUARD — LEGIT NO-OP (must PASS before & after)
        [Fact]
        public async Task ApplyHorninessOverlayAsync_WhenInstructionIsEmpty_ReturnsOriginalMessageUnchanged()
        {
            // Arrange
            var transport = new FixedResponseTransport("some alternative text");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyHorninessOverlayAsync(message: "orig", instruction: string.Empty);

            // Assert
            Assert.Equal("orig", result);
        }

        // 5. GUARD — DEGRADED-ON-EMPTY NOT BRITTLE (must PASS before & after)
        [Fact]
        public async Task ApplyHorninessOverlayAsync_WhenTransportReturnsEmpty_ReturnsOriginalMessage()
        {
            // Arrange
            var transport = new FixedResponseTransport("");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyHorninessOverlayAsync(message: "orig msg", instruction: "make it horny");

            // Assert
            Assert.Equal("orig msg", result);
        }
    }
}

using System;
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
    public class Issue1292_UnifiedOverlayTransportTests
    {
        private sealed class CountingTransport : ILlmTransport
        {
            public int Calls { get; set; }
            private readonly string _response;
            public CountingTransport(string response = "primary-was-called") => _response = response;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(_response);
            }
        }

        [Fact]
        public async Task ApplyHorninessOverlayAsync_NoOverlayTransportProvided_UsesPrimaryTransport()
        {
            // Arrange
            var primary = new CountingTransport("primary-horniness");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            // Using the 3-arg constructor with overlayTransport: null
            var adapter = new PinderLlmAdapter(primary, options, overlayTransport: null);

            // Act
            var result = await adapter.ApplyHorninessOverlayAsync("some message", "some instruction");

            // Assert
            Assert.Equal(1, primary.Calls);
            Assert.Equal("primary-horniness", result);
        }

        [Fact]
        public async Task ApplyHorninessOverlayAsync_OverlayTransportProvided_RoutesToOverlayTransportNotPrimary()
        {
            // Arrange
            var primary = new CountingTransport("primary-horniness");
            var overlay = new CountingTransport("overlay-horniness");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            // Using the 3-arg constructor
            var adapter = new PinderLlmAdapter(primary, options, overlayTransport: overlay);

            // Act
            var result = await adapter.ApplyHorninessOverlayAsync("some message", "some instruction");

            // Assert
            Assert.Equal(1, overlay.Calls);
            Assert.Equal(0, primary.Calls);
            Assert.Equal("overlay-horniness", result);
        }

        [Fact]
        public async Task ApplyTrapOverlayAsync_NoOverlayTransportProvided_UsesPrimaryTransport()
        {
            // Arrange
            var primary = new CountingTransport("primary-trap");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            // Using the 3-arg constructor with overlayTransport: null
            var adapter = new PinderLlmAdapter(primary, options, overlayTransport: null);

            // Act
            var result = await adapter.ApplyTrapOverlayAsync("some message", "some instruction", "clown-trap");

            // Assert
            Assert.Equal(1, primary.Calls);
            Assert.Equal("primary-trap", result);
        }

        [Fact]
        public async Task ApplyTrapOverlayAsync_OverlayTransportProvided_RoutesToOverlayTransportNotPrimary()
        {
            // Arrange
            var primary = new CountingTransport("primary-trap");
            var overlay = new CountingTransport("overlay-trap");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            // Using the 3-arg constructor
            var adapter = new PinderLlmAdapter(primary, options, overlayTransport: overlay);

            // Act
            var result = await adapter.ApplyTrapOverlayAsync("some message", "some instruction", "clown-trap");

            // Assert
            Assert.Equal(1, overlay.Calls);
            Assert.Equal(0, primary.Calls);
            Assert.Equal("overlay-trap", result);
        }

        [Fact]
        public async Task ApplyShadowCorruptionAsync_NoOverlayTransportProvided_UsesPrimaryTransport()
        {
            // Arrange
            var primary = new CountingTransport("primary-shadow");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            // Using the 3-arg constructor with overlayTransport: null
            var adapter = new PinderLlmAdapter(primary, options, overlayTransport: null);

            // Act
            var result = await adapter.ApplyShadowCorruptionAsync("some message", "some instruction", ShadowStatType.Madness);

            // Assert
            Assert.Equal(1, primary.Calls);
            Assert.Equal("primary-shadow", result);
        }

        [Fact]
        public async Task ApplyShadowCorruptionAsync_OverlayTransportProvided_RoutesToOverlayTransportNotPrimary()
        {
            // Arrange
            var primary = new CountingTransport("primary-shadow");
            var overlay = new CountingTransport("overlay-shadow");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            // Using the 3-arg constructor
            var adapter = new PinderLlmAdapter(primary, options, overlayTransport: overlay);

            // Act
            var result = await adapter.ApplyShadowCorruptionAsync("some message", "some instruction", ShadowStatType.Madness);

            // Assert
            Assert.Equal(1, overlay.Calls);
            Assert.Equal(0, primary.Calls);
            Assert.Equal("overlay-shadow", result);
        }
    }
}

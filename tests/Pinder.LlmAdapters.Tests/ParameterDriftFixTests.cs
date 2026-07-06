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
    public class ParameterDriftFixTests
    {
        private sealed class TemperatureTrackingTransport : ILlmTransport
        {
            public double LastTemperature { get; private set; } = -1.0;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                LastTemperature = temperature;
                return Task.FromResult("mocked-response");
            }
        }

        [Fact]
        public async Task ApplyHorninessOverlayAsync_UsesDefaultDeliveryTemperature_WhenOptionsTemperatureIsNull()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = null
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyHorninessOverlayAsync("hello", "make it horny");

            // Assert
            Assert.Equal(0.7, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyHorninessOverlayAsync_UsesOptionsTemperature_WhenProvided()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = 0.5
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyHorninessOverlayAsync("hello", "make it horny");

            // Assert
            Assert.Equal(0.5, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyTrapOverlayAsync_UsesDefaultDeliveryTemperature_WhenOptionsTemperatureIsNull()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = null
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyTrapOverlayAsync("hello", "trap them", "clown-trap");

            // Assert
            Assert.Equal(0.7, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyTrapOverlayAsync_UsesOptionsTemperature_WhenProvided()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = 0.5
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyTrapOverlayAsync("hello", "trap them", "clown-trap");

            // Assert
            Assert.Equal(0.5, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyShadowCorruptionAsync_UsesDefaultDeliveryTemperature_WhenOptionsTemperatureIsNull()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = null
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyShadowCorruptionAsync("hello", "corrupt them", ShadowStatType.Fixation);

            // Assert
            Assert.Equal(0.7, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyShadowCorruptionAsync_UsesOptionsTemperature_WhenProvided()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = 0.5
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyShadowCorruptionAsync("hello", "corrupt them", ShadowStatType.Fixation);

            // Assert
            Assert.Equal(0.5, transport.LastTemperature);
        }
    }
}

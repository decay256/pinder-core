using System;
using System.IO;
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
            Assert.Equal(LlmPhaseTemperatures.OverlayRewrite, transport.LastTemperature);
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
            Assert.Equal(LlmPhaseTemperatures.OverlayRewrite, transport.LastTemperature);
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
            Assert.Equal(LlmPhaseTemperatures.OverlayRewrite, transport.LastTemperature);
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

        [Fact]
        public void PinderLlmAdapterOptions_DefaultsToCanonicalLlmPhaseTemperature()
        {
            var options = new PinderLlmAdapterOptions();

            Assert.Equal(LlmPhaseTemperatures.Default, options.Temperature);
        }

        [Fact]
        public void PinderLlmAdapter_SourceUsesCanonicalTemperatureRegistry()
        {
            string source = File.ReadAllText(FindRepoFile("src", "Pinder.LlmAdapters", "PinderLlmAdapter.cs"));

            Assert.DoesNotContain("DefaultDialogueOptionsTemperature", source);
            Assert.DoesNotContain("DefaultDeliveryTemperature", source);
            Assert.DoesNotContain("DefaultDateeResponseTemperature", source);
            Assert.Contains("LlmPhaseTemperatures.DialogueOptions", source);
            Assert.Contains("LlmPhaseTemperatures.OverlayRewrite", source);
            Assert.Contains("LlmPhaseTemperatures.DateeResponse", source);
        }

        private static string FindRepoFile(params string[] segments)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, Path.Combine(segments));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not find repo file.", Path.Combine(segments));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public class Issue1287_ShadowCorruptionGroqRoutingTests
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

        // (a) [Fact] NoGroqConfigured_UsesPrimaryTransport_ReturnsRewrite
        [Fact]
        public async Task NoGroqConfigured_UsesPrimaryTransport_ReturnsRewrite()
        {
            // Arrange
            var transport = new FixedResponseTransport("shadow-rewritten");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyShadowCorruptionAsync("orig", "corrupt it", ShadowStatType.Madness);

            // Assert
            Assert.Equal("shadow-rewritten", result.Trim());
        }

        // (b) [Fact] EmptyInstruction_ReturnsOriginalUnchanged
        [Fact]
        public async Task EmptyInstruction_ReturnsOriginalUnchanged()
        {
            // Arrange
            var transport = new FixedResponseTransport("some-rewrite");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyShadowCorruptionAsync("orig", "", ShadowStatType.Madness);

            // Assert
            Assert.Equal("orig", result);
        }

        // (c) [Fact] PrimaryReturnsEmpty_ReturnsOriginalMessage
        [Fact]
        public async Task PrimaryReturnsEmpty_ReturnsOriginalMessage()
        {
            // Arrange
            var transport = new FixedResponseTransport("");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            var result = await adapter.ApplyShadowCorruptionAsync("orig msg", "corrupt it", ShadowStatType.Madness);

            // Assert
            Assert.Equal("orig msg", result);
        }

        // (d) [Fact] GroqOverlayApplier_HasPublicShadowCorruptionMethodWithShadowParam
        [Fact]
        public void GroqOverlayApplier_HasPublicShadowCorruptionMethodWithShadowParam()
        {
            // Arrange & Act
            var method = typeof(Pinder.LlmAdapters.Groq.GroqOverlayApplier)
                .GetMethod("ApplyShadowCorruptionAsync", BindingFlags.Public | BindingFlags.Static);

            // Assert
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            var hasShadowParam = parameters.Any(p => p.ParameterType == typeof(Pinder.Core.Stats.ShadowStatType));
            Assert.True(hasShadowParam, "Method should have a parameter of type ShadowStatType.");
        }

        // (e) [Fact] WhenGroqConfigured_PrimaryTransportIsBypassed
        [Fact]
        public async Task WhenGroqConfigured_PrimaryTransportIsBypassed()
        {
            // Arrange
            var transport = new CountingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                OverlayGroqModel = "llama-3.3-70b-versatile",
                OverlayGroqApiKey = "gsk_test_dummy"
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            // Dummy network call with invalid API key fails fast.
            // Using a safe CancellationToken to absolutely prevent hanging.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await adapter.ApplyShadowCorruptionAsync("orig", "corrupt it", ShadowStatType.Madness, ct: cts.Token);

            // Assert
            Assert.Equal(0, transport.Calls);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests.SessionSetup
{
    [Trait("Category", "SessionSetup")]
    public class Issue1158_DramaticArcGenerationTests
    {
        [Fact]
        public async Task GenerateAsync_DefaultsToCatalogMaxTokens1000()
        {
            var transport = new QueueLlmTransport(
                "Setup lands softly. Escalation tilts without forcing a choice. The turn can break either way.");
            var generator = new LlmDramaticArcGenerator(transport);

            await GenerateAsync(generator);

            Assert.Equal(1000, transport.LastMaxTokens);
            Assert.Equal(LlmPhase.DramaticArc, transport.LastPhase);
        }

        [Fact]
        public async Task GenerateAsync_AttributesBothPromptPartsToDramaticArcCatalog()
        {
            var traceService = InMemoryPromptTraceService.Instance;
            traceService.Clear();
            var generator = new LlmDramaticArcGenerator(new QueueLlmTransport(
                "Setup lands softly. Escalation tilts without forcing a choice. The turn can break either way."));

            await GenerateAsync(generator);

            var systemTrace = traceService.GetLastTrace("dramatic-arc-system");
            var userTrace = traceService.GetLastTrace("dramatic-arc-user");
            Assert.NotNull(systemTrace);
            Assert.NotNull(userTrace);
            Assert.Contains(systemTrace!.Spans, span =>
                span.SourceFile == "data/prompts/dramatic_arc.yaml" &&
                span.Key == "dramatic_arc.system_prompt");
            Assert.Contains(userTrace!.Spans, span =>
                span.SourceFile == "data/prompts/dramatic_arc.yaml" &&
                span.Key == "dramatic_arc.user_template");
        }

        [Fact]
        public async Task GenerateAsync_RetriesIncompleteArcAndPreservesValidText()
        {
            const string valid =
                "Setup lands softly.  Escalation tilts without forcing a choice. The turn can break either way.";
            var transport = new QueueLlmTransport(
                "Only one complete sentence.",
                valid);
            var generator = new LlmDramaticArcGenerator(transport);

            string result = await GenerateAsync(generator);

            Assert.Equal(valid, result);
            Assert.Equal(2, transport.CallCount);
        }

        [Theory]
        [InlineData("Setup lands softly. Escalation tilts without forcing a choice. The turn can break either way.\"")]
        [InlineData("Setup lands softly. Escalation tilts without forcing a choice. The turn can break either way.)")]
        [InlineData("Setup lands softly. Escalation tilts without forcing a choice. The turn can break either way.]}")]
        public async Task GenerateAsync_AcceptsClosingDelimitersAfterTerminalPunctuation(string response)
        {
            var transport = new QueueLlmTransport(response);
            var generator = new LlmDramaticArcGenerator(transport);

            string result = await GenerateAsync(generator);

            Assert.Equal(response, result);
            Assert.Equal(1, transport.CallCount);
        }

        [Fact]
        public async Task GenerateAsync_IncompleteArcFailsExplicitlyAfterExhaustion()
        {
            SetupGenerationResult? degraded = null;
            var transport = new QueueLlmTransport(
                "Only one complete sentence.",
                "Still only two. Not enough.");
            var generator = new LlmDramaticArcGenerator(
                transport,
                new LlmDramaticArcGenerator.Options
                {
                    MaxValidationAttempts = 2,
                    OnDegraded = result => degraded = result,
                });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => GenerateAsync(generator));

            Assert.Contains("3-5 complete sentences", ex.Message, StringComparison.Ordinal);
            Assert.Equal(2, transport.CallCount);
            Assert.NotNull(degraded);
            Assert.True(degraded!.Degraded);
            Assert.Equal("dramatic_arc", degraded.GeneratorName);
            Assert.Equal("invalid_output", degraded.ErrorCode);
        }

        [Fact]
        public async Task GenerateAsync_RecoverableTransportFailureWithCallback_DegradesToEmpty()
        {
            SetupGenerationResult? degraded = null;
            var generator = new LlmDramaticArcGenerator(
                new ThrowingTransport(new LlmTransportException("network down")),
                new LlmDramaticArcGenerator.Options { OnDegraded = result => degraded = result });

            string result = await GenerateAsync(generator);

            Assert.Equal(string.Empty, result);
            Assert.NotNull(degraded);
            Assert.Equal("dramatic_arc", degraded!.GeneratorName);
            Assert.Equal("transport_error", degraded.ErrorCode);
        }

        [Fact]
        public async Task GenerateAsync_UnexpectedTransportException_BubblesEvenWithCallback()
        {
            SetupGenerationResult? degraded = null;
            var generator = new LlmDramaticArcGenerator(
                new ThrowingTransport(new InvalidOperationException("serializer bug")),
                new LlmDramaticArcGenerator.Options { OnDegraded = result => degraded = result });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(generator));

            Assert.Equal("serializer bug", ex.Message);
            Assert.Null(degraded);
        }

        [Fact]
        public async Task GenerateAsync_CancellationStillBubblesWithCallback()
        {
            SetupGenerationResult? degraded = null;
            var generator = new LlmDramaticArcGenerator(
                new ThrowingTransport(new OperationCanceledException("cancelled")),
                new LlmDramaticArcGenerator.Options { OnDegraded = result => degraded = result });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GenerateAsync(generator));

            Assert.Null(degraded);
        }

        private static Task<string> GenerateAsync(LlmDramaticArcGenerator generator) =>
            generator.GenerateAsync(
                "Player",
                "Player stake",
                "Player bio",
                "Datee",
                "Datee stake",
                "Datee bio");

        private sealed class QueueLlmTransport : ILlmTransport
        {
            private readonly Queue<string> _responses;

            public int CallCount { get; private set; }
            public int? LastMaxTokens { get; private set; }
            public string? LastPhase { get; private set; }

            public QueueLlmTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                CallCount++;
                LastMaxTokens = maxTokens;
                LastPhase = phase;
                return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : string.Empty);
            }
        }

        private sealed class ThrowingTransport : ILlmTransport
        {
            private readonly Exception _exception;

            public ThrowingTransport(Exception exception)
            {
                _exception = exception;
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                throw _exception;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests.SessionSetup
{
    /// <summary>
    /// Streaming-overload behaviour for <see cref="LlmMatchupAnalyzer"/>:
    /// fragment flow, prompt forwarding, cancellation, transport-failure
    /// translation into <see cref="LlmTransportException"/>.
    /// </summary>
    public class LlmMatchupAnalyzerStreamTests
    {
        // ── happy path ────────────────────────────────────────────────────

        [Fact]
        public async Task StreamMatchupAsync_ReturnsAllFragmentsInOrder()
        {
            var fragments = new[] { "Alice ", "(level 1) ", "is the player. ", "Prediction: it goes well." };
            var streaming = new FakeStreamingTransport(fragments);
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            var player = CharacterFactory.Make("Alice");
            var opponent = CharacterFactory.Make("Bob");

            var collected = new List<string>();
            await foreach (var fragment in analyzer.StreamMatchupAsync(player, opponent))
            {
                collected.Add(fragment);
            }

            Assert.Equal(fragments, collected);
            Assert.Equal(fragments.Length, streaming.FragmentsYielded);
        }

        [Fact]
        public async Task StreamMatchupAsync_ForwardsPlainTextSystemPrompt()
        {
            var streaming = new FakeStreamingTransport(new[] { "ok" });
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            await foreach (var _ in analyzer.StreamMatchupAsync(
                CharacterFactory.Make("A"), CharacterFactory.Make("B"))) { }

            Assert.NotNull(streaming.LastSystemPrompt);
            // Plain-text contract: the system prompt must explicitly forbid markdown.
            Assert.Contains("plain prose", streaming.LastSystemPrompt!);
            Assert.Contains("Do NOT use markdown", streaming.LastSystemPrompt!);
        }

        [Fact]
        public async Task StreamMatchupAsync_ForwardsTemperatureAndMaxTokens()
        {
            var streaming = new FakeStreamingTransport(new[] { "ok" });
            var options = new LlmMatchupAnalyzer.Options { Temperature = 0.42, MaxTokens = 123 };
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming, options);

            await foreach (var _ in analyzer.StreamMatchupAsync(
                CharacterFactory.Make("A"), CharacterFactory.Make("B"))) { }

            Assert.Equal(0.42, streaming.LastTemperature);
            Assert.Equal(123, streaming.LastMaxTokens);
        }

        // ── cancellation ──────────────────────────────────────────────────

        [Fact]
        public async Task StreamMatchupAsync_HonoursCancellation_BeforeStart()
        {
            var streaming = new FakeStreamingTransport(new[] { "x", "y", "z" });
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var player = CharacterFactory.Make("A");
            var opponent = CharacterFactory.Make("B");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(player, opponent, cts.Token)) { }
            });
        }

        [Fact]
        public async Task StreamMatchupAsync_HonoursCancellation_MidStream()
        {
            var streaming = new FakeStreamingTransport(new[] { "a", "b", "c", "d", "e" });
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            using var cts = new CancellationTokenSource();
            int seen = 0;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(
                                   CharacterFactory.Make("A"), CharacterFactory.Make("B"), cts.Token))
                {
                    seen++;
                    if (seen == 2) cts.Cancel();
                }
            });

            Assert.True(seen < 5, $"Expected cancellation to short-circuit, got all {seen} fragments.");
        }

        // ── transport failure ─────────────────────────────────────────────

        [Fact]
        public async Task StreamMatchupAsync_TranslatesMidStreamFailure_IntoLlmTransportException()
        {
            var streaming = FakeStreamingTransport.ThatThrowsAfter(
                new[] { "first ", "second ", "third " }, afterIndex: 1,
                new IOException("network reset"));
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(
                                   CharacterFactory.Make("A"), CharacterFactory.Make("B"))) { }
            });

            Assert.NotNull(ex.InnerException);
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Equal(1, streaming.FragmentsYielded);
        }

        [Fact]
        public async Task StreamMatchupAsync_TranslatesOpenFailure_IntoLlmTransportException()
        {
            var streaming = FakeStreamingTransport.ThatThrowsOnOpen(
                new InvalidOperationException("provider 503"));
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(
                                   CharacterFactory.Make("A"), CharacterFactory.Make("B"))) { }
            });

            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public async Task StreamMatchupAsync_PassesThroughExistingLlmTransportException()
        {
            var streaming = FakeStreamingTransport.ThatThrowsAfter(
                new[] { "first ", "second " }, afterIndex: 1,
                new LlmTransportException("provider returned 429"));
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport(), streaming);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(
                                   CharacterFactory.Make("A"), CharacterFactory.Make("B"))) { }
            });

            // Direct match — no double-wrapping.
            Assert.Equal("provider returned 429", ex.Message);
            Assert.Null(ex.InnerException);
        }

        // ── misuse ────────────────────────────────────────────────────────

        [Fact]
        public async Task StreamMatchupAsync_WithoutStreamingTransport_ThrowsInvalidOperation()
        {
            // Single-arg ctor (no streaming transport) — streaming overload must fail loudly.
            var analyzer = new LlmMatchupAnalyzer(new StubLlmTransport());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(
                                   CharacterFactory.Make("A"), CharacterFactory.Make("B"))) { }
            });
        }

        [Fact]
        public async Task StreamMatchupAsync_NullArgs_ThrowArgumentNullException()
        {
            var analyzer = new LlmMatchupAnalyzer(
                new StubLlmTransport(), new FakeStreamingTransport(new[] { "x" }));

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(null!, CharacterFactory.Make("B"))) { }
            });
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await foreach (var _ in analyzer.StreamMatchupAsync(CharacterFactory.Make("A"), null!)) { }
            });
        }
    }
}

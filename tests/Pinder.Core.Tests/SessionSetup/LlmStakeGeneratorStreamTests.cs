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
    /// Streaming-overload behaviour for <see cref="LlmStakeGenerator"/>:
    /// fragment flow, prompt forwarding, cancellation, transport-failure
    /// translation into <see cref="LlmTransportException"/>.
    /// </summary>
    public class LlmStakeGeneratorStreamTests
    {
        private const string Prompt = "Assembled prompt for tests.";

        // ── happy path ────────────────────────────────────────────────────

        [Fact]
        public async Task StreamStakeAsync_ReturnsAllFragmentsInOrder()
        {
            var fragments = new[] { "Alice ", "spent the morning ", "rearranging IKEA furniture." };
            var streaming = new FakeStreamingTransport(fragments);
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            var collected = new List<string>();
            await foreach (var fragment in gen.StreamStakeAsync("Alice", Prompt))
            {
                collected.Add(fragment);
            }

            Assert.Equal(fragments, collected);
            Assert.Equal(fragments.Length, streaming.FragmentsYielded);
        }

        [Fact]
        public async Task StreamStakeAsync_ForwardsPlainTextSystemPrompt()
        {
            var streaming = new FakeStreamingTransport(new[] { "ok" });
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt)) { }

            Assert.NotNull(streaming.LastSystemPrompt);
            // Plain-text contract: the system prompt must explicitly forbid markdown.
            Assert.Contains("plain prose", streaming.LastSystemPrompt!);
            Assert.Contains("Do NOT use markdown", streaming.LastSystemPrompt!);
        }

        [Fact]
        public async Task StreamStakeAsync_ForwardsTemperatureAndMaxTokens()
        {
            var streaming = new FakeStreamingTransport(new[] { "ok" });
            var options = new LlmStakeGenerator.Options { Temperature = 0.55, MaxTokens = 321 };
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming, options);

            await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt)) { }

            Assert.Equal(0.55, streaming.LastTemperature);
            Assert.Equal(321, streaming.LastMaxTokens);
        }

        // ── cancellation ──────────────────────────────────────────────────

        [Fact]
        public async Task StreamStakeAsync_HonoursCancellation_BeforeStart()
        {
            var streaming = new FakeStreamingTransport(new[] { "x", "y", "z" });
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt, cts.Token)) { }
            });
        }

        [Fact]
        public async Task StreamStakeAsync_HonoursCancellation_MidStream()
        {
            var streaming = new FakeStreamingTransport(new[] { "a", "b", "c", "d", "e" });
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            using var cts = new CancellationTokenSource();
            int seen = 0;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt, cts.Token))
                {
                    seen++;
                    if (seen == 2) cts.Cancel();
                }
            });

            Assert.True(seen < 5, $"Expected cancellation to short-circuit, got all {seen} fragments.");
        }

        // ── transport failure ─────────────────────────────────────────────

        [Fact]
        public async Task StreamStakeAsync_TranslatesMidStreamFailure_IntoLlmTransportException()
        {
            var streaming = FakeStreamingTransport.ThatThrowsAfter(
                new[] { "first ", "second ", "third " }, afterIndex: 1,
                new IOException("network reset"));
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt)) { }
            });

            Assert.NotNull(ex.InnerException);
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Equal(1, streaming.FragmentsYielded);
        }

        [Fact]
        public async Task StreamStakeAsync_TranslatesOpenFailure_IntoLlmTransportException()
        {
            var streaming = FakeStreamingTransport.ThatThrowsOnOpen(
                new InvalidOperationException("provider 503"));
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt)) { }
            });

            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public async Task StreamStakeAsync_PassesThroughExistingLlmTransportException()
        {
            var streaming = FakeStreamingTransport.ThatThrowsAfter(
                new[] { "first ", "second " }, afterIndex: 1,
                new LlmTransportException("provider returned 429"));
            var gen = new LlmStakeGenerator(new StubLlmTransport(), streaming);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt)) { }
            });

            Assert.Equal("provider returned 429", ex.Message);
            Assert.Null(ex.InnerException);
        }

        // ── misuse ────────────────────────────────────────────────────────

        [Fact]
        public async Task StreamStakeAsync_WithoutStreamingTransport_ThrowsInvalidOperation()
        {
            var gen = new LlmStakeGenerator(new StubLlmTransport());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", Prompt)) { }
            });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task StreamStakeAsync_BlankCharacterName_ThrowsArgumentException(string? name)
        {
            var gen = new LlmStakeGenerator(
                new StubLlmTransport(), new FakeStreamingTransport(new[] { "x" }));

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync(name!, Prompt)) { }
            });
        }

        [Fact]
        public async Task StreamStakeAsync_NullPrompt_ThrowsArgumentNullException()
        {
            var gen = new LlmStakeGenerator(
                new StubLlmTransport(), new FakeStreamingTransport(new[] { "x" }));

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await foreach (var _ in gen.StreamStakeAsync("Alice", null!)) { }
            });
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Issue #1115 regression coverage: the Anthropic streaming transport
    /// previously yielded only <c>content_block_delta</c> text frames and
    /// silently discarded the <c>message_start</c> / <c>message_delta</c>
    /// frames that carry token usage. As a result every streaming exchange
    /// recorded all-zero usage (and a null cost). These tests pin that the
    /// transport now captures usage from those frames and surfaces it via
    /// <see cref="ITokenUsageProvider"/> — mirroring the non-streaming
    /// <c>AnthropicLlmAdapter</c> path (see <c>Issue534_DebugFlagTests</c>
    /// for the SSE/usage JSON shape this reuses).
    /// </summary>
    public partial class AnthropicStreamingTransportTests
    {
        // SSE frame factories reusing the JSON shape Anthropic actually emits
        // (cf. the message_start / message_delta examples in the transport's
        // own docstring and the usage block in Issue534_DebugFlagTests).
        private static string MessageStartFrame(
            int inputTokens, int cacheCreationInputTokens, int cacheReadInputTokens, int outputTokens = 1)
        {
            return "event: message_start\n" +
                   "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_01\",\"role\":\"assistant\"," +
                   "\"content\":[],\"usage\":{" +
                   "\"input_tokens\":" + inputTokens + "," +
                   "\"cache_creation_input_tokens\":" + cacheCreationInputTokens + "," +
                   "\"cache_read_input_tokens\":" + cacheReadInputTokens + "," +
                   "\"output_tokens\":" + outputTokens + "}}}\n\n";
        }

        private static string MessageDeltaUsageFrame(int outputTokens)
        {
            return "event: message_delta\n" +
                   "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null}," +
                   "\"usage\":{\"output_tokens\":" + outputTokens + "}}\n\n";
        }

        [Fact]
        public async Task SendStreamAsync_CapturesUsage_FromMessageStartAndMessageDelta()
        {
            // What: a stream that carries usage in message_start (prompt + cache)
            // and message_delta (output) must produce NON-ZERO usage on the
            // ITokenUsageProvider path. Mutation: reverting the fix (ignoring
            // those frames) yields all-zero usage and fails every assert below.
            var frames = SsePayload(
                MessageStartFrame(inputTokens: 25, cacheCreationInputTokens: 20, cacheReadInputTokens: 30),
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n",
                TextDeltaFrame("Hello"),
                TextDeltaFrame(", world!"),
                "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n",
                MessageDeltaUsageFrame(outputTokens: 15),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            // Consume the whole stream — usage is committed once the stream ends.
            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("sys", "user"))
                fragments.Add(f);

            // Text path is unchanged.
            Assert.Equal("Hello, world!", string.Concat(fragments));

            // Usage path now carries real numbers (was all-zero pre-#1115).
            var usage = transport.GetSessionUsage();
            Assert.NotNull(usage);
            Assert.Equal(25, usage.InputTokens);
            Assert.Equal(15, usage.OutputTokens);
            Assert.Equal(20, usage.CacheCreationInputTokens);
            Assert.Equal(30, usage.CacheReadInputTokens);
            Assert.Equal(1, usage.CallCount);

            // TotalBilledInput = input + cache_creation (excludes cache_read).
            Assert.Equal(45, usage.TotalBilledInput);

            // The exchange is not zero — this is the crux of #1115.
            Assert.True(usage.InputTokens > 0 && usage.OutputTokens > 0,
                "Streaming usage must be non-zero so token_usages rows and cost_usd are populated.");
        }

        [Fact]
        public async Task SendStreamAsync_AsTokenUsageProvider_ExposesUsage()
        {
            // What: the transport implements ITokenUsageProvider so the same
            // downstream telemetry that reads the non-streaming adapter can
            // read the streaming transport. Mutation: dropping the interface
            // (or returning zero) fails the cast/assert.
            var frames = SsePayload(
                MessageStartFrame(inputTokens: 100, cacheCreationInputTokens: 0, cacheReadInputTokens: 50),
                TextDeltaFrame("hi"),
                MessageDeltaUsageFrame(outputTokens: 42),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            ITokenUsageProvider provider = transport;

            await foreach (var _ in transport.SendStreamAsync("sys", "user")) { }

            var usage = provider.GetSessionUsage();
            Assert.Equal(100, usage.InputTokens);
            Assert.Equal(42, usage.OutputTokens);
            Assert.Equal(0, usage.CacheCreationInputTokens);
            Assert.Equal(50, usage.CacheReadInputTokens);
            Assert.Equal(1, usage.CallCount);
        }

        [Fact]
        public async Task SendStreamAsync_MultipleStreams_AccumulateUsage()
        {
            // What: usage from successive streams accumulates across calls,
            // matching the per-call accumulation of the non-streaming adapter.
            var firstFrames = SsePayload(
                MessageStartFrame(inputTokens: 10, cacheCreationInputTokens: 5, cacheReadInputTokens: 0),
                TextDeltaFrame("a"),
                MessageDeltaUsageFrame(outputTokens: 3),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            ).ToArray();
            var secondFrames = SsePayload(
                MessageStartFrame(inputTokens: 20, cacheCreationInputTokens: 0, cacheReadInputTokens: 7),
                TextDeltaFrame("b"),
                MessageDeltaUsageFrame(outputTokens: 4),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            ).ToArray();

            var handler = new TwoResponseSseHandler(firstFrames, secondFrames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            await foreach (var _ in transport.SendStreamAsync("sys", "user1")) { }
            await foreach (var _ in transport.SendStreamAsync("sys", "user2")) { }

            var usage = transport.GetSessionUsage();
            Assert.Equal(30, usage.InputTokens);   // 10 + 20
            Assert.Equal(7, usage.OutputTokens);   // 3 + 4
            Assert.Equal(5, usage.CacheCreationInputTokens);
            Assert.Equal(7, usage.CacheReadInputTokens);
            Assert.Equal(2, usage.CallCount);
        }

        [Fact]
        public async Task SendStreamAsync_NoUsageFrames_LeavesUsageZeroWithNoCall()
        {
            // What: a degenerate stream with no usage frames records no call
            // (so we never write an all-zero telemetry row). Mutation: always
            // committing a row would make CallCount == 1 here.
            var frames = SsePayload(
                TextDeltaFrame("orphan"),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            await foreach (var _ in transport.SendStreamAsync("sys", "user")) { }

            var usage = transport.GetSessionUsage();
            Assert.Equal(0, usage.InputTokens);
            Assert.Equal(0, usage.OutputTokens);
            Assert.Equal(0, usage.CallCount);
        }

        /// <summary>
        /// Serves a different canned SSE body on each successive request, so a
        /// single transport instance can drive two streams (for the
        /// accumulation test).
        /// </summary>
        private sealed class TwoResponseSseHandler : HttpMessageHandler
        {
            private readonly byte[][] _first;
            private readonly byte[][] _second;
            private int _calls;

            public TwoResponseSseHandler(byte[][] first, byte[][] second)
            {
                _first = first;
                _second = second;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                var chunks = _calls == 0 ? _first : _second;
                _calls++;
                var stream = new ChunkedStream(chunks, System.TimeSpan.Zero);
                var content = new StreamContent(stream);
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }
    }
}

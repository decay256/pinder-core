using System;
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
    public partial class AnthropicStreamingTransportTests
    {
        [Fact]
        public async Task SendStreamAsync_SystemBlocks_HaveCacheControlEphemeral()
        {
            var frames = SsePayload(
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            await foreach (var _ in transport.SendStreamAsync("sysprompt-stream", "usermsg-stream")) { }

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("\"cache_control\"", handler.LastRequestBody);
            Assert.Contains("\"type\":\"ephemeral\"", handler.LastRequestBody);
            Assert.Contains("sysprompt-stream", handler.LastRequestBody);
        }

        // ─── Happy path: tokens arrive in order, multi-chunk reassembly ──

        [Fact]
        public async Task SendStreamAsync_YieldsTextDeltasInOrder()
        {
            var frames = SsePayload(
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_01\",\"role\":\"assistant\",\"content\":[]}}\n\n",
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n",
                TextDeltaFrame("Hello"),
                TextDeltaFrame(", "),
                TextDeltaFrame("world!"),
                "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n",
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":7}}\n\n",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("sys", "user"))
                fragments.Add(f);

            Assert.Equal(new[] { "Hello", ", ", "world!" }, fragments);
            Assert.Equal("Hello, world!", string.Concat(fragments));
        }

        [Fact]
        public async Task SendStreamAsync_RequestBodyContainsStreamTrue()
        {
            var frames = SsePayload(
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            await foreach (var _ in transport.SendStreamAsync("sysprompt", "usermsg", 0.7, 256)) { }

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("\"stream\":true", handler.LastRequestBody);
            Assert.Contains("\"model\":\"" + TestModel + "\"", handler.LastRequestBody);
            Assert.Contains("\"max_tokens\":256", handler.LastRequestBody);
            Assert.Contains("\"temperature\":0.7", handler.LastRequestBody);
            Assert.Contains("sysprompt", handler.LastRequestBody);
            Assert.Contains("usermsg", handler.LastRequestBody);
        }

        [Fact]
        public async Task SendStreamAsync_FrameSplitAcrossChunks_StillReassembles()
        {
            // One full text_delta frame split mid-line into 3 byte chunks.
            var full = TextDeltaFrame("split_text");
            var bytes = U(full);
            var c1 = bytes.Take(10).ToArray();
            var c2 = bytes.Skip(10).Take(20).ToArray();
            var c3 = bytes.Skip(30).ToArray();
            var handler = new SseHandler(HttpStatusCode.OK, new[] { c1, c2, c3 });
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("s", "u"))
                fragments.Add(f);

            Assert.Single(fragments);
            Assert.Equal("split_text", fragments[0]);
        }

        // ─── Unknown event types are ignored ─────────────────────────────

        [Fact]
        public async Task SendStreamAsync_UnknownEventTypes_AreIgnored()
        {
            var frames = SsePayload(
                "event: ping\ndata: {\"type\":\"ping\"}\n\n",
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"x\",\"role\":\"assistant\",\"content\":[]}}\n\n",
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n",
                "event: weird_future_event\ndata: {\"type\":\"weird_future_event\",\"foo\":\"bar\"}\n\n",
                TextDeltaFrame("only-text"),
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("s", "u"))
                fragments.Add(f);

            Assert.Single(fragments);
            Assert.Equal("only-text", fragments[0]);
        }

        [Fact]
        public async Task SendStreamAsync_NonTextDelta_IsIgnored()
        {
            // content_block_delta with a non-text delta type (e.g.
            // input_json_delta for tool calls). Should not yield.
            var frames = SsePayload(
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"x\\\":1}\"}}\n\n",
                TextDeltaFrame("real text"),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("s", "u"))
                fragments.Add(f);

            Assert.Single(fragments);
            Assert.Equal("real text", fragments[0]);
        }

        // ─── HTTP open-time errors ────────────────────────────────────────

        [Fact]
        public async Task SendStreamAsync_Http401_Throws_LlmTransportException_WithStatus()
        {
            var handler = new FixedHandler(HttpStatusCode.Unauthorized, "{\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            Assert.Contains("401", ex.Message);
            Assert.DoesNotContain("invalid x-api-key", ex.Message);
            Assert.Contains("provider=anthropic-streaming", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_Http500_Throws_LlmTransportException_WithStatus()
        {
            var handler = new FixedHandler(HttpStatusCode.InternalServerError, "{\"error\":{\"type\":\"api_error\",\"message\":\"boom\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            Assert.Contains("500", ex.Message);
            Assert.DoesNotContain("boom", ex.Message);
            Assert.Contains("provider=anthropic-streaming", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_Http400_UsesSafeBodyDiagnostics()
        {
            var bigBody = new string('x', 4096);
            var handler = new FixedHandler(HttpStatusCode.BadRequest, bigBody);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            Assert.DoesNotContain(bigBody, ex.Message);
            Assert.Contains("body_length=4096", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }
    }
}

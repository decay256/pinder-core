using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public partial class OpenAiStreamingTransportTests
    {
        // ------------------------------------------------------------------
        // Happy paths
        // ------------------------------------------------------------------

        [Fact]
        public async Task SendStreamAsync_YieldsContentFragmentsInOrder()
        {
            // role-only first frame, then three content deltas, then [DONE]
            var sse = BuildSse(new[]
            {
                ChunkRole(),
                ChunkContent("Hello"),
                ChunkContent(", "),
                ChunkContent("world!"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "Hello", ", ", "world!" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_RoleOnlyFirstFrame_IsIgnored()
        {
            var sse = BuildSse(new[]
            {
                ChunkRole(),       // role-only delta
                ChunkContent("ok"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "ok" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_DoneSentinel_TerminatesStreamCleanly()
        {
            // Anything after [DONE] must be ignored.
            var sse =
                "data: " + ChunkContent("a") + "\n\n" +
                "data: [DONE]\n\n" +
                "data: " + ChunkContent("b") + "\n\n";

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "a" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_FinishReasonChunkWithEmptyDelta_DoesNotYield()
        {
            // Final non-content chunk (finish_reason set, delta empty).
            var sse = BuildSse(new[]
            {
                ChunkContent("hi"),
                "{\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}",
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "hi" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_ToolCallDeltas_AreIgnored()
        {
            // Some providers emit tool_calls deltas with no content; we ignore them.
            var sse = BuildSse(new[]
            {
                "{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\"}]}}]}",
                ChunkContent("after"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "after" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_EmptyContentDelta_NotYielded()
        {
            var sse = BuildSse(new[]
            {
                ChunkContent(""),     // empty content -> ignore
                ChunkContent("real"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "real" }, fragments);
        }

        // ------------------------------------------------------------------
        // Reasoning models (issue #178)
        // ------------------------------------------------------------------

        [Fact]
        public async Task SendStreamAsync_ReasoningOnlyStream_YieldsReasoningAndDetailSummaries()
        {
            // Reasoning model: every chunk has empty content but non-empty
            // delta.reasoning, plus a structured reasoning_details[].summary
            // on the final reasoning frame. No delta.content arrives at all.
            var sse = BuildSse(new[]
            {
                ChunkRole(),
                ChunkReasoning("I"),
                ChunkReasoning(" need"),
                ChunkReasoning(" to think."),
                ChunkReasoningDetailsSummaries(new[] { "Summary part A.", " Summary part B." }),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(
                new[] { "I", " need", " to think.", "Summary part A. Summary part B." },
                fragments);
        }

        [Fact]
        public async Task SendStreamAsync_MixedReasoningAndContentStream_YieldsContentFirstThenReasoning()
        {
            // Mixed stream:
            //   1. role-only
            //   2. reasoning-only frames (Anthropic-thinking style)
            //   3. mixed frame with BOTH content and reasoning
            //      (per yield order: content first, reasoning second)
            //   4. final content-only frames
            var sse = BuildSse(new[]
            {
                ChunkRole(),
                ChunkReasoning("thinking..."),
                ChunkContentAndReasoning("Hi", " still thinking"),
                ChunkContent(", "),
                ChunkContent("world!"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(
                new[] { "thinking...", "Hi", " still thinking", ", ", "world!" },
                fragments);
        }

        [Fact]
        public async Task SendStreamAsync_TolleratesSseCommentsAndOtherFields()
        {
            // ":" comments (Groq sometimes sends keepalive comments) and unknown
            // SSE fields (event:, id:) must be ignored.
            var sse =
                ": ping\n" +
                "event: message\n" +
                "id: 1\n" +
                "data: " + ChunkContent("x") + "\n\n" +
                ": keepalive\n\n" +
                "data: [DONE]\n\n";

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "x" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_PostsRequestBodyWithStreamTrue()
        {
            var sse = BuildSse(new[] { ChunkContent("ok"), "[DONE]" });
            string? capturedBody = null;
            string? capturedAuth = null;
            string? capturedUrl = null;

            var handler = new CannedSseHandler(sse, capture: req =>
            {
                capturedUrl = req.RequestUri!.ToString();
                if (req.Headers.TryGetValues("Authorization", out var auth))
                    capturedAuth = string.Join(",", auth);
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            });
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);

            _ = await CollectAsync(transport.SendStreamAsync("SYSPROMPT", "USERMSG", temperature: 0.42, maxTokens: 77));

            Assert.Equal("https://example.test/v1/chat/completions", capturedUrl);
            Assert.Equal("Bearer sk-test", capturedAuth);
            Assert.NotNull(capturedBody);
            Assert.Contains("\"stream\":true", capturedBody);
            Assert.Contains("\"model\":\"test-model\"", capturedBody);
            Assert.Contains("\"max_tokens\":77", capturedBody);
            Assert.Contains("\"temperature\":0.42", capturedBody);
            Assert.Contains("SYSPROMPT", capturedBody);
            Assert.Contains("USERMSG", capturedBody);
        }
    }
}

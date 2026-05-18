using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    /// <summary>
    /// Issue #947 — Anthropic prompt cache must hit on the OpenRouter Sonnet
    /// path. The fix wraps the (byte-stable) system prompt in a content-block
    /// array with <c>cache_control: { type: "ephemeral" }</c>. These tests
    /// assert request-side payload shape (which is what determines whether
    /// the upstream Anthropic cache layer actually registers a breakpoint).
    /// A full end-to-end <c>cache_read_input_tokens &gt; 0</c> assertion
    /// requires a real Anthropic API key and is out of scope for unit tests.
    /// </summary>
    public class Issue947_PromptCacheControlTests
    {
        // ---------------------------------------------------------------
        // OpenAiCacheControl — pure unit
        // ---------------------------------------------------------------

        [Fact]
        public void BuildSystemContent_WhenDisabled_ReturnsPlainString()
        {
            var content = OpenAiCacheControl.BuildSystemContent(
                "SYS", useCacheControl: false);

            Assert.Equal("SYS", content);
        }

        [Fact]
        public void BuildSystemContent_WhenEnabled_ReturnsContentBlockArrayWithEphemeralMarker()
        {
            var content = OpenAiCacheControl.BuildSystemContent(
                "SYS", useCacheControl: true);

            // Serialize through Newtonsoft to inspect the shape — the test is
            // shape-of-payload, not type-identity, since we serialize anyway.
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(content);
            var arr = JArray.Parse(json);

            Assert.Single(arr);
            Assert.Equal("text", (string?)arr[0]["type"]);
            Assert.Equal("SYS", (string?)arr[0]["text"]);
            Assert.Equal("ephemeral", (string?)arr[0]["cache_control"]?["type"]);
        }

        // ---------------------------------------------------------------
        // OpenAiTransport — request body carries cache_control by default
        // ---------------------------------------------------------------

        [Fact]
        public async Task OpenAiTransport_ByDefault_SendsSystemAsContentBlockArrayWithCacheControl()
        {
            string? capturedBody = null;
            var handler = new CaptureChatCompletionsHandler(
                responseBody: "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}",
                capture: req => capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            using var http = new HttpClient(handler);
            using var transport = new OpenAiTransport(
                "sk-test", "https://example.test", "test-model", http);

            _ = await transport.SendAsync(
                systemPrompt: "STABLE-PREFIX-SYS",
                userMessage: "TURN-1-USER",
                temperature: 0.5,
                maxTokens: 32);

            Assert.NotNull(capturedBody);
            var json = JObject.Parse(capturedBody!);
            var messages = (JArray)json["messages"]!;

            Assert.Equal(2, messages.Count);
            Assert.Equal("system", (string?)messages[0]["role"]);

            // The system content must be an array of content blocks (not a
            // plain string) so the cache_control marker can travel inline.
            var systemContent = messages[0]["content"];
            Assert.NotNull(systemContent);
            Assert.Equal(JTokenType.Array, systemContent!.Type);
            var blocks = (JArray)systemContent;
            Assert.Single(blocks);
            Assert.Equal("text", (string?)blocks[0]["type"]);
            Assert.Equal("STABLE-PREFIX-SYS", (string?)blocks[0]["text"]);
            Assert.Equal("ephemeral", (string?)blocks[0]["cache_control"]?["type"]);

            // The user message stays a plain string — only the prefix is
            // a cache breakpoint.
            Assert.Equal("user", (string?)messages[1]["role"]);
            Assert.Equal("TURN-1-USER", (string?)messages[1]["content"]);
        }

        [Fact]
        public async Task OpenAiTransport_WhenCacheControlDisabled_SendsSystemAsPlainString()
        {
            string? capturedBody = null;
            var handler = new CaptureChatCompletionsHandler(
                responseBody: "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}",
                capture: req => capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            using var http = new HttpClient(handler);
            using var transport = new OpenAiTransport(
                "sk-test", "https://example.test", "test-model", http,
                useAnthropicCacheControl: false);

            _ = await transport.SendAsync("SYS", "USR");

            Assert.NotNull(capturedBody);
            var json = JObject.Parse(capturedBody!);
            var messages = (JArray)json["messages"]!;
            Assert.Equal("system", (string?)messages[0]["role"]);
            Assert.Equal(JTokenType.String, messages[0]["content"]!.Type);
            Assert.Equal("SYS", (string?)messages[0]["content"]);
            Assert.DoesNotContain("cache_control", capturedBody);
        }

        // ---------------------------------------------------------------
        // OpenAiStreamingTransport — same shape on the streaming path
        // ---------------------------------------------------------------

        [Fact]
        public async Task OpenAiStreamingTransport_ByDefault_SendsSystemAsContentBlockArrayWithCacheControl()
        {
            string? capturedBody = null;
            var sse = "data: " + ChunkContent("ok") + "\n\ndata: [DONE]\n\n";
            var handler = new CaptureChatCompletionsHandler(
                responseBody: sse,
                contentType: "text/event-stream",
                capture: req => capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport(
                "sk-test", "https://example.test", "test-model", http);

            await foreach (var _ in transport.SendStreamAsync("SYS-STREAM", "USR-STREAM"))
            {
                // drain
            }

            Assert.NotNull(capturedBody);
            var json = JObject.Parse(capturedBody!);
            var messages = (JArray)json["messages"]!;
            Assert.Equal(2, messages.Count);

            var systemContent = messages[0]["content"];
            Assert.NotNull(systemContent);
            Assert.Equal(JTokenType.Array, systemContent!.Type);
            var blocks = (JArray)systemContent;
            Assert.Single(blocks);
            Assert.Equal("SYS-STREAM", (string?)blocks[0]["text"]);
            Assert.Equal("ephemeral", (string?)blocks[0]["cache_control"]?["type"]);
        }

        [Fact]
        public async Task OpenAiStreamingTransport_WhenCacheControlDisabled_OmitsMarker()
        {
            string? capturedBody = null;
            var sse = "data: " + ChunkContent("ok") + "\n\ndata: [DONE]\n\n";
            var handler = new CaptureChatCompletionsHandler(
                responseBody: sse,
                contentType: "text/event-stream",
                capture: req => capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult());
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport(
                "sk-test", "https://example.test", "test-model", http,
                useAnthropicCacheControl: false);

            await foreach (var _ in transport.SendStreamAsync("SYS", "USR"))
            {
                // drain
            }

            Assert.NotNull(capturedBody);
            Assert.DoesNotContain("cache_control", capturedBody);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static string ChunkContent(string content)
        {
            var escaped = content.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"id\":\"c1\",\"object\":\"chat.completion.chunk\","
                + "\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + escaped + "\"}}]}";
        }

        private sealed class CaptureChatCompletionsHandler : HttpMessageHandler
        {
            private readonly byte[] _body;
            private readonly string _contentType;
            private readonly Action<HttpRequestMessage>? _capture;

            public CaptureChatCompletionsHandler(
                string responseBody,
                string contentType = "application/json",
                Action<HttpRequestMessage>? capture = null)
            {
                _body = Encoding.UTF8.GetBytes(responseBody);
                _contentType = contentType;
                _capture = capture;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _capture?.Invoke(request);
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_body),
                };
                resp.Content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
                return Task.FromResult(resp);
            }
        }
    }

}

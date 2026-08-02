using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pi.AI;
using Pinder.LlmAdapters.Pi;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class PiProviderTransportFactoryTests
    {
        [Fact]
        public async Task Anthropic_UsesMessagesApiAndNormalizesModelId()
        {
            HttpTransportRequest? captured = null;
            FetchFunction fetch = (request, _) =>
            {
                captured = request;
                return Task.FromResult(JsonResponse(
                    "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"usage\":{\"input_tokens\":1}}}\n\n" +
                    "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"ok\"}}\n\n" +
                    "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n" +
                    "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":1}}\n\n" +
                    "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"));
            };
            var transport = PiProviderTransportFactory.Create(new PiProviderTransportOptions
            {
                Provider = "Anthropic",
                Model = "claude-opus-4.8",
                ApiKey = "test-key",
                Fetch = fetch,
                MaxRetries = 0,
            });

            string result = await transport.SendAsync("system", "hello");

            Assert.Equal("ok", result);
            Assert.NotNull(captured);
            Assert.Equal("https://api.anthropic.com/v1/messages", captured!.Url);
            string body = Encoding.UTF8.GetString(captured.Body!);
            Assert.Contains("claude-opus-4-8", body, StringComparison.Ordinal);
            Assert.Equal("test-key", captured.Headers["x-api-key"]);
        }

        [Fact]
        public async Task OpenRouter_UsesInjectedFetchAndOpenAiWireShape()
        {
            HttpTransportRequest? captured = null;
            FetchFunction fetch = (request, _) =>
            {
                captured = request;
                return Task.FromResult(JsonResponse(
                    "data: {\"id\":\"chatcmpl-1\",\"model\":\"anthropic/claude-sonnet-4.6\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ok\"},\"finish_reason\":null}]}\n\n" +
                    "data: {\"id\":\"chatcmpl-1\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}\n\n" +
                    "data: [DONE]\n\n"));
            };
            var transport = PiProviderTransportFactory.Create(new PiProviderTransportOptions
            {
                Provider = "openrouter",
                Model = "anthropic/claude-sonnet-4.6",
                ApiKey = "router-key",
                Fetch = fetch,
                MaxRetries = 0,
            });

            string result = await transport.SendAsync("system", "hello");

            Assert.Equal("ok", result);
            Assert.NotNull(captured);
            Assert.Equal("https://openrouter.ai/api/v1/chat/completions", captured!.Url);
            Assert.Equal("Bearer router-key", captured.Headers["Authorization"]);
        }

        [Fact]
        public void CustomProviderWithoutBaseUrl_FailsClosed()
        {
            Assert.Throws<ArgumentException>(() => PiProviderTransportFactory.Create(
                new PiProviderTransportOptions
                {
                    Provider = "custom",
                    Model = "model",
                    ApiKey = "key",
                    Fetch = (_, __) => throw new InvalidOperationException(),
                }));
        }

        private static HttpTransportResponse JsonResponse(string json)
            => new(
                200,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content-type"] = "application/json",
                },
                new MemoryResponseBody(Encoding.UTF8.GetBytes(json)));

        private sealed class MemoryResponseBody : IHttpResponseBody
        {
            private readonly byte[] _content;
            private int _offset;

            public MemoryResponseBody(byte[] content) => _content = content;

            public Task<int> ReadAsync(
                byte[] buffer,
                int bufferOffset,
                int count,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = Math.Min(count, _content.Length - _offset);
                if (read > 0)
                {
                    Buffer.BlockCopy(_content, _offset, buffer, bufferOffset, read);
                    _offset += read;
                }

                return Task.FromResult(read);
            }

            public void Dispose() { }
        }
    }
}

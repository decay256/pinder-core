using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
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
                ModelCapabilities = new PiProviderModelCapabilities
                {
                    MaxOutputTokens = 4096,
                },
            });

            string result = await transport.SendAsync("system", "hello");

            Assert.Equal("ok", result);
            Assert.NotNull(captured);
            Assert.Equal("https://api.anthropic.com/v1/messages", captured!.Url);
            string body = Encoding.UTF8.GetString(captured.Body!);
            Assert.Contains("claude-opus-4-8", body, StringComparison.Ordinal);
            Assert.DoesNotContain("\"temperature\"", body, StringComparison.Ordinal);
            Assert.Equal(4096, ReadInteger(body, "max_tokens"));
            Assert.Equal("test-key", captured.Headers["x-api-key"]);
        }

        [Fact]
        public async Task Anthropic_ExplicitMaxTokensDoesNotRequireModelPolicy()
        {
            HttpTransportRequest? captured = null;
            var transport = CreateAnthropicTransport(
                request => captured = request,
                new PiProviderModelCapabilities());

            await transport.SendAsync("system", "hello", maxTokens: 321);

            Assert.NotNull(captured);
            Assert.Equal(321, ReadInteger(Encoding.UTF8.GetString(captured!.Body!), "max_tokens"));
        }

        [Fact]
        public async Task Anthropic_NullMaxTokensWithoutModelPolicyFailsClearlyBeforeFetch()
        {
            int fetchCalls = 0;
            var transport = CreateAnthropicTransport(
                _ => fetchCalls++,
                new PiProviderModelCapabilities());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => transport.SendAsync("system", "hello", maxTokens: null));

            Assert.Contains("max output tokens", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("anthropic", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, fetchCalls);
        }

        [Fact]
        public async Task Anthropic_ModelThatSupportsTemperature_IncludesConfiguredTemperature()
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
                Provider = "anthropic",
                Model = "claude-sonnet-4.6",
                ApiKey = "test-key",
                Fetch = fetch,
                MaxRetries = 0,
                ModelCapabilities = new PiProviderModelCapabilities
                {
                    MaxOutputTokens = 4096,
                },
            });

            await transport.SendAsync("system", "hello", temperature: 0.42);

            Assert.NotNull(captured);
            string body = Encoding.UTF8.GetString(captured!.Body!);
            Assert.Contains("\"temperature\":0.42", body, StringComparison.Ordinal);
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
            Assert.DoesNotContain("\"max_tokens\"", Encoding.UTF8.GetString(captured.Body!), StringComparison.Ordinal);
        }

        [Fact]
        public async Task OpenRouter_ExplicitMaxTokensIsIncludedOnWire()
        {
            HttpTransportRequest? captured = null;
            var transport = CreateOpenRouterTransport(request => captured = request);

            await transport.SendAsync("system", "hello", maxTokens: 654);

            Assert.NotNull(captured);
            Assert.Equal(654, ReadInteger(Encoding.UTF8.GetString(captured!.Body!), "max_completion_tokens"));
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

        private static PiLlmTransport CreateAnthropicTransport(
            Action<HttpTransportRequest> capture,
            PiProviderModelCapabilities capabilities)
            => PiProviderTransportFactory.Create(new PiProviderTransportOptions
            {
                Provider = "anthropic",
                Model = "claude-sonnet-4.6",
                ApiKey = "test-key",
                Fetch = (request, _) =>
                {
                    capture(request);
                    return Task.FromResult(AnthropicResponse());
                },
                MaxRetries = 0,
                ModelCapabilities = capabilities,
            });

        private static PiLlmTransport CreateOpenRouterTransport(Action<HttpTransportRequest> capture)
            => PiProviderTransportFactory.Create(new PiProviderTransportOptions
            {
                Provider = "openrouter",
                Model = "anthropic/claude-sonnet-4.6",
                ApiKey = "router-key",
                Fetch = (request, _) =>
                {
                    capture(request);
                    return Task.FromResult(OpenAiResponse());
                },
                MaxRetries = 0,
            });

        private static HttpTransportResponse AnthropicResponse()
            => JsonResponse(
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"usage\":{\"input_tokens\":1}}}\n\n" +
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"ok\"}}\n\n" +
                "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n" +
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":1}}\n\n" +
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");

        private static HttpTransportResponse OpenAiResponse()
            => JsonResponse(
                "data: {\"id\":\"chatcmpl-1\",\"model\":\"anthropic/claude-sonnet-4.6\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ok\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"id\":\"chatcmpl-1\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}\n\n" +
                "data: [DONE]\n\n");

        private static int ReadInteger(string json, string propertyName)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty(propertyName).GetInt32();
        }

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

using System;
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
        public async Task SendStreamAsync_Http404_ClassifiesAsModelNotFound()
        {
            // Arrange
            var handler = new FixedHandler(
                HttpStatusCode.NotFound, 
                "{\"error\":{\"type\":\"not_found_error\",\"message\":\"The model does not exist\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            // Act
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            // Assert
            Assert.Equal(LlmFailureKind.ModelNotFound, ex.FailureKind);
            Assert.Contains("not_found_error", ex.Message);
            Assert.DoesNotContain("The model does not exist", ex.Message);
            Assert.DoesNotContain("The model does not exist", ex.ToString());
            Assert.Contains("provider=anthropic-streaming", ex.Message);
            Assert.Contains(TestModel, ex.Message);
            Assert.Contains("Operator hint", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_Http401_ClassifiesAsUnauthorized()
        {
            // Arrange
            var handler = new FixedHandler(
                HttpStatusCode.Unauthorized, 
                "{\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            // Act
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            // Assert
            Assert.Equal(LlmFailureKind.Unauthorized, ex.FailureKind);
            Assert.Contains("authentication_error", ex.Message);
            Assert.DoesNotContain("invalid x-api-key", ex.Message);
            Assert.DoesNotContain("invalid x-api-key", ex.ToString());
            Assert.Contains("Operator hint", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_Http429_ClassifiesAsRateLimited()
        {
            // Arrange
            var handler = new FixedHandler(
                (HttpStatusCode)429, 
                "{\"error\":{\"type\":\"rate_limit_error\",\"message\":\"throttled\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            // Act
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            // Assert
            Assert.Equal(LlmFailureKind.RateLimited, ex.FailureKind);
            Assert.Contains("rate_limit_error", ex.Message);
            Assert.DoesNotContain("throttled", ex.Message);
            Assert.DoesNotContain("throttled", ex.ToString());
            Assert.Contains("Operator hint", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_NetworkException_BeforeHeaders_ClassifiesAsNetwork()
        {
            // Arrange
            var handler = new ExceptionThrowingHandler(new HttpRequestException("Connection refused"));
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            // Act
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            // Assert
            Assert.Equal(LlmFailureKind.Network, ex.FailureKind);
            Assert.Contains("request failed before headers", ex.Message);
            Assert.NotNull(ex.InnerException);
            Assert.Equal("Connection refused", ex.InnerException.Message);
        }

        private sealed class ExceptionThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _exception;
            public ExceptionThrowingHandler(Exception exception) => _exception = exception;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                throw _exception;
            }
        }
    }
}

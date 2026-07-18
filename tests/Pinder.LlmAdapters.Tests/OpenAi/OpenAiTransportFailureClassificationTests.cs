using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public sealed class OpenAiTransportFailureClassificationTests
    {
        [Theory]
        [InlineData(401, LlmFailureKind.Unauthorized)]
        [InlineData(404, LlmFailureKind.ModelNotFound)]
        public async Task SendAsync_HttpStatusFailure_ThrowsTypedTransportException(
            int statusCode,
            LlmFailureKind expectedKind)
        {
            using var http = new HttpClient(new StatusHandler((HttpStatusCode)statusCode));
            using var transport = new OpenAiTransport(
                "sk-test",
                "https://example.test",
                "missing-or-unauthorized-model",
                http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(
                () => transport.SendAsync("sys", "user"));

            Assert.Equal(expectedKind, ex.FailureKind);
        }

        private sealed class StatusHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;

            public StatusHandler(HttpStatusCode statusCode)
            {
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(
                        "{\"error\":{\"message\":\"provider rejected request\"}}",
                        Encoding.UTF8,
                        "application/json")
                });
            }
        }
    }
}

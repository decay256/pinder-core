using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public class OpenAiClientSafeDiagnosticsTests
    {
        [Fact]
        public async Task SendChatCompletionAsync_HttpError_ExcludesRawProviderBody()
        {
            const string rawBody = "{\"error\":{\"message\":\"SECRET_PROVIDER_BODY_DO_NOT_LOG\",\"code\":\"sensitive-code\"}}";
            var handler = new SingleResponseHandler(HttpStatusCode.BadRequest, rawBody);
            using var http = new HttpClient(handler);
            using var client = new OpenAiClient("sk-test", "https://example.test", http);

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.SendChatCompletionAsync("{\"model\":\"unsafe-model\",\"messages\":[]}"));

            Assert.DoesNotContain("SECRET_PROVIDER_BODY_DO_NOT_LOG", ex.Message);
            Assert.DoesNotContain("SECRET_PROVIDER_BODY_DO_NOT_LOG", ex.ToString());
            Assert.DoesNotContain("sensitive-code", ex.Message);
            Assert.Contains("provider=openai-compatible", ex.Message);
            Assert.Contains("status=400", ex.Message);
            Assert.Contains("model=unsafe-model", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendChatCompletionAsync_MalformedSuccessBody_ExcludesRawProviderBody()
        {
            const string rawBody = "SECRET_MALFORMED_PROVIDER_BODY_DO_NOT_LOG {";
            var handler = new SingleResponseHandler(HttpStatusCode.OK, rawBody);
            using var http = new HttpClient(handler);
            using var client = new OpenAiClient("sk-test", "https://example.test", http);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SendChatCompletionAsync("{\"model\":\"unsafe-model\",\"messages\":[]}"));

            Assert.DoesNotContain("SECRET_MALFORMED_PROVIDER_BODY_DO_NOT_LOG", ex.Message);
            Assert.DoesNotContain("SECRET_MALFORMED_PROVIDER_BODY_DO_NOT_LOG", ex.ToString());
            Assert.Contains("provider=openai-compatible", ex.Message);
            Assert.Contains("status=200", ex.Message);
            Assert.Contains("model=unsafe-model", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
            Assert.IsType<JsonReaderException>(ex.InnerException);
        }

        private sealed class SingleResponseHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _body;

            public SingleResponseHandler(HttpStatusCode statusCode, string body)
            {
                _statusCode = statusCode;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}

using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class AnthropicApiExceptionTests
    {
        [Fact]
        public void Constructor_SetsPropertiesAndMessage()
        {
            var body = "{\"error\":{\"type\":\"rate_limit_error\"}}";
            var ex = new AnthropicApiException(429, body);

            Assert.Equal(429, ex.StatusCode);
            Assert.Equal(body, ex.RawResponseBody);
            Assert.DoesNotContain("rate_limit_error", ex.ResponseBody);
            Assert.DoesNotContain("rate_limit_error", ex.Message);
            Assert.DoesNotContain("rate_limit_error", ex.ToString());
            Assert.Contains("provider=anthropic", ex.Message);
            Assert.Contains("status=429", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
        }

        [Fact]
        public void Constructor_HandlesEmptyResponseBody()
        {
            var ex = new AnthropicApiException(500, "");

            Assert.Equal(500, ex.StatusCode);
            Assert.Equal("", ex.ResponseBody);
            Assert.Equal("", ex.RawResponseBody);
            Assert.Contains("status=500", ex.Message);
            Assert.Contains("body_length=0", ex.Message);
        }

        [Fact]
        public void IsException()
        {
            var ex = new AnthropicApiException(400, "bad request");
            Assert.IsAssignableFrom<System.Exception>(ex);
        }
    }
}

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
            Assert.Equal(body, ex.ResponseBody);
            Assert.Equal($"Anthropic API error 429: {body}", ex.Message);
        }

        [Fact]
        public void Constructor_HandlesEmptyResponseBody()
        {
            var ex = new AnthropicApiException(500, "");

            Assert.Equal(500, ex.StatusCode);
            Assert.Equal("", ex.ResponseBody);
            Assert.Equal("Anthropic API error 500: ", ex.Message);
        }

        [Fact]
        public void IsException()
        {
            var ex = new AnthropicApiException(400, "bad request");
            Assert.IsAssignableFrom<System.Exception>(ex);
        }
    }
}

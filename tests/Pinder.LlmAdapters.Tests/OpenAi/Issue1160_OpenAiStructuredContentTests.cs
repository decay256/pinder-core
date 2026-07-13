using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public class Issue1160_OpenAiStructuredContentTests
    {
        [Fact]
        public void ExtractAssistantContentOnly_IgnoresReasoningOnlyPayload()
        {
            var json = JObject.Parse(@"{
  ""choices"": [
    {
      ""message"": {
        ""content"": """",
        ""reasoning"": ""I should produce two options but have not emitted JSON.""
      }
    }
  ]
}");

            string content = OpenAiClient.ExtractAssistantContentOnly(json);

            Assert.Equal(string.Empty, content);
        }

        [Theory]
        [InlineData(@"{ ""id"": ""chatcmpl-test"" }", "missing_choices")]
        [InlineData(@"{ ""choices"": [] }", "empty_choices")]
        [InlineData(@"{ ""choices"": [{ ""finish_reason"": ""stop"" }] }", "missing_message")]
        [InlineData(@"{ ""choices"": [{ ""message"": ""not an object"" }] }", "invalid_message")]
        public void ExtractAssistantContentOnly_MalformedAssistantShape_ThrowsProviderResponseException(
            string body,
            string expectedShapeError)
        {
            var json = JObject.Parse(body);

            var ex = Assert.Throws<OpenAiProviderResponseException>(
                () => OpenAiClient.ExtractAssistantContentOnly(json));

            Assert.Equal(expectedShapeError, ex.ShapeError);
            Assert.Contains("provider response", ex.Message, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}

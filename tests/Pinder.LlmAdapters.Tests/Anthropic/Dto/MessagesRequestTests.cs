using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic.Dto
{
    public class MessagesRequestTests
    {
        [Fact]
        public void Defaults_SerializeCorrectly()
        {
            var request = new MessagesRequest();
            var json = JObject.Parse(JsonConvert.SerializeObject(request));

            Assert.Equal("", json["model"]!.Value<string>());
            Assert.Equal(1024, json["max_tokens"]!.Value<int>());
            Assert.Equal(0.9, json["temperature"]!.Value<double>());
            Assert.Empty(json["system"]!);
            Assert.Empty(json["messages"]!);
        }

        [Fact]
        public void FullRequest_SerializesToExpectedJson()
        {
            var request = new MessagesRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 1024,
                Temperature = 0.9,
                System = new[]
                {
                    new ContentBlock
                    {
                        Type = "text",
                        Text = "You are a character.",
                        CacheControl = new CacheControl { Type = "ephemeral" }
                    }
                },
                Messages = new[]
                {
                    new Message { Role = "user", Content = "Generate 4 dialogue options." }
                }
            };

            var json = JObject.Parse(JsonConvert.SerializeObject(request));

            Assert.Equal("claude-sonnet-4-20250514", json["model"]!.Value<string>());
            var system = (JArray)json["system"]!;
            Assert.Single(system);
            Assert.Equal("You are a character.", system[0]["text"]!.Value<string>());
            Assert.Equal("ephemeral", system[0]["cache_control"]!["type"]!.Value<string>());

            var messages = (JArray)json["messages"]!;
            Assert.Single(messages);
            Assert.Equal("user", messages[0]["role"]!.Value<string>());
        }

        [Fact]
        public void SystemAndMessages_DefaultToEmptyArray_NotNull()
        {
            var request = new MessagesRequest();
            Assert.NotNull(request.System);
            Assert.Empty(request.System);
            Assert.NotNull(request.Messages);
            Assert.Empty(request.Messages);
        }
    }
}

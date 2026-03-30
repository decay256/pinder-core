using Newtonsoft.Json;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic.Dto
{
    public class MessagesResponseTests
    {
        [Fact]
        public void GetText_ReturnsFirstContentBlock()
        {
            var json = @"{
                ""content"": [
                    { ""type"": ""text"", ""text"": ""Here are your options:\n1. ..."" }
                ],
                ""usage"": {
                    ""input_tokens"": 1500,
                    ""output_tokens"": 350,
                    ""cache_creation_input_tokens"": 1200,
                    ""cache_read_input_tokens"": 0
                }
            }";

            var response = JsonConvert.DeserializeObject<MessagesResponse>(json)!;

            Assert.Equal("Here are your options:\n1. ...", response.GetText());
            Assert.NotNull(response.Usage);
            Assert.Equal(1500, response.Usage!.InputTokens);
            Assert.Equal(350, response.Usage.OutputTokens);
            Assert.Equal(1200, response.Usage.CacheCreationInputTokens);
            Assert.Equal(0, response.Usage.CacheReadInputTokens);
        }

        [Fact]
        public void GetText_ReturnsEmptyString_WhenContentEmpty()
        {
            var json = @"{ ""content"": [], ""usage"": null }";
            var response = JsonConvert.DeserializeObject<MessagesResponse>(json)!;

            Assert.Equal("", response.GetText());
            Assert.Null(response.Usage);
        }

        [Fact]
        public void GetText_ReturnsFirstOnly_WhenMultipleBlocks()
        {
            var response = new MessagesResponse
            {
                Content = new[]
                {
                    new ResponseContent { Type = "text", Text = "First" },
                    new ResponseContent { Type = "text", Text = "Second" }
                }
            };

            Assert.Equal("First", response.GetText());
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var response = new MessagesResponse();
            Assert.NotNull(response.Content);
            Assert.Empty(response.Content);
            Assert.Null(response.Usage);
            Assert.Equal("", response.GetText());
        }
    }
}

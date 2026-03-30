using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic.Dto
{
    public class ContentBlockTests
    {
        [Fact]
        public void CacheControl_OmittedFromJson_WhenNull()
        {
            var block = new ContentBlock { Type = "text", Text = "Hello" };
            var json = JObject.Parse(JsonConvert.SerializeObject(block));

            Assert.Equal("text", json["type"]!.Value<string>());
            Assert.Equal("Hello", json["text"]!.Value<string>());
            Assert.Null(json["cache_control"]);
        }

        [Fact]
        public void CacheControl_IncludedInJson_WhenSet()
        {
            var block = new ContentBlock
            {
                Type = "text",
                Text = "Cached block",
                CacheControl = new CacheControl { Type = "ephemeral" }
            };
            var json = JObject.Parse(JsonConvert.SerializeObject(block));

            Assert.NotNull(json["cache_control"]);
            Assert.Equal("ephemeral", json["cache_control"]!["type"]!.Value<string>());
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var block = new ContentBlock();
            Assert.Equal("text", block.Type);
            Assert.Equal("", block.Text);
            Assert.Null(block.CacheControl);

            var cache = new CacheControl();
            Assert.Equal("ephemeral", cache.Type);

            var msg = new Message();
            Assert.Equal("", msg.Role);
            Assert.Equal("", msg.Content);
        }
    }
}

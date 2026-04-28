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
        public void GetText_ConcatenatesAllTextBlocks_WhenMultipleBlocks()
        {
            // Issue #320: previously this returned only the first block. Anthropic
            // can split a single answer across multiple type:"text" blocks (and
            // also interleaves type:"thinking" blocks for extended-thinking
            // models), so GetText() must concatenate every text block in order.
            var response = new MessagesResponse
            {
                Content = new[]
                {
                    new ResponseContent { Type = "text", Text = "First" },
                    new ResponseContent { Type = "text", Text = "Second" }
                }
            };

            Assert.Equal("FirstSecond", response.GetText());
        }

        [Fact]
        public void GetText_SkipsThinkingBlocks_AndReturnsTrailingText()
        {
            // Issue #320: Anthropic extended-thinking responses lead with a
            // type:"thinking" block followed by the real type:"text" answer.
            // The old GetText() returned Content[0].Text — i.e. the empty
            // string — and downstream parsers saw "empty output".
            var json = @"{
                ""content"": [
                    { ""type"": ""thinking"", ""thinking"": ""...internal reasoning..."" },
                    { ""type"": ""text"", ""text"": ""The real answer."" }
                ]
            }";
            var response = JsonConvert.DeserializeObject<MessagesResponse>(json)!;

            Assert.Equal("The real answer.", response.GetText());
        }

        [Fact]
        public void GetText_SkipsRedactedThinkingBlocks()
        {
            // redacted_thinking is the encrypted form Anthropic emits when the
            // platform redacts internal reasoning. It must be skipped just like
            // plain thinking blocks.
            var response = new MessagesResponse
            {
                Content = new[]
                {
                    new ResponseContent { Type = "redacted_thinking", Text = "" },
                    new ResponseContent { Type = "thinking", Text = "" },
                    new ResponseContent { Type = "text", Text = "Visible answer." }
                }
            };

            Assert.Equal("Visible answer.", response.GetText());
        }

        [Fact]
        public void GetText_ReturnsEmpty_WhenOnlyThinkingBlocks()
        {
            // Defensive: if a provider returns a thinking-only payload (no text
            // block), GetText() must return empty string — not throw, not return
            // the thinking content. Callers detect empty and route to the
            // setup_error path rather than feeding internal reasoning to users.
            var response = new MessagesResponse
            {
                Content = new[]
                {
                    new ResponseContent { Type = "thinking", Text = "" }
                }
            };

            Assert.Equal("", response.GetText());
        }

        [Fact]
        public void GetText_IgnoresToolUseBlocks_ConcatenatesText()
        {
            // tool_use is surfaced via GetToolInput(). When a response contains
            // both a text block and a tool_use block, GetText() must return only
            // the text — the JObject input is for the structured-tool path.
            var response = new MessagesResponse
            {
                Content = new[]
                {
                    new ResponseContent { Type = "thinking", Text = "" },
                    new ResponseContent { Type = "text", Text = "Prelude. " },
                    new ResponseContent { Type = "tool_use", Text = "", Name = "do_thing" },
                    new ResponseContent { Type = "text", Text = "Postlude." }
                }
            };

            Assert.Equal("Prelude. Postlude.", response.GetText());
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

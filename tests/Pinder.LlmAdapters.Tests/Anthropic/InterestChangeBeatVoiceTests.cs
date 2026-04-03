using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Tests for issue #352: InterestChangeBeat should include opponent system prompt
    /// so the LLM generates beats in the opponent's character voice.
    /// </summary>
    public class InterestChangeBeatVoiceTests
    {
        private static AnthropicOptions DefaultOptions() => new AnthropicOptions
        {
            ApiKey = "test-key",
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 1024,
        };

        private static string MakeApiResponse(string text) =>
            JsonConvert.SerializeObject(new
            {
                content = new[] { new { type = "text", text } },
                usage = new { input_tokens = 10, output_tokens = 5 }
            });

        [Fact]
        public async Task GetInterestChangeBeatAsync_with_opponent_prompt_includes_system_blocks()
        {
            var handler = new VoiceTestHandler
            {
                ResponseBody = MakeApiResponse("Brick checks her planner and pencils you in.")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var context = new InterestChangeContext(
                "Brick",
                20,
                25,
                InterestState.DateSecured,
                opponentPrompt: "You are Brick, a Level 9 M&A professional who color-codes her planner.");

            var result = await adapter.GetInterestChangeBeatAsync(context);

            Assert.Equal("Brick checks her planner and pencils you in.", result);

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.NotNull(body);
            Assert.NotNull(body!.System);
            Assert.Single(body.System); // Opponent-only system block
            Assert.Contains("Brick", body.System[0].Text);
            Assert.Contains("M&A professional", body.System[0].Text);
            Assert.Equal("ephemeral", body.System[0].CacheControl?.Type);
        }

        [Fact]
        public async Task GetInterestChangeBeatAsync_without_opponent_prompt_has_no_system_blocks()
        {
            var handler = new VoiceTestHandler
            {
                ResponseBody = MakeApiResponse("They lean closer.")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            // No opponentPrompt (default null) — backward compatible
            var context = new InterestChangeContext("Velvet", 15, 17, InterestState.VeryIntoIt);

            var result = await adapter.GetInterestChangeBeatAsync(context);

            Assert.Equal("They lean closer.", result);

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.NotNull(body);
            Assert.Empty(body!.System); // No system blocks when no prompt
        }

        [Fact]
        public async Task GetInterestChangeBeatAsync_empty_opponent_prompt_has_no_system_blocks()
        {
            var handler = new VoiceTestHandler
            {
                ResponseBody = MakeApiResponse("Generic beat.")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var context = new InterestChangeContext("Test", 10, 16, InterestState.VeryIntoIt, opponentPrompt: "");

            var result = await adapter.GetInterestChangeBeatAsync(context);

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.NotNull(body);
            Assert.Empty(body!.System);
        }

        [Fact]
        public void InterestChangeContext_stores_opponent_prompt()
        {
            var ctx = new InterestChangeContext("Brick", 20, 25, InterestState.DateSecured,
                opponentPrompt: "You are Brick.");

            Assert.Equal("You are Brick.", ctx.OpponentPrompt);
        }

        [Fact]
        public void InterestChangeContext_opponent_prompt_defaults_to_null()
        {
            var ctx = new InterestChangeContext("Brick", 20, 25, InterestState.DateSecured);

            Assert.Null(ctx.OpponentPrompt);
        }

        /// <summary>Local fake handler to avoid internal visibility issues.</summary>
        private class VoiceTestHandler : HttpMessageHandler
        {
            public string? CapturedRequestBody { get; private set; }
            public string ResponseBody { get; set; } = "";

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Content != null)
                {
                    CapturedRequestBody = await request.Content.ReadAsStringAsync();
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ResponseBody)
                };
            }
        }
    }
}

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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Partial class holding test setup, mock HTTP handlers, and helper methods.
    /// File size strictly monitored to remain under 500 lines.
    /// </summary>
    public partial class AnthropicLlmAdapterTests
    {
        /// <summary>
        /// Fake HttpMessageHandler that captures the request and returns a configurable response.
        /// </summary>
        internal class FakeHttpHandler : HttpMessageHandler
        {
            public HttpRequestMessage? CapturedRequest { get; private set; }
            public string? CapturedRequestBody { get; private set; }
            public string ResponseBody { get; set; } = "";
            public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CapturedRequest = request;
                if (request.Content != null)
                {
                    CapturedRequestBody = await request.Content.ReadAsStringAsync();
                }
                return new HttpResponseMessage(StatusCode)
                {
                    Content = new StringContent(ResponseBody)
                };
            }
        }

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

        private static DialogueContext MakeDialogueContext() => new DialogueContext(
            playerPrompt: "You are Thundercock",
            opponentPrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey there") },
            opponentLastMessage: "Hey there",
            activeTraps: new string[0],
            currentInterest: 10,
            playerName: "Thundercock",
            opponentName: "Velvet",
            currentTurn: 1);

        private static DeliveryContext MakeDeliveryContext() => new DeliveryContext(
            playerPrompt: "You are Thundercock",
            opponentPrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey") },
            opponentLastMessage: "Hey",
            chosenOption: new DialogueOption(StatType.Charm, "Nice to meet you"),
            outcome: FailureTier.None,
            beatDcBy: 5,
            activeTraps: new string[0],
            playerName: "Thundercock",
            opponentName: "Velvet");

        private static OpponentContext MakeOpponentContext() => new OpponentContext(
            playerPrompt: "You are Thundercock",
            opponentPrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey") },
            opponentLastMessage: "Hey",
            activeTraps: new string[0],
            currentInterest: 12,
            playerDeliveredMessage: "Nice to meet you too!",
            interestBefore: 10,
            interestAfter: 12,
            responseDelayMinutes: 2.0,
            playerName: "Thundercock",
            opponentName: "Velvet");
    }
}

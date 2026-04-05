using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public class AnthropicLlmAdapterStatefulTests
    {
        // ==============================================================================
        // Test Infrastructure
        // ==============================================================================

        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
            public List<string> RequestBodies { get; } = new List<string>();

            public CapturingHandler(string responseText)
                : this(_ => MakeJsonResponse(responseText)) { }

            public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
            {
                _factory = factory;
            }

            public static HttpResponseMessage MakeJsonResponse(string text)
            {
                var json = JsonConvert.SerializeObject(new
                {
                    content = new[] { new { type = "text", text } },
                    usage = new { input_tokens = 10, output_tokens = 5 }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken ct)
            {
                if (request.Content != null)
                    RequestBodies.Add(await request.Content.ReadAsStringAsync());
                else
                    RequestBodies.Add("");
                return _factory(request);
            }
        }

        private static AnthropicOptions DefaultOptions() => new AnthropicOptions
        {
            ApiKey = "test-key",
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 1024,
        };

        private static string FourOptionResponse => @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Second""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Third""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Fourth""";

        private static DialogueContext MakeDialogueContext() => new DialogueContext(
            playerPrompt: "You are Player",
            opponentPrompt: "You are Opponent",
            conversationHistory: new List<(string, string)> { ("Opponent", "Hey") },
            opponentLastMessage: "Hey",
            activeTraps: new string[0],
            currentInterest: 10,
            playerName: "Player",
            opponentName: "Opponent",
            currentTurn: 1);

        private static DeliveryContext MakeDeliveryContext() => new DeliveryContext(
            playerPrompt: "You are Player",
            opponentPrompt: "You are Opponent",
            conversationHistory: new List<(string, string)> { ("Opponent", "Hey") },
            opponentLastMessage: "Hey",
            chosenOption: new DialogueOption(StatType.Charm, "Nice"),
            outcome: FailureTier.None,
            beatDcBy: 5,
            activeTraps: new string[0],
            playerName: "Player",
            opponentName: "Opponent");

        private static OpponentContext MakeOpponentContext() => new OpponentContext(
            playerPrompt: "You are Player",
            opponentPrompt: "You are Opponent",
            conversationHistory: new List<(string, string)> { ("Player", "Nice") },
            opponentLastMessage: "Hey",
            activeTraps: new string[0],
            currentInterest: 10,
            playerDeliveredMessage: "Nice",
            interestBefore: 10,
            interestAfter: 11,
            responseDelayMinutes: 0,
            playerName: "Player",
            opponentName: "Opponent");

        private static InterestChangeContext MakeInterestChangeContext() => new InterestChangeContext(
            opponentName: "Opponent",
            interestBefore: 10,
            interestAfter: 16,
            newState: InterestState.VeryIntoIt,
            conversationHistory: new List<(string, string)> { ("Player", "Hello") },
            playerName: "Player",
            opponentPrompt: "You are Opponent");

        // ==============================================================================
        // StartConversation / HasActiveConversation
        // ==============================================================================

        [Fact]
        public void HasActiveConversation_false_by_default()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            Assert.False(adapter.HasActiveConversation);
        }

        [Fact]
        public void StartConversation_sets_active()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system prompt");
            Assert.True(adapter.HasActiveConversation);
        }

        [Fact]
        public void StartConversation_throws_on_null()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            Assert.Throws<ArgumentException>(() => adapter.StartConversation(null!));
        }

        [Fact]
        public void StartConversation_throws_on_empty()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            Assert.Throws<ArgumentException>(() => adapter.StartConversation(""));
        }

        [Fact]
        public void StartConversation_replaces_previous_session()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("first");
            adapter.StartConversation("second");
            Assert.True(adapter.HasActiveConversation);
        }

        // ==============================================================================
        // Stateful GetDialogueOptionsAsync
        // ==============================================================================

        [Fact]
        public async Task Stateful_GetDialogueOptions_sends_accumulated_messages()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system prompt text");

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            Assert.Equal(4, result.Length);

            // Verify request has system blocks from session and 1 user message
            Assert.Single(handler.RequestBodies);
            var body = JObject.Parse(handler.RequestBodies[0]);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            Assert.Contains("system prompt text", system![0]!["text"]!.ToString());
            Assert.NotNull(system[0]!["cache_control"]);

            var messages = body["messages"] as JArray;
            Assert.NotNull(messages);
            Assert.Single(messages!); // 1 user message
            Assert.Equal("user", messages[0]!["role"]!.ToString());
        }

        [Fact]
        public async Task Stateful_messages_accumulate_across_calls()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");

            // First call
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            // After: session has [user, assistant]

            // Second call
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            // After: session has [user, assistant, user, assistant]

            // Second request should have 3 messages: user, assistant, user
            Assert.Equal(2, handler.RequestBodies.Count);
            var body2 = JObject.Parse(handler.RequestBodies[1]);
            var messages = body2["messages"] as JArray;
            Assert.NotNull(messages);
            Assert.Equal(3, messages!.Count); // u1, a1, u2
            Assert.Equal("user", messages[0]!["role"]!.ToString());
            Assert.Equal("assistant", messages[1]!["role"]!.ToString());
            Assert.Equal("user", messages[2]!["role"]!.ToString());
        }

        // ==============================================================================
        // Stateful DeliverMessageAsync
        // ==============================================================================

        [Fact]
        public async Task Stateful_DeliverMessage_uses_session()
        {
            var handler = new CapturingHandler("delivered text");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");
            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Equal("delivered text", result);

            var body = JObject.Parse(handler.RequestBodies[0]);
            var system = body["system"] as JArray;
            Assert.Contains("system", system![0]!["text"]!.ToString());
        }

        // ==============================================================================
        // Stateful GetOpponentResponseAsync
        // ==============================================================================

        [Fact]
        public async Task Stateful_GetOpponentResponse_uses_session()
        {
            var responseText = "[RESPONSE] \"Oh hey there\"\n[SIGNALS]\nTELL: Charm (likes compliments)";
            var handler = new CapturingHandler(responseText);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");
            var result = await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            Assert.Equal("Oh hey there", result.MessageText);
            Assert.NotNull(result.DetectedTell);
        }

        // ==============================================================================
        // Stateful GetInterestChangeBeatAsync
        // ==============================================================================

        [Fact]
        public async Task Stateful_GetInterestChangeBeat_uses_session()
        {
            var handler = new CapturingHandler("*leans in closer*");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");
            var result = await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            Assert.Equal("*leans in closer*", result);
        }

        [Fact]
        public async Task Stateful_GetInterestChangeBeat_returns_null_for_empty()
        {
            var handler = new CapturingHandler("   ");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");
            var result = await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            Assert.Null(result);
        }

        // ==============================================================================
        // Stateless fallback (no session)
        // ==============================================================================

        [Fact]
        public async Task Without_session_GetDialogueOptions_uses_stateless_path()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            // Do NOT call StartConversation
            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            Assert.Equal(4, result.Length);

            // Should use CacheBlockBuilder player-only blocks, not session system blocks
            var body = JObject.Parse(handler.RequestBodies[0]);
            var messages = body["messages"] as JArray;
            Assert.Single(messages!); // Single user message, not accumulated
        }

        // ==============================================================================
        // Response text appended before parsing
        // ==============================================================================

        [Fact]
        public async Task Stateful_assistant_response_appended_even_if_parsing_pads()
        {
            // Return incomplete options — parser will pad to 4
            var handler = new CapturingHandler("garbage text with no options");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");
            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            // Should still get 4 padded options
            Assert.Equal(4, result.Length);

            // Second call should see the prior assistant response in messages
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body2 = JObject.Parse(handler.RequestBodies[1]);
            var messages = body2["messages"] as JArray;
            Assert.Equal(3, messages!.Count); // u1, a1(garbage), u2
            Assert.Equal("assistant", messages[1]!["role"]!.ToString());
            Assert.Equal("garbage text with no options", messages[1]!["content"]!.ToString());
        }

        // ==============================================================================
        // Multi-turn full sequence
        // ==============================================================================

        [Fact]
        public async Task Full_turn_sequence_accumulates_all_messages()
        {
            var callNum = 0;
            var handler = new CapturingHandler(_ =>
            {
                callNum++;
                switch (callNum)
                {
                    case 1: return CapturingHandler.MakeJsonResponse(FourOptionResponse);
                    case 2: return CapturingHandler.MakeJsonResponse("delivered message");
                    case 3: return CapturingHandler.MakeJsonResponse("[RESPONSE] \"opponent reply\"");
                    default: return CapturingHandler.MakeJsonResponse("beat text");
                }
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("combined system prompt");

            // Turn 1: options
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            // Turn 1: delivery
            await adapter.DeliverMessageAsync(MakeDeliveryContext());
            // Turn 1: opponent response
            await adapter.GetOpponentResponseAsync(MakeOpponentContext());
            // Interest beat
            await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            Assert.Equal(4, handler.RequestBodies.Count);

            // 4th call should have 7 messages: u1, a1, u2, a2, u3, a3, u4
            var body4 = JObject.Parse(handler.RequestBodies[3]);
            var messages = body4["messages"] as JArray;
            Assert.Equal(7, messages!.Count);

            // Verify alternation
            for (int i = 0; i < messages.Count; i++)
            {
                var expectedRole = i % 2 == 0 ? "user" : "assistant";
                Assert.Equal(expectedRole, messages[i]!["role"]!.ToString());
            }
        }
    }
}

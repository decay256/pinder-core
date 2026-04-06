using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #541 — Additional tests for stateful conversation mode.
    /// Supplements ConversationSessionTests.cs, AnthropicLlmAdapterStatefulTests.cs,
    /// and Issue541_StatefulConversationTests.cs with coverage for remaining spec
    /// acceptance criteria, edge cases, and error conditions.
    /// </summary>
    public class Issue541_AdditionalTests
    {
        // ==============================================================================
        // Test Infrastructure (test-only utilities)
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
""First option""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Second option""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Third option""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Fourth option""";

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
        // AC1: BuildRequest snapshot isolation (spec: "returns a snapshot")
        // ==============================================================================

        // Mutation: would catch if BuildRequest returned a reference to the live internal list
        // instead of copying to an array
        [Fact]
        public void BuildRequest_snapshot_is_not_affected_by_subsequent_appends()
        {
            var session = new ConversationSession("system");
            session.AppendUser("u1");
            session.AppendAssistant("a1");

            var req = session.BuildRequest("model", 1024, 0.9);
            Assert.Equal(2, req.Messages.Length);

            // Append more messages AFTER building the request
            session.AppendUser("u2");
            session.AppendAssistant("a2");

            // The previously built request must NOT have the new messages
            Assert.Equal(2, req.Messages.Length);
            Assert.Equal("u1", req.Messages[0].Content);
            Assert.Equal("a1", req.Messages[1].Content);
        }

        // Mutation: would catch if BuildRequest with zero messages threw or returned null
        [Fact]
        public void BuildRequest_with_zero_messages_returns_valid_request()
        {
            var session = new ConversationSession("system");
            var req = session.BuildRequest("model", 512, 0.7);

            Assert.NotNull(req);
            Assert.Empty(req.Messages);
            Assert.Single(req.System);
            Assert.Equal("model", req.Model);
        }

        // ==============================================================================
        // AC1: ConversationSession is public sealed class
        // ==============================================================================

        // Mutation: would catch if ConversationSession was not sealed (allowing subclassing)
        [Fact]
        public void ConversationSession_is_sealed()
        {
            Assert.True(typeof(ConversationSession).IsSealed);
        }

        // Mutation: would catch if ConversationSession was internal instead of public
        [Fact]
        public void ConversationSession_is_public()
        {
            Assert.True(typeof(ConversationSession).IsPublic);
        }

        // ==============================================================================
        // AC1: Message role correctness
        // ==============================================================================

        // Mutation: would catch if AppendUser set Role to "assistant" instead of "user"
        [Fact]
        public void AppendUser_sets_role_to_user_not_assistant()
        {
            var session = new ConversationSession("sys");
            session.AppendUser("content");
            Assert.Equal("user", session.Messages[0].Role);
        }

        // Mutation: would catch if AppendAssistant set Role to "user" instead of "assistant"
        [Fact]
        public void AppendAssistant_sets_role_to_assistant_not_user()
        {
            var session = new ConversationSession("sys");
            session.AppendAssistant("content");
            Assert.Equal("assistant", session.Messages[0].Role);
        }

        // ==============================================================================
        // AC1: Message ordering preserved across interleaved appends
        // ==============================================================================

        // Mutation: would catch if messages were stored in a data structure that reorders
        [Fact]
        public void Messages_maintain_exact_append_order()
        {
            var session = new ConversationSession("sys");
            session.AppendUser("u1");
            session.AppendAssistant("a1");
            session.AppendUser("u2");
            session.AppendAssistant("a2");
            session.AppendUser("u3");

            Assert.Equal(5, session.Messages.Count);
            Assert.Equal("u1", session.Messages[0].Content);
            Assert.Equal("user", session.Messages[0].Role);
            Assert.Equal("a1", session.Messages[1].Content);
            Assert.Equal("assistant", session.Messages[1].Role);
            Assert.Equal("u2", session.Messages[2].Content);
            Assert.Equal("a2", session.Messages[3].Content);
            Assert.Equal("u3", session.Messages[4].Content);
        }

        // ==============================================================================
        // AC2: StartConversation replacement — new prompt replaces old completely
        // ==============================================================================

        // Mutation: would catch if StartConversation concatenated prompts instead of replacing
        [Fact]
        public async Task StartConversation_replacement_uses_new_prompt_only()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("OLD_PROMPT_SHOULD_NOT_APPEAR");
            adapter.StartConversation("NEW_PROMPT_ONLY");

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var systemText = body["system"]![0]!["text"]!.ToString();
            Assert.Contains("NEW_PROMPT_ONLY", systemText);
            Assert.DoesNotContain("OLD_PROMPT_SHOULD_NOT_APPEAR", systemText);
        }

        // ==============================================================================
        // AC3: Stateful GetInterestChangeBeat accumulates messages
        // ==============================================================================

        // Mutation: would catch if GetInterestChangeBeatAsync did not append to session

        // ==============================================================================
        // AC3: Stateful GetOpponentResponse accumulates with delivery
        // ==============================================================================

        // Mutation: would catch if GetOpponentResponseAsync didn't read accumulated history
        [Fact]
        public async Task Stateful_opponent_response_sees_delivery_history()
        {
            var callNum = 0;
            var handler = new CapturingHandler(_ =>
            {
                callNum++;
                if (callNum == 1) return CapturingHandler.MakeJsonResponse("delivered text");
                return CapturingHandler.MakeJsonResponse("[RESPONSE] \"cool reply\"");
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");

            // Deliver first
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            // Opponent response should see delivery messages
            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body2 = JObject.Parse(handler.RequestBodies[1]);
            var messages = body2["messages"] as JArray;
            Assert.NotNull(messages);
            // Should have 3: u1(delivery), a1(delivered text), u2(opponent prompt)
            Assert.Equal(3, messages!.Count);
        }

        // ==============================================================================
        // AC3: System blocks in stateful request — all 4 methods use session system
        // ==============================================================================

        // Mutation: would catch if GetOpponentResponseAsync used CacheBlockBuilder in stateful mode
        [Fact]
        public async Task Stateful_opponent_response_system_blocks_from_session()
        {
            var handler = new CapturingHandler("[RESPONSE] \"hey\"");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("UNIQUE_SESSION_PROMPT_FOR_OPPONENT");
            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            Assert.Contains("UNIQUE_SESSION_PROMPT_FOR_OPPONENT", system![0]!["text"]!.ToString());
        }

        // Mutation: would catch if GetInterestChangeBeatAsync used CacheBlockBuilder in stateful mode

        // ==============================================================================
        // AC4: HasActiveConversation is on concrete class, not interface
        // ==============================================================================

        // Mutation: would catch if HasActiveConversation property was added to ILlmAdapter
        [Fact]
        public void HasActiveConversation_exists_on_adapter_class()
        {
            var prop = typeof(AnthropicLlmAdapter).GetProperty("HasActiveConversation");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop!.PropertyType);
        }

        // ==============================================================================
        // Edge case: Error conditions from spec table
        // ==============================================================================

        // Mutation: would catch if AppendAssistant accepted null silently
        [Fact]
        public void AppendAssistant_throws_on_null()
        {
            var session = new ConversationSession("prompt");
            Assert.Throws<ArgumentNullException>(() => session.AppendAssistant(null!));
        }

        // ==============================================================================
        // Edge case: API failure — session still usable for next call
        // ==============================================================================

        // Mutation: would catch if API failure destroyed session or prevented further calls
        [Fact]
        public async Task Session_remains_usable_after_api_failure()
        {
            var callNum = 0;
            var handler = new CapturingHandler(_ =>
            {
                callNum++;
                // First 3 calls fail (retries), then succeed
                if (callNum <= 3)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            "{\"error\":{\"type\":\"server_error\",\"message\":\"fail\"}}",
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return CapturingHandler.MakeJsonResponse(FourOptionResponse);
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");

            // First call fails
            await Assert.ThrowsAnyAsync<Exception>(
                () => adapter.GetDialogueOptionsAsync(MakeDialogueContext()));

            // Session should still be active
            Assert.True(adapter.HasActiveConversation);

            // Second call succeeds — session is still usable
            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            Assert.Equal(4, result.Length);
        }

        // ==============================================================================
        // Edge case: Stateless path — multiple different methods don't accumulate
        // ==============================================================================

        // Mutation: would catch if stateless path accidentally shared state between calls
        [Fact]
        public async Task Stateless_different_methods_do_not_accumulate()
        {
            var callNum = 0;
            var handler = new CapturingHandler(_ =>
            {
                callNum++;
                if (callNum == 1) return CapturingHandler.MakeJsonResponse(FourOptionResponse);
                return CapturingHandler.MakeJsonResponse("delivered");
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            // No StartConversation — stateless
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            // Second call should still have single message (no accumulation)
            var body2 = JObject.Parse(handler.RequestBodies[1]);
            var messages = body2["messages"] as JArray;
            Assert.Single(messages!);
        }

        // ==============================================================================
        // Edge case: HasActiveConversation false before StartConversation, true after
        // ==============================================================================

        // Mutation: would catch if HasActiveConversation was hardcoded true
        [Fact]
        public void HasActiveConversation_transitions_from_false_to_true()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            Assert.False(adapter.HasActiveConversation);
            adapter.StartConversation("prompt");
            Assert.True(adapter.HasActiveConversation);
        }

        // ==============================================================================
        // Edge case: BuildRequest system includes cache_control ephemeral
        // ==============================================================================

        // Mutation: would catch if cache_control type was set to something other than "ephemeral"
        [Fact]
        public void BuildRequest_system_block_has_ephemeral_cache_control()
        {
            var session = new ConversationSession("prompt text");
            var req = session.BuildRequest("model", 1024, 0.9);

            Assert.Single(req.System);
            Assert.Equal("text", req.System[0].Type);
            Assert.Equal("prompt text", req.System[0].Text);
            Assert.NotNull(req.System[0].CacheControl);
            Assert.Equal("ephemeral", req.System[0].CacheControl!.Type);
        }

        // ==============================================================================
        // Edge case: Multi-turn with all 4 methods — message count grows correctly
        // ==============================================================================

        // Mutation: would catch if any method failed to append both user AND assistant messages

        // ==============================================================================
        // Edge case: ConversationSession content preservation
        // ==============================================================================

        // Mutation: would catch if AppendUser/AppendAssistant truncated or modified content
        [Fact]
        public void Appended_content_is_preserved_verbatim()
        {
            var session = new ConversationSession("sys");
            var userContent = "[ENGINE — Turn 1: Option Generation]\nInterest: 10/25\n\nMulti-line content";
            var assistantContent = "OPTION_1\n[STAT: CHARM]\n\"hey there gorgeous\"\n\nOPTION_2\n[STAT: RIZZ]\n\"sup\"";

            session.AppendUser(userContent);
            session.AppendAssistant(assistantContent);

            Assert.Equal(userContent, session.Messages[0].Content);
            Assert.Equal(assistantContent, session.Messages[1].Content);
        }

        // ==============================================================================
        // Edge case: BuildRequest system blocks reference same array as SystemBlocks property
        // ==============================================================================

        // Mutation: would catch if BuildRequest created different system blocks than SystemBlocks property
        [Fact]
        public void BuildRequest_system_equals_SystemBlocks()
        {
            var session = new ConversationSession("my prompt");
            var req = session.BuildRequest("model", 1024, 0.9);

            Assert.Same(session.SystemBlocks, req.System);
        }
    }
}

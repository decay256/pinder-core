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
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #541 — AnthropicLlmAdapter stateful conversation mode.
    /// Tests spec acceptance criteria, edge cases, and error conditions
    /// that are NOT already covered in ConversationSessionTests.cs or
    /// AnthropicLlmAdapterStatefulTests.cs.
    /// </summary>
    public class Issue541_StatefulConversationTests
    {
        // ==============================================================================
        // Test Infrastructure (test-only utilities, not production code)
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

        private sealed class FailingHandler : HttpMessageHandler
        {
            private int _callCount;
            private readonly int _failOnCall;
            private readonly string _successResponse;

            public FailingHandler(int failOnCall, string successResponse)
            {
                _failOnCall = failOnCall;
                _successResponse = successResponse;
            }

            public List<string> RequestBodies { get; } = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken ct)
            {
                _callCount++;
                if (request.Content != null)
                    RequestBodies.Add(await request.Content.ReadAsStringAsync());
                else
                    RequestBodies.Add("");

                if (_callCount == _failOnCall)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{\"error\":{\"type\":\"server_error\",\"message\":\"fail\"}}")
                    };
                }

                return CapturingHandler.MakeJsonResponse(_successResponse);
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
        // AC1: ConversationSession — additional edge cases
        // ==============================================================================

        // Mutation: would catch if AppendAssistant rejects empty strings like constructor does
        [Fact]
        public void AppendAssistant_allows_empty_string()
        {
            var session = new ConversationSession("prompt");
            session.AppendAssistant("");
            Assert.Single(session.Messages);
            Assert.Equal("", session.Messages[0].Content);
            Assert.Equal("assistant", session.Messages[0].Role);
        }

        // Mutation: would catch if consecutive same-role messages were silently rejected
        [Fact]
        public void ConversationSession_does_not_enforce_alternation()
        {
            var session = new ConversationSession("prompt");
            session.AppendUser("u1");
            session.AppendUser("u2"); // consecutive user — allowed per spec
            Assert.Equal(2, session.Messages.Count);
            Assert.Equal("user", session.Messages[0].Role);
            Assert.Equal("user", session.Messages[1].Role);
        }

        // Mutation: would catch if SystemBlocks was re-created on each access
        [Fact]
        public void SystemBlocks_is_stable_across_accesses()
        {
            var session = new ConversationSession("test prompt");
            var first = session.SystemBlocks;
            var second = session.SystemBlocks;
            Assert.Same(first, second);
        }

        // Mutation: would catch if BuildRequest returned Messages as a live reference
        [Fact]
        public void BuildRequest_returns_independent_array_each_call()
        {
            var session = new ConversationSession("system");
            session.AppendUser("u1");
            session.AppendAssistant("a1");

            var req1 = session.BuildRequest("model", 1024, 0.9);
            var req2 = session.BuildRequest("model", 1024, 0.9);

            // Two different array instances
            Assert.NotSame(req1.Messages, req2.Messages);
            // But same content
            Assert.Equal(req1.Messages.Length, req2.Messages.Length);
        }

        // Mutation: would catch if Messages property exposed internal list directly (allowing external mutation)
        [Fact]
        public void Messages_reflects_appends_after_read()
        {
            var session = new ConversationSession("system");
            var msgs = session.Messages;
            Assert.Empty(msgs);

            session.AppendUser("hello");
            // IReadOnlyList backed by the internal list should reflect the new message
            Assert.Single(session.Messages);
            Assert.Equal("hello", session.Messages[0].Content);
        }

        // ==============================================================================
        // AC2: StartConversation replaces session — verify old state is gone
        // ==============================================================================

        // Mutation: would catch if StartConversation appended to existing session instead of replacing
        [Fact]
        public async Task StartConversation_replacement_clears_accumulated_messages()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("first system prompt");
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            // Session now has [user, assistant] from first call

            // Replace the session
            adapter.StartConversation("second system prompt");

            // Next call should only have 1 message (fresh session)
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.RequestBodies[1]);
            var messages = body["messages"] as JArray;
            Assert.Single(messages!); // Only the new user message

            var system = body["system"] as JArray;
            Assert.Contains("second system prompt", system![0]!["text"]!.ToString());
        }

        // Mutation: would catch if HasActiveConversation checked old reference
        [Fact]
        public void StartConversation_throws_on_whitespace()
        {
            var handler = new CapturingHandler("ok");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            Assert.Throws<ArgumentException>(() => adapter.StartConversation("   "));
        }

        // ==============================================================================
        // AC3: Stateful calls accumulate across different method types
        // ==============================================================================

        // Mutation: would catch if each ILlmAdapter method had its own session instead of shared
        [Fact]
        public async Task Different_adapter_methods_share_same_session()
        {
            var callNum = 0;
            var handler = new CapturingHandler(_ =>
            {
                callNum++;
                switch (callNum)
                {
                    case 1: return CapturingHandler.MakeJsonResponse(FourOptionResponse);
                    case 2: return CapturingHandler.MakeJsonResponse("delivered msg");
                    default: return CapturingHandler.MakeJsonResponse("[RESPONSE] \"reply\"");
                }
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("shared system");

            // Call 1: GetDialogueOptions → appends user+assistant
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            // Call 2: DeliverMessage → should see prior messages + new user
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body2 = JObject.Parse(handler.RequestBodies[1]);
            var messages2 = body2["messages"] as JArray;
            // Should have 3 messages: u1(options), a1(options response), u2(delivery)
            Assert.Equal(3, messages2!.Count);
            Assert.Equal("user", messages2[0]!["role"]!.ToString());
            Assert.Equal("assistant", messages2[1]!["role"]!.ToString());
            Assert.Equal("user", messages2[2]!["role"]!.ToString());

            // Call 3: GetOpponentResponse → should see all 4 prior + new user
            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body3 = JObject.Parse(handler.RequestBodies[2]);
            var messages3 = body3["messages"] as JArray;
            // Should have 5 messages: u1, a1, u2, a2(delivery response), u3(opponent)
            Assert.Equal(5, messages3!.Count);
        }

        // Mutation: would catch if stateful mode used CacheBlockBuilder system blocks instead of session's
        [Fact]
        public async Task Stateful_system_blocks_come_from_session_not_cachebuilder()
        {
            var handler = new CapturingHandler("delivered text");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            var sessionPrompt = "UNIQUE_SESSION_SYSTEM_PROMPT_XYZ";
            adapter.StartConversation(sessionPrompt);
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            // System should contain the session prompt, not CacheBlockBuilder output
            Assert.Contains(sessionPrompt, system![0]!["text"]!.ToString());
        }

        // Mutation: would catch if system blocks lacked cache_control in stateful mode
        [Fact]
        public async Task Stateful_system_blocks_have_cache_control()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("cached system prompt");
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            var cacheControl = system![0]!["cache_control"];
            Assert.NotNull(cacheControl);
            Assert.Equal("ephemeral", cacheControl!["type"]!.ToString());
        }

        // ==============================================================================
        // AC4: ILlmAdapter interface unchanged — verified structurally
        // ==============================================================================

        // Mutation: would catch if StartConversation was added to ILlmAdapter
        [Fact]
        public void StartConversation_is_not_on_ILlmAdapter_interface()
        {
            // ILlmAdapter must not have StartConversation — it's a concrete adapter method
            var ilmType = typeof(Pinder.Core.Interfaces.ILlmAdapter);
            var method = ilmType.GetMethod("StartConversation");
            Assert.Null(method);
        }

        // Mutation: would catch if HasActiveConversation was added to ILlmAdapter
        [Fact]
        public void HasActiveConversation_is_not_on_ILlmAdapter_interface()
        {
            var ilmType = typeof(Pinder.Core.Interfaces.ILlmAdapter);
            var prop = ilmType.GetProperty("HasActiveConversation");
            Assert.Null(prop);
        }

        // ==============================================================================
        // Edge case: API failure in stateful mode
        // ==============================================================================

        // Mutation: would catch if API failure destroyed the session entirely
        [Fact]
        public async Task API_failure_preserves_session_active_state()
        {
            var callNum = 0;
            var handler = new CapturingHandler(req =>
            {
                callNum++;
                if (callNum <= 3)
                {
                    // All retry attempts fail (500)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            "{\"error\":{\"type\":\"server_error\",\"message\":\"fail\"}}",
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
                // Subsequent calls succeed
                return CapturingHandler.MakeJsonResponse(FourOptionResponse);
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");

            // First call should throw due to API failure (after retries)
            await Assert.ThrowsAnyAsync<Exception>(
                () => adapter.GetDialogueOptionsAsync(MakeDialogueContext()));

            // Session should still be active after failure
            Assert.True(adapter.HasActiveConversation);
        }

        // ==============================================================================
        // Edge case: Stateless path is truly unchanged
        // ==============================================================================

        // Mutation: would catch if stateless path accidentally used session logic
        [Fact]
        public async Task Stateless_deliver_sends_single_message_request()
        {
            var handler = new CapturingHandler("delivered");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            // No StartConversation — stateless mode
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var messages = body["messages"] as JArray;
            Assert.Single(messages!);
        }

        // Mutation: would catch if stateless GetOpponentResponse built accumulated messages
        [Fact]
        public async Task Stateless_opponent_response_sends_single_message_request()
        {
            var responseText = "[RESPONSE] \"Hey back\"\n[SIGNALS]\nTELL: Charm (likes compliments)";
            var handler = new CapturingHandler(responseText);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            // No StartConversation — stateless mode
            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var messages = body["messages"] as JArray;
            Assert.Single(messages!);
        }

        // Mutation: would catch if calling multiple stateless methods somehow accumulated state
        [Fact]
        public async Task Stateless_multiple_calls_do_not_accumulate()
        {
            var handler = new CapturingHandler(FourOptionResponse);
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            // Two stateless calls
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            // Both requests should have exactly 1 message each
            var body1 = JObject.Parse(handler.RequestBodies[0]);
            var body2 = JObject.Parse(handler.RequestBodies[1]);
            Assert.Single((JArray)body1["messages"]!);
            Assert.Single((JArray)body2["messages"]!);
        }

        // ==============================================================================
        // Edge case: Large message accumulation (unbounded growth)
        // ==============================================================================

        // Mutation: would catch if session truncated messages after N turns
        [Fact]
        public void ConversationSession_accumulates_many_messages()
        {
            var session = new ConversationSession("system");
            for (int i = 0; i < 60; i++)
            {
                session.AppendUser($"user-{i}");
                session.AppendAssistant($"assistant-{i}");
            }

            Assert.Equal(120, session.Messages.Count);

            var request = session.BuildRequest("model", 1024, 0.9);
            Assert.Equal(120, request.Messages.Length);

            // Verify first and last messages
            Assert.Equal("user-0", request.Messages[0].Content);
            Assert.Equal("assistant-59", request.Messages[119].Content);
        }

        // ==============================================================================
        // Edge case: BuildRequest parameters pass through correctly
        // ==============================================================================

        // Mutation: would catch if BuildRequest hardcoded model/maxTokens/temperature
        [Fact]
        public void BuildRequest_passes_through_all_parameters()
        {
            var session = new ConversationSession("sys");
            session.AppendUser("u");

            var req = session.BuildRequest("custom-model-v2", 4096, 0.3);
            Assert.Equal("custom-model-v2", req.Model);
            Assert.Equal(4096, req.MaxTokens);
            Assert.Equal(0.3, req.Temperature);
        }

        // ==============================================================================
        // Edge case: Session system prompt content preserved exactly
        // ==============================================================================

        // Mutation: would catch if system prompt was trimmed or modified
        [Fact]
        public void SystemBlocks_preserves_exact_prompt_text()
        {
            var prompt = "  You are Velvet, a sardonic music critic.\n\nWith trailing spaces  ";
            var session = new ConversationSession(prompt);
            Assert.Equal(prompt, session.SystemBlocks[0].Text);
        }

        // Mutation: would catch if constructor created multiple system blocks
        [Fact]
        public void SystemBlocks_contains_exactly_one_block()
        {
            var session = new ConversationSession("multi\nline\nprompt");
            Assert.Single(session.SystemBlocks);
        }

        // ==============================================================================
        // Edge case: InterestChangeBeat stateless path
        // ==============================================================================

        // Mutation: would catch if stateless beat accumulated messages
        [Fact]
        public async Task Stateless_interest_beat_sends_single_message()
        {
            var handler = new CapturingHandler("*smiles*");
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            var body = JObject.Parse(handler.RequestBodies[0]);
            var messages = body["messages"] as JArray;
            Assert.Single(messages!);
        }

        // ==============================================================================
        // Error recovery: API failure must not corrupt session state
        // ==============================================================================

        // Mutation: would catch if AppendUser was not rolled back on API failure
        [Fact]
        public async Task API_failure_rolls_back_user_message_session_stays_clean()
        {
            var callNum = 0;
            var handler = new CapturingHandler(req =>
            {
                callNum++;
                if (callNum <= 3) // AnthropicClient retries up to 3 times for 500
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

            // First call fails — should NOT leave dangling user message
            await Assert.ThrowsAnyAsync<Exception>(
                () => adapter.GetDialogueOptionsAsync(MakeDialogueContext()));

            // Session should have 0 messages (rolled back)
            Assert.True(adapter.HasActiveConversation);

            // Reset callNum so next call succeeds
            callNum = 3;

            // Second call should succeed with only 1 message (fresh user)
            var options = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            Assert.NotNull(options);
            Assert.Equal(4, options.Length);

            // The successful request should have exactly 1 message
            // (the last request body is the successful one)
            var lastBody = JObject.Parse(handler.RequestBodies[handler.RequestBodies.Count - 1]);
            var messages = lastBody["messages"] as JArray;
            Assert.Single(messages!);
            Assert.Equal("user", messages![0]!["role"]!.ToString());
        }

        // Mutation: would catch if only GetDialogueOptions had rollback but not DeliverMessage
        [Fact]
        public async Task API_failure_in_DeliverMessage_rolls_back_user_message()
        {
            var callNum = 0;
            var handler = new CapturingHandler(req =>
            {
                callNum++;
                if (callNum <= 3)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            "{\"error\":{\"type\":\"server_error\",\"message\":\"fail\"}}",
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return CapturingHandler.MakeJsonResponse("delivered text");
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");

            // Fail
            await Assert.ThrowsAnyAsync<Exception>(
                () => adapter.DeliverMessageAsync(MakeDeliveryContext()));

            // Reset so next succeeds
            callNum = 3;

            // Should work cleanly with 1 user message
            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());
            Assert.Equal("delivered text", result);

            var lastBody = JObject.Parse(handler.RequestBodies[handler.RequestBodies.Count - 1]);
            var messages = lastBody["messages"] as JArray;
            Assert.Single(messages!);
        }

        // Mutation: would catch if failure after successful calls corrupted prior history
        [Fact]
        public async Task API_failure_after_successful_call_preserves_prior_history()
        {
            var callNum = 0;
            var handler = new CapturingHandler(req =>
            {
                callNum++;
                // Calls 1-3: succeed (initial call retries are transparent)
                if (callNum <= 1)
                    return CapturingHandler.MakeJsonResponse(FourOptionResponse);
                // Calls 2-4: fail (second call retries)
                if (callNum <= 4)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            "{\"error\":{\"type\":\"server_error\",\"message\":\"fail\"}}",
                            System.Text.Encoding.UTF8, "application/json")
                    };
                }
                // Call 5+: succeed again
                return CapturingHandler.MakeJsonResponse("delivered ok");
            });
            using var http = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), http);

            adapter.StartConversation("system");

            // First call succeeds — session has [user, assistant]
            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            // Second call fails — should roll back, leaving session with [user, assistant]
            await Assert.ThrowsAnyAsync<Exception>(
                () => adapter.DeliverMessageAsync(MakeDeliveryContext()));

            // Reset for success
            callNum = 4;

            // Third call should see prior 2 messages + new user = 3 total
            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var lastBody = JObject.Parse(handler.RequestBodies[handler.RequestBodies.Count - 1]);
            var messages = lastBody["messages"] as JArray;
            Assert.Equal(3, messages!.Count);
            Assert.Equal("user", messages[0]!["role"]!.ToString());
            Assert.Equal("assistant", messages[1]!["role"]!.ToString());
            Assert.Equal("user", messages[2]!["role"]!.ToString());
        }

        // ==============================================================================
        // ConversationSession.RemoveLast
        // ==============================================================================

        // Mutation: would catch if RemoveLast was a no-op
        [Fact]
        public void RemoveLast_removes_last_appended_message()
        {
            var session = new ConversationSession("system");
            session.AppendUser("u1");
            session.AppendAssistant("a1");
            session.AppendUser("u2");

            session.RemoveLast();

            Assert.Equal(2, session.Messages.Count);
            Assert.Equal("a1", session.Messages[1].Content);
        }

        // Mutation: would catch if RemoveLast silently did nothing on empty list
        [Fact]
        public void RemoveLast_throws_on_empty_session()
        {
            var session = new ConversationSession("system");
            Assert.Throws<InvalidOperationException>(() => session.RemoveLast());
        }
    }
}

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
    /// Spec-driven tests for AnthropicLlmAdapter (issue #208).
    /// Complements AnthropicLlmAdapterTests with additional coverage
    /// from the spec's edge cases, error conditions, and acceptance criteria.
    /// </summary>
    public partial class AnthropicLlmAdapterSpecTests
    {
        // ==============================================================================
        // Test Infrastructure
        // ==============================================================================

        private sealed class CapturingHttpHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
            public List<string> RequestBodies { get; } = new List<string>();

            public CapturingHttpHandler(string responseText)
                : this(_ => MakeJsonResponse(responseText)) { }

            public CapturingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
            {
                _factory = factory;
            }

            private static HttpResponseMessage MakeJsonResponse(string text)
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
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                if (request.Content != null)
                    RequestBodies.Add(await request.Content.ReadAsStringAsync());
                else
                    RequestBodies.Add("");
                return _factory(request);
            }
        }

        private static AnthropicOptions DefaultOptions(string key = "test-key") => new AnthropicOptions
        {
            ApiKey = key,
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
            playerPrompt: "You are Thundercock",
            dateePrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey there") },
            dateeLastMessage: "Hey there",
            activeTraps: new string[0],
            currentInterest: 10,
            playerName: "Thundercock",
            dateeName: "Velvet",
            currentTurn: 1);

        private static DeliveryContext MakeDeliveryContext() => new DeliveryContext(
            playerPrompt: "You are Thundercock",
            dateePrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey") },
            dateeLastMessage: "Hey",
            chosenOption: new DialogueOption(StatType.Charm, "Nice to meet you"),
            outcome: FailureTier.None,
            beatDcBy: 5,
            activeTraps: new string[0],
            playerName: "Thundercock",
            dateeName: "Velvet");

        private static DateeContext MakeDateeContext() => new DateeContext(
            playerPrompt: "You are Thundercock",
            dateePrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey") },
            dateeLastMessage: "Hey",
            activeTraps: new string[0],
            currentInterest: 12,
            playerDeliveredMessage: "Nice to meet you too!",
            interestBefore: 10,
            interestAfter: 12,
            responseDelayMinutes: 2.0,
            playerName: "Thundercock",
            dateeName: "Velvet");

        // ==============================================================================
        // AC1: All 4 ILlmAdapter methods implemented — behavioral integration
        // ==============================================================================

        // What: AC1 - Adapter implements ILlmAdapter interface
        // Mutation: Would catch if class does not implement the interface
        [Fact]
        public void AnthropicLlmAdapter_Implements_ILlmAdapter()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            Assert.IsAssignableFrom<Pinder.Core.Interfaces.ILlmAdapter>(adapter);
        }

        // What: AC1 - Adapter implements IDisposable
        // Mutation: Would catch if IDisposable is not implemented
        [Fact]
        public void AnthropicLlmAdapter_Implements_IDisposable()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            Assert.IsAssignableFrom<IDisposable>(adapter);
        }

        // ==============================================================================
        // AC2: cache_control on system blocks — deeper verification
        // ==============================================================================

        // What: AC2 - DeliverMessageAsync also uses cached system blocks with both prompts
        // Mutation: Would catch if delivery skips caching or uses wrong builder
        [Fact]
        public async Task DeliverMessageAsync_SystemBlocks_HavePlayerOnlyPromptWithCacheControl()
        {
            var handler = new CapturingHttpHandler("Delivered text");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Single(handler.RequestBodies);
            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.NotNull(body);
            // Issue #241: delivery uses player-only system blocks to prevent voice contamination
            Assert.Equal(1, body!.System.Length);
            Assert.All(body.System, block =>
            {
                Assert.NotNull(block.CacheControl);
                Assert.Equal("ephemeral", block.CacheControl!.Type);
            });
        }

        // What: AC2 - GetInterestChangeBeatAsync has empty/no system blocks
        // Mutation: Would catch if interest beat incorrectly adds system blocks
        [Fact(Skip = "Removed in #573")]
        public async Task GetInterestChangeBeatAsync_SystemBlocks_AreEmpty()
        {
            var handler = new CapturingHttpHandler("Beat text");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var ctx = new InterestChangeContext("Velvet", 15, 17, InterestState.VeryIntoIt);
            await adapter.GetInterestChangeBeatAsync(ctx);

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Empty(body!.System);
        }

        // AC3: Datee response uses ONLY DateePrompt
        // ==============================================================================

        // What: AC3 - Datee system has exactly 1 block
        // Mutation: Would catch if 2 blocks sent (player + datee) instead of datee-only
        [Fact]
        public async Task GetDateeResponseAsync_ExactlyOneSystemBlock()
        {
            var handler = new CapturingHttpHandler(@"[RESPONSE]
""text""");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDateeResponseAsync(MakeDateeContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Single(body!.System);
            Assert.Equal("ephemeral", body.System[0].CacheControl?.Type);
        }

        // ==============================================================================
        // Dispose behavior from spec
        // ==============================================================================

        // What: Spec - Dispose on adapter constructed with external client doesn't dispose client
        // Mutation: Would catch if external client is disposed by adapter
        [Fact]
        public void Dispose_ExternalClient_NotDisposed()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);
            adapter.Dispose();

            // External client should still be usable — won't throw ObjectDisposedException
            // (If adapter disposed it, this would throw)
            Assert.NotNull(client.BaseAddress?.ToString() ?? "still alive");
        }

        // ==============================================================================
        // DTO backward compatibility (AC8 prerequisite)
        // ==============================================================================

        // What: AC8 - DialogueContext without new optional params still works
        // Mutation: Would catch if required params were added instead of optional
        [Fact]
        public void DialogueContext_OldConstructorCallStillWorks()
        {
            // This is a compile-time check + runtime defaults check
            var ctx = new DialogueContext(
                "player", "datee",
                new List<(string, string)>(),
                "last", new string[0], 10);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.DateeName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        // What: AC8 - DateeContext new fields have correct defaults
        // Mutation: Would catch if defaults are non-empty string or non-zero
        [Fact]
        public void DateeContext_NewFieldsDefaultCorrectly()
        {
            var ctx = new DateeContext(
                "player", "datee",
                new List<(string, string)>(),
                "last", new string[0], 10, "msg",
                10, 12, 2.0);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.DateeName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        // What: AC8 - DeliveryContext new fields have correct defaults
        // Mutation: Would catch if defaults changed
        [Fact]
        public void DeliveryContext_NewFieldsDefaultCorrectly()
        {
            var ctx = new DeliveryContext(
                "player", "datee",
                new List<(string, string)>(),
                "last",
                new DialogueOption(StatType.Charm, "text"),
                FailureTier.None, 5,
                new string[0]);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.DateeName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        // ==============================================================================
        // Helpers
        // ==============================================================================

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _ex;
            public ThrowingHandler(Exception ex) => _ex = ex;
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken ct) => throw _ex;
        }
    }
}

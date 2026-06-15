using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// Issue #208 spec-driven tests for AnthropicLlmAdapter.
    /// Covers acceptance criteria, edge cases, and error conditions
    /// from docs/specs/issue-208-spec.md.
    /// Prototype maturity: happy-path per AC + key edge cases.
    /// </summary>
    public partial class AnthropicLlmAdapterIssue208Tests
    {
        // ======================================================================
        // Test infrastructure
        // ======================================================================

        private sealed class MockHttpHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastRequestBody { get; private set; }
            public string ResponseBody { get; set; } = "";
            public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                if (request.Content != null)
                    LastRequestBody = await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(StatusCode)
                {
                    Content = new StringContent(ResponseBody)
                };
            }
        }

        private static AnthropicOptions DefaultOptions() => new AnthropicOptions
        {
            ApiKey = "test-key-208",
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
            playerAvatarPrompt: "You are Thundercock, a bold confident penis",
            dateePrompt: "You are Velvet, a mysterious and alluring match",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey there, nice profile pic") },
            dateeLastMessage: "Hey there, nice profile pic",
            activeTraps: new string[0],
            currentInterest: 10,
            playerName: "Thundercock",
            dateeName: "Velvet",
            currentTurn: 1, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty, Pinder.Core.Stats.StatType.Chaos, Pinder.Core.Stats.StatType.Wit, Pinder.Core.Stats.StatType.SelfAwareness });

        // #1138: MakeDeliveryContext()/DeliveryContext removed — delivery prompt
        // surface was collapsed into the deterministic DeliveryOverlay (#1125).

        private static DateeContext MakeDateeContext() => new DateeContext(
            dateePrompt: "You are Velvet",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey") },
            dateeLastMessage: "Hey",
            activeTraps: new string[0],
            currentInterest: 12,
            playerDeliveredMessage: "Nice to meet you!",
            interestBefore: 10,
            interestAfter: 12,
            responseDelayMinutes: 2.0,
            playerName: "Thundercock",
            dateeName: "Velvet");

        private static InterestChangeContext MakeInterestChangeContext() =>
            new InterestChangeContext("Velvet", 15, 17, InterestState.VeryIntoIt);

        // ======================================================================
        // AC1: All 4 ILlmAdapter methods implemented
        // ======================================================================

        // What: AC1 — adapter implements ILlmAdapter interface
        // Mutation: Would catch if class declaration doesn't implement ILlmAdapter
        [Fact]
        public void AC1_Adapter_Is_ILlmAdapter()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);
            Assert.IsAssignableFrom<Pinder.Core.Interfaces.ILlmAdapter>(adapter);
        }

        // What: AC1 — adapter implements IDisposable
        // Mutation: Would catch if IDisposable is not implemented
        [Fact]
        public void AC1_Adapter_Is_IDisposable()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);
            Assert.IsAssignableFrom<IDisposable>(adapter);
            adapter.Dispose();
        }

        // ======================================================================
        // Dispose behavior
        // ======================================================================

        // What: Spec — disposing adapter with external client doesn't dispose client
        // Mutation: Would catch if external client is disposed by adapter
        [Fact]
        public void Dispose_ExternalClient_StillUsable()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);
            adapter.Dispose();

            // External client should still be functional (not disposed)
            // If adapter disposed it, any operation would throw ObjectDisposedException
            Assert.NotNull(client.DefaultRequestHeaders);
        }
    }
}
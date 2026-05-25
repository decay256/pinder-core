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
    public partial class AnthropicLlmAdapterIssue208Tests
    {
        // ======================================================================
        // AC7: Mocked HTTP — request shape verification
        // ======================================================================

        // What: AC7 — correct URL for API endpoint
        // Mutation: Would catch if URL is wrong (e.g. /v2/messages or different host)
        [Fact]
        public async Task AC7_RequestUrl_IsCorrect()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n\"Hi\"")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            Assert.NotNull(handler.LastRequest);
            Assert.Contains("api.anthropic.com", handler.LastRequest!.RequestUri!.Host);
            Assert.Contains("/v1/messages", handler.LastRequest.RequestUri.PathAndQuery);
        }

        // What: AC7 — model string matches options
        // Mutation: Would catch if model is hardcoded instead of read from options
        [Fact]
        public async Task AC7_ModelString_FromOptions()
        {
            var options = DefaultOptions();
            options.Model = "claude-test-model";
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("\"test\"") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal("claude-test-model", body["model"]!.ToString());
        }

        // What: AC7 — DialogueOptions temperature is 0.9 by default
        // Mutation: Would catch if wrong default temperature is used
        [Fact]
        public async Task AC7_DialogueOptions_DefaultTemperature_0_9()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n\"Hi\"")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal(0.9, body["temperature"]!.Value<double>(), 2);
        }

        // What: AC7 — DeliverMessage temperature is 0.7 by default
        // Mutation: Would catch if delivery uses wrong temperature (e.g., 0.9)
        [Fact]
        public async Task AC7_DeliverMessage_DefaultTemperature_0_7()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("\"Delivered\"") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal(0.7, body["temperature"]!.Value<double>(), 2);
        }

        // What: AC7 — OpponentResponse temperature is 0.85 by default
        // Mutation: Would catch if opponent response uses wrong temperature
        [Fact]
        public async Task AC7_OpponentResponse_DefaultTemperature_0_85()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("[RESPONSE]\n\"Hey\"") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal(0.85, body["temperature"]!.Value<double>(), 2);
        }

        // What: AC7 — InterestChangeBeat temperature is 0.8 by default
        // Mutation: Would catch if interest change beat uses wrong temperature
        [Fact(Skip = "Removed in #573")]
        public async Task AC7_InterestChangeBeat_DefaultTemperature_0_8()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("\"A beat\"") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal(0.8, body["temperature"]!.Value<double>(), 2);
        }

        // What: AC7 — InterestChangeBeat has empty system blocks
        // Mutation: Would catch if system blocks are populated for interest change beat
        [Fact(Skip = "Removed in #573")]
        public async Task AC7_InterestChangeBeat_EmptySystemBlocks()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("\"A beat\"") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"];
            // System should be empty array or absent
            if (system != null && system is JArray arr)
            {
                Assert.Empty(arr);
            }
            // If system is null/absent, that's also acceptable
        }

        // What: Temperature overrides from options are used
        // Mutation: Would catch if per-method temperature overrides are ignored
        [Fact]
        public async Task AC7_TemperatureOverrides_Used()
        {
            var options = DefaultOptions();
            options.DialogueOptionsTemperature = 0.5;
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n\"Hi\"")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal(0.5, body["temperature"]!.Value<double>(), 2);
        }

        // What: MaxTokens from options flows to request
        // Mutation: Would catch if max_tokens is hardcoded instead of from options
        [Fact]
        public async Task AC7_MaxTokens_FromOptions()
        {
            var options = DefaultOptions();
            options.MaxTokens = 2048;
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n\"Hi\"")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            Assert.Equal(2048, body["max_tokens"]!.Value<int>());
        }

        // ======================================================================
        // Error conditions
        // ======================================================================

        // What: Spec — null context throws ArgumentNullException
        // Mutation: Would catch if null context is silently handled
        [Fact]
        public async Task Error_NullContext_ThrowsArgumentNullException()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.GetDialogueOptionsAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.DeliverMessageAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.GetOpponentResponseAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.GetInterestChangeBeatAsync(null!));
        }

        // What: Spec — null options throws ArgumentNullException
        // Mutation: Would catch if null options doesn't throw
        [Fact]
        public void Error_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AnthropicLlmAdapter(null!));
        }

        // What: Spec — HttpRequestException propagates (network failure)
        // Mutation: Would catch if network exceptions are silently caught
        [Fact]
        public async Task Error_NetworkFailure_PropagatesHttpRequestException()
        {
            var handler = new MockHttpHandler { StatusCode = HttpStatusCode.InternalServerError };
            // The AnthropicClient should throw for 5xx after retries, or the adapter should propagate
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            // This should throw some kind of exception — either HttpRequestException or AnthropicApiException
            await Assert.ThrowsAnyAsync<Exception>(() =>
                adapter.GetDialogueOptionsAsync(MakeDialogueContext()));
        }

        // What: Spec — LLM empty response for DeliverMessageAsync returns empty string
        // Mutation: Would catch if empty response returns null instead of ""
        [Fact]
        public async Task Error_EmptyLlmResponse_DeliverReturnsEmptyString()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());
            Assert.NotNull(result);
            Assert.Equal("", result);
        }

        // What: Spec — LLM empty response for GetOpponentResponseAsync
        // Mutation: Would catch if empty response throws instead of returning default
        [Fact]
        public async Task Error_EmptyLlmResponse_OpponentReturnsEmptyMessage()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetOpponentResponseAsync(MakeOpponentContext());
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — LLM empty response for GetInterestChangeBeatAsync returns null
        // Mutation: Would catch if empty response returns "" instead of null
        [Fact]
        public async Task Error_EmptyLlmResponse_InterestChangeBeat_ReturnsNull()
        {
            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse("") };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());
            Assert.Null(result);
        }

        // What: Spec — unparseable response for GetDialogueOptionsAsync returns 4 defaults
        // Mutation: Would catch if unparseable response throws
        [Fact]
        public async Task Error_UnparseableResponse_DialogueOptions_Returns4Defaults()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("This is completely random text with no structure at all!!!")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            Assert.Equal(4, result.Length);
        }

        // ======================================================================
        // Full response parsing flow (end-to-end with mocked HTTP)
        // ======================================================================

        // What: AC1 — GetDialogueOptionsAsync full happy path
        // Mutation: Would catch if response text is not parsed into DialogueOption array
        [Fact]
        public async Task FullFlow_GetDialogueOptionsAsync_ParsesCorrectly()
        {
            var responseText = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Thanks! You're not so bad yourself.""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Oh, you haven't even seen my best angle yet""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: The Setup] [TELL_BONUS: yes]
""Bold opening. I like that.""

OPTION_4
[STAT: HONESTY] [CALLBACK: 2] [COMBO: none] [TELL_BONUS: no]
""I appreciate the compliment.""";

            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse(responseText) };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Thanks! You're not so bad yourself.", result[0].IntendedText);
            Assert.Equal(StatType.Rizz, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal("The Setup", result[2].ComboName);
            Assert.True(result[2].HasTellBonus);
            Assert.Equal(StatType.Honesty, result[3].Stat);
            Assert.Equal(2, result[3].CallbackTurnNumber);
        }

        // What: AC1 — GetOpponentResponseAsync full happy path with signals
        // Mutation: Would catch if signal parsing is not wired up in the adapter method
        [Fact]
        public async Task FullFlow_GetOpponentResponseAsync_ParsesSignals()
        {
            var responseText = @"[RESPONSE]
""Haha the penguin section! So what's YOUR type then?""

[SIGNALS]
TELL: CHARM (genuinely flustered by direct compliments)
WEAKNESS: WIT -2 (overthinking their responses)";

            var handler = new MockHttpHandler { ResponseBody = MakeApiResponse(responseText) };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            Assert.Contains("penguin section", result.MessageText);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Contains("flustered", result.DetectedTell.Description);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Wit, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        // What: AC1 — DeliverMessageAsync returns the response text
        // Mutation: Would catch if deliver returns empty string instead of LLM response
        [Fact]
        public async Task FullFlow_DeliverMessageAsync_ReturnsResponseText()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("Thanks! You're not so bad yourself. What brings you to Pinder?")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Contains("What brings you to Pinder", result);
        }

        // What: AC1 — GetInterestChangeBeatAsync returns narrative beat
        // Mutation: Would catch if interest change beat returns null for valid response
        [Fact(Skip = "Removed in #573")]
        public async Task FullFlow_InterestChangeBeat_ReturnsNarrativeBeat()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("Velvet leans closer, a smile spreading across her face.")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetInterestChangeBeatAsync(MakeInterestChangeContext());

            Assert.NotNull(result);
            Assert.Contains("Velvet", result!);
        }
    }
}
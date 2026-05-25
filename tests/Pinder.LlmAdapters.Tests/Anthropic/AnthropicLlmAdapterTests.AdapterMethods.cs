using System;
using System.Net.Http;
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
    public partial class AnthropicLlmAdapterTests
    {
        [Fact]
        public async Task GetDialogueOptionsAsync_sends_correct_request_shape()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse(@"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hello""
OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hey""
OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hi there""
OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Nice profile""")
            };
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            using var adapter = new AnthropicLlmAdapter(options, client);

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            Assert.Equal(4, result.Length);

            // Verify request shape
            Assert.NotNull(handler.CapturedRequestBody);
            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.NotNull(body);
            Assert.Equal("claude-sonnet-4-20250514", body!.Model);
            Assert.Equal(0.9, body.Temperature, 2);
            // Only player prompt in system (fix for voice bleed #487)
            Assert.Single(body.System);
            Assert.Equal("ephemeral", body.System[0].CacheControl?.Type);
            Assert.Contains("Thundercock", body.System[0].Text);
            // Opponent profile appears in user message, not system
            Assert.Single(body.Messages);
            Assert.Contains("Velvet", body.Messages[0].Content);
            Assert.Contains("YOU ARE TALKING TO", body.Messages[0].Content);
        }

        [Fact]
        public async Task DeliverMessageAsync_sends_correct_temperature()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse("Delivered message text")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Equal("Delivered message text", result);
            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.Equal(0.7, body!.Temperature, 2);
            Assert.Equal(1, body.System.Length); // Player-only prompt cached
        }

        [Fact]
        public async Task GetOpponentResponseAsync_uses_only_opponent_prompt()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse(@"[RESPONSE]
""That's sweet, tell me more""")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            Assert.Equal("That's sweet, tell me more", result.MessageText);
            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.Equal(0.85, body!.Temperature, 2);
            // Only 1 system block — opponent only
            Assert.Single(body.System);
            Assert.Contains("Velvet", body.System[0].Text);
            Assert.DoesNotContain("Thundercock", body.System[0].Text);
            Assert.Equal("ephemeral", body.System[0].CacheControl?.Type);
        }

        [Fact(Skip = "Removed in #573")]
        public async Task GetInterestChangeBeatAsync_no_system_blocks()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse("Velvet leans closer to her phone.")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var context = new InterestChangeContext("Velvet", 15, 17, InterestState.VeryIntoIt);
            var result = await adapter.GetInterestChangeBeatAsync(context);

            Assert.Equal("Velvet leans closer to her phone.", result);
            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.Equal(0.8, body!.Temperature, 2);
            Assert.Empty(body.System); // No system blocks
        }

        [Fact]
        public async Task GetInterestChangeBeatAsync_empty_response_returns_null()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse("")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var context = new InterestChangeContext("Velvet", 10, 11, InterestState.Interested);
            var result = await adapter.GetInterestChangeBeatAsync(context);

            Assert.Null(result);
        }

        [Fact]
        public async Task Temperature_overrides_used_when_set()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse(@"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Test""
OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Test2""
OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Test3""
OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Test4""")
            };
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.DialogueOptionsTemperature = 0.5;
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.CapturedRequestBody!);
            Assert.Equal(0.5, body!.Temperature, 2);
        }

        [Fact]
        public async Task Null_context_throws_ArgumentNullException()
        {
            var handler = new FakeHttpHandler { ResponseBody = MakeApiResponse("") };
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

        [Fact]
        public async Task GetDialogueOptionsAsync_fallback_on_unparseable_response()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse("Just some random text, not options at all.")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            Assert.Equal(4, result.Length);
            // All defaults
            foreach (var opt in result)
            {
                Assert.Equal("...", opt.IntendedText);
            }
        }

        [Fact]
        public async Task GetOpponentResponseAsync_with_signals_parsed()
        {
            var handler = new FakeHttpHandler
            {
                ResponseBody = MakeApiResponse(@"[RESPONSE]
""Nice one! Tell me more about yourself""

[SIGNALS]
TELL: CHARM (they blushed at the compliment)
WEAKNESS: HONESTY -3 (clearly deflecting personal questions)")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            Assert.Equal("Nice one! Tell me more about yourself", result.MessageText);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Honesty, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(3, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void Constructor_null_options_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AnthropicLlmAdapter(null!));
        }
    }
}

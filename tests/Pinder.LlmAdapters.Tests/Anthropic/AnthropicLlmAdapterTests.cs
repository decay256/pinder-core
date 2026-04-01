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
    // ============================================================
    // ParseDialogueOptions tests
    // ============================================================

    public class ParseDialogueOptionsTests
    {
        [Fact]
        public void Null_input_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Chaos, result[3].Stat);
            foreach (var opt in result) Assert.Equal("...", opt.IntendedText);
        }

        [Fact]
        public void Empty_string_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("");
            Assert.Equal(4, result.Length);
            foreach (var opt in result) Assert.Equal("...", opt.IntendedText);
        }

        [Fact]
        public void Parses_4_valid_options()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Thanks! You're not so bad yourself.""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Oh, you haven't even seen my best angle yet""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Bold opening. I like that in a match.""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""I appreciate the compliment. Honestly I spent way too long picking that photo.""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Rizz, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Honesty, result[3].Stat);
            Assert.Contains("not so bad", result[0].IntendedText);
        }

        [Fact]
        public void Parses_callback_and_combo_metadata()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: yes]
""Speaking of pizza, remember that crime?""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Wit, result[0].Stat);
            Assert.Equal(3, result[0].CallbackTurnNumber);
            Assert.Equal("The Setup", result[0].ComboName);
            Assert.True(result[0].HasTellBonus);
        }

        [Fact]
        public void Callback_turn_N_format_parsed()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: turn_5] [COMBO: none] [TELL_BONUS: no]
""Some text here""";
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(5, result[0].CallbackTurnNumber);
        }

        [Fact]
        public void NonNumeric_callback_returns_null_turn()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: pizza_story] [COMBO: none] [TELL_BONUS: no]
""Some text here""";
            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
        }

        [Fact]
        public void One_valid_option_padded_to_4()
        {
            var input = @"OPTION_1
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Just one option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
            Assert.Equal("Just one option", result[0].IntendedText);
            // Padding skips stats already present — Rizz is not in defaults so all 3 defaults used
            Assert.Equal(StatType.Charm, result[1].Stat);
            Assert.Equal("...", result[1].IntendedText);
        }

        [Fact]
        public void Five_options_truncated_to_4()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""One""
OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Two""
OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Three""
OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Four""
OPTION_5
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Five""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal("One", result[0].IntendedText);
            Assert.Equal("Four", result[3].IntendedText);
        }

        [Fact]
        public void Invalid_stat_skipped_and_padded()
        {
            var input = @"OPTION_1
[STAT: INVALID_STAT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Bad stat option""
OPTION_2
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Good option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Good option", result[0].IntendedText);
        }

        [Fact]
        public void SelfAwareness_stat_parsed_from_SELF_AWARENESS()
        {
            var input = @"OPTION_1
[STAT: SELF_AWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""I know this is awkward but...""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
        }

        [Fact]
        public void Missing_quoted_text_skips_option()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
no quotes here
OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Valid text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
        }

        [Fact]
        public void HasWeaknessWindow_always_false()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""Some text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.False(result[0].HasWeaknessWindow);
        }

        [Fact]
        public void Garbage_input_returns_4_defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("just some random text\nwith lines");
            Assert.Equal(4, result.Length);
            foreach (var opt in result) Assert.Equal("...", opt.IntendedText);
        }
    }

    // ============================================================
    // DialogueOptionsInstruction template content tests
    // ============================================================

    public class DialogueOptionsInstructionTests
    {
        [Fact]
        public void Instruction_contains_OPTION_headers()
        {
            Assert.Contains("OPTION_1", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("OPTION_2", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void Instruction_contains_output_format_rules()
        {
            Assert.Contains("STAT must be one of", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("SELF_AWARENESS", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("double quotes", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("No extra text before OPTION_1", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void Instruction_preserves_original_guidelines()
        {
            // Verify original instructional content was not removed
            Assert.Contains("Generate exactly 4 dialogue options", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("CHARM, RIZZ, HONESTY, CHAOS, WIT, SELF_AWARENESS", PromptTemplates.DialogueOptionsInstruction);
            Assert.Contains("Keep options concise", PromptTemplates.DialogueOptionsInstruction);
        }

        [Fact]
        public void WellFormed_output_matching_instruction_format_parses_correctly()
        {
            // Simulate LLM output that follows the format described in the instruction
            var llmOutput = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""hey so I noticed you're into marine biology… is that a career thing or a documentary thing""

OPTION_2
[STAT: HONESTY] [CALLBACK: turn_2] [COMBO: The Reveal] [TELL_BONUS: yes]
""okay real talk I looked at your profile for way too long and I have questions about the penguin photo""

OPTION_3
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""what if penguins had tinder. like what would their bios say. I need your thoughts on this""

OPTION_4
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""your bio says looking for someone who gets it which is either deeply profound or deeply vague""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(llmOutput);

            Assert.Equal(4, result.Length);

            // All options have non-empty IntendedText (not "...")
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
                Assert.False(string.IsNullOrWhiteSpace(opt.IntendedText));
            }

            // Correct stats
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Chaos, result[2].Stat);
            Assert.Equal(StatType.Wit, result[3].Stat);

            // Metadata on option 2
            Assert.Equal(2, result[1].CallbackTurnNumber);
            Assert.Equal("The Reveal", result[1].ComboName);
            Assert.True(result[1].HasTellBonus);

            // Option 1 has no callback/combo/tell
            Assert.Null(result[0].CallbackTurnNumber);
            Assert.Null(result[0].ComboName);
            Assert.False(result[0].HasTellBonus);
        }
    }

    // ============================================================
    // ParseOpponentResponse tests
    // ============================================================

    public class ParseOpponentResponseTests
    {
        [Fact]
        public void Null_input_returns_empty_response()
        {
            var result = AnthropicLlmAdapter.ParseOpponentResponse(null);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void Empty_input_returns_empty_response()
        {
            var result = AnthropicLlmAdapter.ParseOpponentResponse("");
            Assert.Equal("", result.MessageText);
        }

        [Fact]
        public void Plain_text_without_markers_used_as_message()
        {
            var result = AnthropicLlmAdapter.ParseOpponentResponse("Just a plain message");
            Assert.Equal("Just a plain message", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void Response_block_parsed_without_signals()
        {
            var input = @"[RESPONSE]
""Haha yeah, it's pretty wild out there.""";
            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Haha yeah, it's pretty wild out there.", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void Response_and_signals_both_parsed()
        {
            var input = @"[RESPONSE]
""Haha the penguin section! So what's YOUR type then?""

[SIGNALS]
TELL: CHARM (opponent seems genuinely flustered by direct compliments)
WEAKNESS: WIT -2 (opponent is clearly overthinking their responses)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Haha the penguin section! So what's YOUR type then?", result.MessageText);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Contains("flustered", result.DetectedTell.Description);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Wit, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void Signals_with_only_tell()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
TELL: RIZZ (they keep making flirty comments)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Rizz, result.DetectedTell!.Stat);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void Signals_with_only_weakness()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
WEAKNESS: HONESTY -3 (they seem to be hiding something)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Null(result.DetectedTell);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Honesty, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(3, result.WeaknessWindow.DcReduction);
        }

        [Fact]
        public void Invalid_tell_stat_returns_null_tell()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
TELL: INVALID_STAT (desc)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Null(result.DetectedTell);
        }

        [Fact]
        public void Weakness_zero_reduction_returns_null()
        {
            // WeaknessWindow constructor requires > 0, and our parser checks for that
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
WEAKNESS: WIT -0 (desc)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void SELF_AWARENESS_parsed_in_signals()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
TELL: SELF_AWARENESS (very self-reflective)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.SelfAwareness, result.DetectedTell!.Stat);
        }

        [Fact]
        public void Malformed_signals_block_returns_null_signals()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
garbage data that makes no sense";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Some response", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }
    }

    // ============================================================
    // Adapter method integration tests (mocked HTTP)
    // ============================================================

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

    public class AnthropicLlmAdapterMethodTests
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
            Assert.Equal(2, body.System.Length); // Both player + opponent prompts
            Assert.Equal("ephemeral", body.System[0].CacheControl?.Type);
            Assert.Equal("ephemeral", body.System[1].CacheControl?.Type);
            Assert.Contains("Thundercock", body.System[0].Text);
            Assert.Contains("Velvet", body.System[1].Text);
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

        [Fact]
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

    // ============================================================
    // DTO backward compatibility tests
    // ============================================================

    public class ContextDtoBackwardCompatibilityTests
    {
        [Fact]
        public void DialogueContext_defaults_backward_compatible()
        {
            // Old call site — no playerName/opponentName/currentTurn
            var ctx = new DialogueContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new string[0], 10);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void DeliveryContext_defaults_backward_compatible()
        {
            var ctx = new DeliveryContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new DialogueOption(StatType.Charm, "test"),
                FailureTier.None, 5,
                new string[0]);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void OpponentContext_defaults_backward_compatible()
        {
            var ctx = new OpponentContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new string[0], 10, "delivered",
                10, 12, 2.0);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        [Fact]
        public void DialogueContext_new_fields_settable()
        {
            var ctx = new DialogueContext(
                "player", "opponent",
                new List<(string, string)>(), "last",
                new string[0], 10,
                playerName: "Thundercock",
                opponentName: "Velvet",
                currentTurn: 3);

            Assert.Equal("Thundercock", ctx.PlayerName);
            Assert.Equal("Velvet", ctx.OpponentName);
            Assert.Equal(3, ctx.CurrentTurn);
        }
    }
}

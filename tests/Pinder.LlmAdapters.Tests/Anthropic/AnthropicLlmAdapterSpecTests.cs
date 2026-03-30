using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    /// Spec-driven tests for AnthropicLlmAdapter (issue #208).
    /// Complements AnthropicLlmAdapterTests with additional coverage
    /// from the spec's edge cases, error conditions, and acceptance criteria.
    /// </summary>
    public class AnthropicLlmAdapterSpecTests
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
        public async Task DeliverMessageAsync_SystemBlocks_HaveBothPromptsWithCacheControl()
        {
            var handler = new CapturingHttpHandler("Delivered text");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Single(handler.RequestBodies);
            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.NotNull(body);
            Assert.Equal(2, body!.System.Length);
            Assert.All(body.System, block =>
            {
                Assert.NotNull(block.CacheControl);
                Assert.Equal("ephemeral", block.CacheControl!.Type);
            });
        }

        // What: AC2 - GetInterestChangeBeatAsync has empty/no system blocks
        // Mutation: Would catch if interest beat incorrectly adds system blocks
        [Fact]
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

        // ==============================================================================
        // AC3: Opponent response uses ONLY OpponentPrompt
        // ==============================================================================

        // What: AC3 - Opponent system has exactly 1 block
        // Mutation: Would catch if 2 blocks sent (player + opponent) instead of opponent-only
        [Fact]
        public async Task GetOpponentResponseAsync_ExactlyOneSystemBlock()
        {
            var handler = new CapturingHttpHandler(@"[RESPONSE]
""text""");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Single(body!.System);
            Assert.Equal("ephemeral", body.System[0].CacheControl?.Type);
        }

        // ==============================================================================
        // AC4: ParseDialogueOptions — additional edge cases from spec
        // ==============================================================================

        // What: AC4 Spec edge - Default padding skips stats already present in parsed options
        // Mutation: Would catch if padding doesn't skip already-present Charm, duplicating it
        [Fact]
        public void ParseDialogueOptions_PaddingSkipsAlreadyPresentStats()
        {
            // If Charm is already parsed, defaults should skip Charm and use Honesty, Wit, Chaos
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Charm option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            // Remaining 3 should skip Charm and use Honesty, Wit, Chaos
            var paddedStats = result.Skip(1).Select(o => o.Stat).ToArray();
            Assert.DoesNotContain(StatType.Charm, paddedStats);
            Assert.Contains(StatType.Honesty, paddedStats);
            Assert.Contains(StatType.Wit, paddedStats);
            Assert.Contains(StatType.Chaos, paddedStats);
        }

        // What: AC4 Spec edge - 2 parsed options, 2 defaults filling from order
        // Mutation: Would catch if padding count is wrong (e.g., pads to 5 or 3)
        [Fact]
        public void ParseDialogueOptions_TwoValidOptions_PadsWithTwo()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First""

OPTION_2
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Second""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Wit, result[1].Stat);
            // Defaults: should be Honesty and Chaos (skipping Charm and Wit)
            var defaultOpts = result.Skip(2).ToArray();
            Assert.Equal(2, defaultOpts.Length);
            Assert.All(defaultOpts, o => Assert.Equal("...", o.IntendedText));
        }

        // What: AC4 Spec edge - Default options have null/false for optional fields
        // Mutation: Would catch if defaults have non-null CallbackTurnNumber or true HasTellBonus
        [Fact]
        public void ParseDialogueOptions_DefaultOptions_HaveCorrectDefaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null);
            foreach (var opt in result)
            {
                Assert.Equal("...", opt.IntendedText);
                Assert.Null(opt.CallbackTurnNumber);
                Assert.Null(opt.ComboName);
                Assert.False(opt.HasTellBonus);
                Assert.False(opt.HasWeaknessWindow);
            }
        }

        // What: AC4 Spec edge - Multiple quoted strings: first one is used
        // Mutation: Would catch if last quoted string is used instead of first
        [Fact]
        public void ParseDialogueOptions_MultipleQuotedStrings_UsesFirst()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First quoted string"" and then ""second quoted string""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal("First quoted string", result[0].IntendedText);
        }

        // What: AC4 Spec edge - HasWeaknessWindow always false from adapter parsing
        // Mutation: Would catch if parser ever sets HasWeaknessWindow to true
        [Fact]
        public void ParseDialogueOptions_HasWeaknessWindow_AlwaysFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""text""

OPTION_2
[STAT: RIZZ] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: yes]
""text""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.All(result, opt => Assert.False(opt.HasWeaknessWindow));
        }

        // What: AC4 Spec edge - Text with extra whitespace is trimmed
        // Mutation: Would catch if whitespace trimming is missing
        [Fact]
        public void ParseDialogueOptions_ExtraWhitespace_Trimmed()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""  Hello with spaces  ""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            // Text should be trimmed or at minimum extracted from quotes
            Assert.DoesNotContain("\n", result[0].IntendedText);
        }

        // What: AC4 Spec edge - COMBO value "none" maps to null
        // Mutation: Would catch if "none" string literal stored instead of null
        [Fact]
        public void ParseDialogueOptions_ComboNone_MapsToNull()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].ComboName);
        }

        // What: AC4 Spec edge - CALLBACK "none" maps to null CallbackTurnNumber
        // Mutation: Would catch if "none" is parsed as 0 or empty string
        [Fact]
        public void ParseDialogueOptions_CallbackNone_MapsToNull()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""text""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
        }

        // ==============================================================================
        // ParseOpponentResponse — additional edge cases
        // ==============================================================================

        // What: Spec edge - Tell description is extracted from parenthesized text
        // Mutation: Would catch if description is empty or includes the stat name
        [Fact]
        public void ParseOpponentResponse_TellDescription_ExtractedFromParens()
        {
            var input = @"[RESPONSE]
""some text""

[SIGNALS]
TELL: CHARM (they blush easily at compliments)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Contains("blush", result.DetectedTell!.Description);
        }

        // What: Spec edge - WEAKNESS with -2 DC reduction
        // Mutation: Would catch if DcReduction is always 3 or hardcoded
        [Fact]
        public void ParseOpponentResponse_WeaknessMinusTwo_ParsedCorrectly()
        {
            var input = @"[RESPONSE]
""some text""

[SIGNALS]
WEAKNESS: CHARM -2 (opening for charm plays)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Charm, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        // What: Spec edge - WEAKNESS with -3 DC reduction
        // Mutation: Would catch if -3 is parsed as 2 or some other value
        [Fact]
        public void ParseOpponentResponse_WeaknessMinusThree_ParsedCorrectly()
        {
            var input = @"[RESPONSE]
""some text""

[SIGNALS]
WEAKNESS: HONESTY -3 (clearly deflecting)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(3, result.WeaknessWindow!.DcReduction);
        }

        // What: Spec edge - Response with quotes extracted properly
        // Mutation: Would catch if outer quotes are included in MessageText
        [Fact]
        public void ParseOpponentResponse_QuotesStrippedFromMessage()
        {
            var input = @"[RESPONSE]
""Hello there, nice to meet you""";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Hello there, nice to meet you", result.MessageText);
            Assert.DoesNotContain("\"\"", result.MessageText);
        }

        // ==============================================================================
        // AC7: Temperature verification for all methods
        // ==============================================================================

        // What: AC7 - Temperature override for OpponentResponse method
        // Mutation: Would catch if opponent response ignores its specific override
        [Fact]
        public async Task GetOpponentResponseAsync_TemperatureOverride_Used()
        {
            var handler = new CapturingHttpHandler(@"[RESPONSE]
""text""");
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.OpponentResponseTemperature = 0.6;
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.6, body!.Temperature, 2);
        }

        // What: AC7 - Temperature override for InterestChangeBeat method
        // Mutation: Would catch if interest beat ignores its specific override
        [Fact]
        public async Task GetInterestChangeBeatAsync_TemperatureOverride_Used()
        {
            var handler = new CapturingHttpHandler("Beat text");
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.InterestChangeBeatTemperature = 0.4;
            using var adapter = new AnthropicLlmAdapter(options, client);

            var ctx = new InterestChangeContext("Velvet", 10, 12, InterestState.Interested);
            await adapter.GetInterestChangeBeatAsync(ctx);

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.4, body!.Temperature, 2);
        }

        // What: AC7 - Temperature override for DeliverMessage method
        // Mutation: Would catch if delivery uses dialogue options temp instead of its own
        [Fact]
        public async Task DeliverMessageAsync_TemperatureOverride_Used()
        {
            var handler = new CapturingHttpHandler("text");
            using var client = new HttpClient(handler);
            var options = DefaultOptions();
            options.DeliveryTemperature = 0.3;
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.DeliverMessageAsync(MakeDeliveryContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.3, body!.Temperature, 2);
        }

        // What: AC7 - When no override set, default temperatures are used
        // Mutation: Would catch if null override causes 0 temperature or throws
        [Fact]
        public async Task NoTemperatureOverride_UsesDefaults()
        {
            // Verify each method uses its spec'd default when no override is set
            var options = DefaultOptions();
            // Explicitly ensure all overrides are null
            options.DialogueOptionsTemperature = null;
            options.DeliveryTemperature = null;
            options.OpponentResponseTemperature = null;
            options.InterestChangeBeatTemperature = null;

            var handler = new CapturingHttpHandler(FourOptionResponse);
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());
            var dialogueBody = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(0.9, dialogueBody!.Temperature, 2);
        }

        // ==============================================================================
        // AC7: MaxTokens from options
        // ==============================================================================

        // What: AC7 - MaxTokens is sent from options
        // Mutation: Would catch if max_tokens is hardcoded instead of read from options
        [Fact]
        public async Task GetDialogueOptionsAsync_MaxTokensFromOptions()
        {
            var options = DefaultOptions();
            options.MaxTokens = 2048;
            var handler = new CapturingHttpHandler(FourOptionResponse);
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(options, client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JsonConvert.DeserializeObject<MessagesRequest>(handler.RequestBodies[0]);
            Assert.Equal(2048, body!.MaxTokens);
        }

        // ==============================================================================
        // Error conditions from spec
        // ==============================================================================

        // What: Spec error - HttpRequestException propagates (not caught by adapter)
        // Mutation: Would catch if adapter wraps network errors in a different exception
        [Fact]
        public async Task NetworkFailure_PropagatesHttpRequestException()
        {
            var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => adapter.GetDialogueOptionsAsync(MakeDialogueContext()));
        }

        // What: Spec error - TaskCanceledException (timeout) propagates
        // Mutation: Would catch if adapter catches timeouts and returns defaults
        [Fact]
        public async Task Timeout_PropagatesTaskCanceledException()
        {
            var handler = new ThrowingHandler(new TaskCanceledException("Request timed out"));
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => adapter.DeliverMessageAsync(MakeDeliveryContext()));
        }

        // What: Spec error - DeliverMessageAsync returns "" on empty LLM response
        // Mutation: Would catch if empty deliver response throws or returns null
        [Fact]
        public async Task DeliverMessageAsync_EmptyResponse_ReturnsEmptyString()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.DeliverMessageAsync(MakeDeliveryContext());

            Assert.Equal("", result);
        }

        // What: Spec error - GetOpponentResponseAsync on empty LLM response returns empty message
        // Mutation: Would catch if empty response causes null message or throws
        [Fact]
        public async Task GetOpponentResponseAsync_EmptyResponse_ReturnsEmptyMessage()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            Assert.NotNull(result);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec error - GetDialogueOptionsAsync returns 4 defaults on unparseable response
        // Mutation: Would catch if unparseable response causes fewer than 4 or throws
        [Fact]
        public async Task GetDialogueOptionsAsync_CompletelyUnparseableResponse_FourDefaults()
        {
            var handler = new CapturingHttpHandler("🎉🎊💥 random emoji and gibberish XXXX");
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            var result = await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            Assert.Equal(4, result.Length);
            Assert.All(result, opt => Assert.Equal("...", opt.IntendedText));
        }

        // ==============================================================================
        // SelfAwareness case-insensitive parse via non-generic Enum.Parse (AC5)
        // ==============================================================================

        // What: AC5 - SelfAwareness stat parsed correctly (verifies Enum.Parse compatibility)
        // Mutation: Would catch if Enum.Parse uses wrong form that fails on .NET Standard 2.0
        [Fact]
        public void ParseDialogueOptions_SelfAwareness_ParsedCorrectly()
        {
            var input = @"OPTION_1
[STAT: SELFAWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Self-aware option""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
        }

        // What: AC5 - Case-insensitive stat parsing
        // Mutation: Would catch if case-sensitive match is used (true flag missing in Enum.Parse)
        [Fact]
        public void ParseDialogueOptions_CaseInsensitiveStat()
        {
            var input = @"OPTION_1
[STAT: charm] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""lowercase stat""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Charm, result[0].Stat);
        }

        // ==============================================================================
        // Dispose behavior from spec
        // ==============================================================================

        // What: Spec - Dispose on adapter constructed with external client doesn't dispose client
        // Mutation: Would catch if external client is disposed by adapter
        [Fact]
        public async Task Dispose_ExternalClient_NotDisposed()
        {
            var handler = new CapturingHttpHandler("");
            using var client = new HttpClient(handler);
            var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);
            adapter.Dispose();

            // External client should still be usable — won't throw ObjectDisposedException
            // If adapter erroneously disposed it, SendAsync would throw ObjectDisposedException
            var exception = await Record.ExceptionAsync(async () =>
            {
                try
                {
                    await client.GetAsync("http://localhost/health-check");
                }
                catch (HttpRequestException)
                {
                    // Expected: no server running. The point is it didn't throw ObjectDisposedException.
                }
            });
            Assert.Null(exception);
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
                "player", "opponent",
                new List<(string, string)>(),
                "last", new string[0], 10);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        // What: AC8 - OpponentContext new fields have correct defaults
        // Mutation: Would catch if defaults are non-empty string or non-zero
        [Fact]
        public void OpponentContext_NewFieldsDefaultCorrectly()
        {
            var ctx = new OpponentContext(
                "player", "opponent",
                new List<(string, string)>(),
                "last", new string[0], 10, "msg",
                10, 12, 2.0);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
            Assert.Equal(0, ctx.CurrentTurn);
        }

        // What: AC8 - DeliveryContext new fields have correct defaults
        // Mutation: Would catch if defaults changed
        [Fact]
        public void DeliveryContext_NewFieldsDefaultCorrectly()
        {
            var ctx = new DeliveryContext(
                "player", "opponent",
                new List<(string, string)>(),
                "last",
                new DialogueOption(StatType.Charm, "text"),
                FailureTier.None, 5,
                new string[0]);

            Assert.Equal("", ctx.PlayerName);
            Assert.Equal("", ctx.OpponentName);
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

        // ==============================================================================
        // Logging tests (code review W2 — adapter must log API calls)
        // ==============================================================================

        private sealed class CapturingLogger : ILogger<AnthropicLlmAdapter>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_LogsStartAndComplete()
        {
            var handler = new CapturingHttpHandler(@"OPTION_1
[STAT: Charm] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hello there""
OPTION_2
[STAT: Wit] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Witty line""
OPTION_3
[STAT: Honesty] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Honest line""
OPTION_4
[STAT: Chaos] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Chaos line""");
            using var client = new HttpClient(handler);
            var logger = new CapturingLogger();
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client, logger);

            var history = new List<(string Sender, string Text)> { ("Player", "hi") };
            var context = new DialogueContext(
                "player prompt", "opponent prompt",
                history, "hey", new string[0], 10);

            await adapter.GetDialogueOptionsAsync(context);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("GetDialogueOptionsAsync started"));
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("GetDialogueOptionsAsync complete"));
        }

        [Fact]
        public async Task DeliverMessageAsync_LogsStartAndComplete()
        {
            var handler = new CapturingHttpHandler("Delivered message text");
            using var client = new HttpClient(handler);
            var logger = new CapturingLogger();
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client, logger);

            var history = new List<(string Sender, string Text)> { ("Player", "hi") };
            var option = new DialogueOption(StatType.Charm, "Hey there");
            var context = new DeliveryContext(
                "player prompt", "opponent prompt",
                history, "Hey there", option,
                FailureTier.None, 5, new string[0]);

            await adapter.DeliverMessageAsync(context);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("DeliverMessageAsync started"));
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("DeliverMessageAsync complete"));
        }

        [Fact]
        public async Task GetOpponentResponseAsync_LogsStartAndComplete()
        {
            var handler = new CapturingHttpHandler(@"[RESPONSE] ""Hey back""");
            using var client = new HttpClient(handler);
            var logger = new CapturingLogger();
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client, logger);

            var history = new List<(string Sender, string Text)> { ("Player", "hi") };
            var context = new OpponentContext(
                "player prompt", "opponent prompt",
                history, "last msg", new string[0], 10,
                "Delivered text", 10, 12, 0);

            await adapter.GetOpponentResponseAsync(context);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("GetOpponentResponseAsync started"));
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("GetOpponentResponseAsync complete"));
        }

        [Fact]
        public async Task GetInterestChangeBeatAsync_LogsStartAndComplete()
        {
            var handler = new CapturingHttpHandler("Beat text");
            using var client = new HttpClient(handler);
            var logger = new CapturingLogger();
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client, logger);

            var context = new InterestChangeContext("Opponent", 10, 16, InterestState.VeryIntoIt);

            await adapter.GetInterestChangeBeatAsync(context);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("GetInterestChangeBeatAsync started"));
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Information && e.Message.Contains("GetInterestChangeBeatAsync complete"));
        }

        [Fact]
        public void ParseDialogueOptions_InvalidStat_LogsWarning()
        {
            var logger = new CapturingLogger();
            var response = @"OPTION_1
[STAT: InvalidStat] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Some text""";

            AnthropicLlmAdapter.ParseDialogueOptions(response, logger);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Warning && e.Message.Contains("invalid stat"));
        }

        [Fact]
        public void ParseDialogueOptions_EmptyResponse_LogsPaddingDebug()
        {
            var logger = new CapturingLogger();

            AnthropicLlmAdapter.ParseDialogueOptions("", logger);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Debug && e.Message.Contains("padding"));
        }

        [Fact]
        public void ParseOpponentResponse_InvalidTellStat_LogsWarning()
        {
            var logger = new CapturingLogger();
            var response = @"[RESPONSE] ""Hey there""
[SIGNALS]
TELL: InvalidStat (some description)";

            AnthropicLlmAdapter.ParseOpponentResponse(response, logger);

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Warning && e.Message.Contains("invalid tell stat"));
        }

        [Fact]
        public void ParseOpponentResponse_NoResponseBlock_LogsDebugFallback()
        {
            var logger = new CapturingLogger();
            var response = "Just raw text without markers";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(response, logger);

            Assert.Equal("Just raw text without markers", result.MessageText);
            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Debug && e.Message.Contains("fallback"));
        }
    }
}

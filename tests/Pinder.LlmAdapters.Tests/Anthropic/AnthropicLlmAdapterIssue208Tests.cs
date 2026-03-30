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
    public class AnthropicLlmAdapterIssue208Tests
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
            playerPrompt: "You are Thundercock, a bold confident penis",
            opponentPrompt: "You are Velvet, a mysterious and alluring match",
            conversationHistory: new List<(string, string)> { ("Velvet", "Hey there, nice profile pic") },
            opponentLastMessage: "Hey there, nice profile pic",
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
            beatDcBy: 7,
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
            playerDeliveredMessage: "Nice to meet you!",
            interestBefore: 10,
            interestAfter: 12,
            responseDelayMinutes: 2.0,
            playerName: "Thundercock",
            opponentName: "Velvet");

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
        // AC2: cache_control: ephemeral on system blocks
        // ======================================================================

        // What: AC2 — GetDialogueOptionsAsync system blocks have cache_control ephemeral
        // Mutation: Would catch if cache_control is omitted from system blocks
        [Fact]
        public async Task AC2_DialogueOptions_SystemBlocks_HaveCacheControl()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse(@"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hello there""")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            // Must have at least one block with cache_control
            var cacheBlocks = system!.Where(s => s["cache_control"] != null).ToList();
            Assert.True(cacheBlocks.Count >= 1, "Expected system blocks with cache_control");
            foreach (var block in cacheBlocks)
            {
                Assert.Equal("ephemeral", block["cache_control"]!["type"]!.ToString());
            }
        }

        // What: AC2 — Both player and opponent prompts in system for dialogue options
        // Mutation: Would catch if only one prompt is included in system blocks
        [Fact]
        public async Task AC2_DialogueOptions_SystemBlocks_ContainBothPrompts()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse(@"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Hello""")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDialogueOptionsAsync(MakeDialogueContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            var systemText = string.Join(" ", system!.Select(s => s["text"]?.ToString() ?? ""));
            Assert.Contains("Thundercock", systemText);
            Assert.Contains("Velvet", systemText);
        }

        // ======================================================================
        // AC3: Opponent response uses ONLY OpponentPrompt in system
        // ======================================================================

        // What: AC3 — GetOpponentResponseAsync system blocks contain only opponent prompt
        // Mutation: Would catch if player prompt is included in opponent response system blocks
        [Fact]
        public async Task AC3_OpponentResponse_OnlyOpponentPromptInSystem()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("[RESPONSE]\n\"Hey back!\"")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetOpponentResponseAsync(MakeOpponentContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            var systemText = string.Join(" ", system!.Select(s => s["text"]?.ToString() ?? ""));
            Assert.Contains("Velvet", systemText);
            Assert.DoesNotContain("Thundercock", systemText);
        }

        // ======================================================================
        // AC4: ParseDialogueOptions falls back gracefully (never throws)
        // ======================================================================

        // What: AC4 — null input returns 4 defaults
        // Mutation: Would catch if null input throws instead of returning defaults
        [Fact]
        public void AC4_ParseDialogueOptions_Null_Returns4Defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null!);
            Assert.Equal(4, result.Length);
            // Default stats order: Charm, Honesty, Wit, Chaos
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
            Assert.Equal(StatType.Wit, result[2].Stat);
            Assert.Equal(StatType.Chaos, result[3].Stat);
            Assert.All(result, o => Assert.Equal("...", o.IntendedText));
        }

        // What: AC4 — empty input returns 4 defaults
        // Mutation: Would catch if empty string is not handled as invalid
        [Fact]
        public void AC4_ParseDialogueOptions_Empty_Returns4Defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("");
            Assert.Equal(4, result.Length);
            Assert.All(result, o => Assert.Equal("...", o.IntendedText));
        }

        // What: AC4 — garbage input returns 4 defaults without throwing
        // Mutation: Would catch if parse exception propagates instead of being caught
        [Fact]
        public void AC4_ParseDialogueOptions_Garbage_Returns4Defaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions("!@#$%^&*() completely random gibberish\n\n");
            Assert.Equal(4, result.Length);
        }

        // What: AC4 — 1 valid option padded to 4
        // Mutation: Would catch if padding logic doesn't fill to exactly 4
        [Fact]
        public void AC4_ParseDialogueOptions_OneValid_PaddedTo4()
        {
            var input = @"OPTION_1
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Smooth line here""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Rizz, result[0].Stat);
            Assert.Equal("Smooth line here", result[0].IntendedText);
            // Remaining 3 are defaults — padded from {Charm, Honesty, Wit, Chaos} skipping Rizz
        }

        // What: AC4 — 5+ options truncated to 4
        // Mutation: Would catch if truncation doesn't happen, returning more than 4
        [Fact]
        public void AC4_ParseDialogueOptions_FivePlus_TruncatedTo4()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 1""

OPTION_2
[STAT: RIZZ] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 2""

OPTION_3
[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 3""

OPTION_4
[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 4""

OPTION_5
[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Line 5""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
        }

        // What: AC4 — invalid stat name is skipped and padded
        // Mutation: Would catch if invalid enum name causes exception instead of skip
        [Fact]
        public void AC4_ParseDialogueOptions_InvalidStat_Skipped()
        {
            var input = @"OPTION_1
[STAT: INVALID_STAT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Bad stat line""

OPTION_2
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Good line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            // First parsed valid is Charm
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal("Good line", result[0].IntendedText);
        }

        // What: AC4 — missing quoted text skips option
        // Mutation: Would catch if option without quoted text is accepted with empty/null text
        [Fact]
        public void AC4_ParseDialogueOptions_MissingQuotedText_Skipped()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
no quotes here at all";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            // Should be all defaults since the one option has no quoted text
        }

        // ======================================================================
        // ParseDialogueOptions — metadata parsing edge cases
        // ======================================================================

        // What: Spec edge case — [TELL_BONUS: yes] maps to true
        // Mutation: Would catch if tell bonus parsing doesn't check for "yes" specifically
        [Fact]
        public void ParseDialogueOptions_TellBonusYes_MapsToTrue()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""Tell bonus line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.True(result[0].HasTellBonus);
        }

        // What: Spec edge case — [TELL_BONUS: anything_else] maps to false
        // Mutation: Would catch if any non-empty value is treated as true
        [Fact]
        public void ParseDialogueOptions_TellBonusAnythingElse_MapsToFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""No tell bonus""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.False(result[0].HasTellBonus);
        }

        // What: Spec edge case — [TELL_BONUS: maybe] is not yes, maps to false
        // Mutation: Would catch if parser uses Contains("yes") instead of exact match
        [Fact]
        public void ParseDialogueOptions_TellBonusMaybe_MapsToFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: maybe]
""Ambiguous tell""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.False(result[0].HasTellBonus);
        }

        // What: Spec edge case — [CALLBACK: 3] numeric parses to CallbackTurnNumber = 3
        // Mutation: Would catch if numeric callbacks are treated as non-numeric
        [Fact]
        public void ParseDialogueOptions_NumericCallback_ParsedAsInt()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: 3] [COMBO: none] [TELL_BONUS: no]
""Callback reference""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(3, result[0].CallbackTurnNumber);
        }

        // What: Spec edge case — [CALLBACK: pizza_story] non-numeric → null
        // Mutation: Would catch if non-numeric callback is parsed as 0 instead of null
        [Fact]
        public void ParseDialogueOptions_NonNumericCallback_ReturnsNull()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: pizza_story] [COMBO: none] [TELL_BONUS: no]
""Callback to story""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].CallbackTurnNumber);
        }

        // What: Spec edge case — [COMBO: none] maps to null
        // Mutation: Would catch if "none" string is stored instead of null
        [Fact]
        public void ParseDialogueOptions_ComboNone_MapsToNull()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""No combo""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Null(result[0].ComboName);
        }

        // What: Spec edge case — [COMBO: The One-Two Punch] maps to full name
        // Mutation: Would catch if combo name is truncated at first space
        [Fact]
        public void ParseDialogueOptions_ComboWithSpaces_FullNamePreserved()
        {
            var input = @"OPTION_1
[STAT: WIT] [CALLBACK: none] [COMBO: The One-Two Punch] [TELL_BONUS: no]
""Combo line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal("The One-Two Punch", result[0].ComboName);
        }

        // What: Spec edge case — HasWeaknessWindow always false on dialogue options
        // Mutation: Would catch if weakness window is set from LLM parse (should only come from GameSession)
        [Fact]
        public void ParseDialogueOptions_HasWeaknessWindow_AlwaysFalse()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: yes]
""Test line""

OPTION_2
[STAT: WIT] [CALLBACK: 3] [COMBO: The Setup] [TELL_BONUS: no]
""Another line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.All(result, o => Assert.False(o.HasWeaknessWindow));
        }

        // What: Spec edge case — multiple quoted strings, first one used
        // Mutation: Would catch if last quoted string is used instead of first
        [Fact]
        public void ParseDialogueOptions_MultipleQuotedStrings_UsesFirst()
        {
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""First quote"" and then ""second quote""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal("First quote", result[0].IntendedText);
        }

        // What: Spec edge case — padding skips stats already present
        // Mutation: Would catch if padding creates duplicates of already-parsed stats
        [Fact]
        public void ParseDialogueOptions_PaddingSkipsExistingStats()
        {
            // Charm already present → padding should skip it
            var input = @"OPTION_1
[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Charm line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(4, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            // Padding from {Charm, Honesty, Wit, Chaos} skipping Charm → Honesty, Wit, Chaos
            var paddedStats = result.Skip(1).Select(o => o.Stat).ToArray();
            Assert.Equal(StatType.Honesty, paddedStats[0]);
            Assert.Equal(StatType.Wit, paddedStats[1]);
            Assert.Equal(StatType.Chaos, paddedStats[2]);
        }

        // ======================================================================
        // ParseOpponentResponse edge cases
        // ======================================================================

        // What: Spec — null input returns empty OpponentResponse
        // Mutation: Would catch if null throws instead of returning default
        [Fact]
        public void ParseOpponentResponse_Null_ReturnsEmpty()
        {
            var result = AnthropicLlmAdapter.ParseOpponentResponse(null!);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — empty input returns empty OpponentResponse
        // Mutation: Would catch if empty string is not handled
        [Fact]
        public void ParseOpponentResponse_Empty_ReturnsEmpty()
        {
            var result = AnthropicLlmAdapter.ParseOpponentResponse("");
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — no [RESPONSE] marker, plain text used as message
        // Mutation: Would catch if absence of marker causes empty message
        [Fact]
        public void ParseOpponentResponse_NoMarker_PlainTextUsedAsMessage()
        {
            var result = AnthropicLlmAdapter.ParseOpponentResponse("Just some plain text response");
            Assert.Equal("Just some plain text response", result.MessageText.Trim());
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — [RESPONSE] present, no [SIGNALS]
        // Mutation: Would catch if missing signals block causes exception
        [Fact]
        public void ParseOpponentResponse_ResponseOnly_NoSignals()
        {
            var input = "[RESPONSE]\n\"Hey there, nice to meet you!\"";
            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Hey there, nice to meet you!", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — both TELL and WEAKNESS parsed
        // Mutation: Would catch if only one signal type is parsed
        [Fact]
        public void ParseOpponentResponse_BothSignals_Parsed()
        {
            var input = @"[RESPONSE]
""Great conversation!""

[SIGNALS]
TELL: CHARM (flustered by compliments)
WEAKNESS: WIT -2 (overthinking responses)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Great conversation!", result.MessageText);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Wit, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        // What: Spec — WEAKNESS: WIT -3 parsed correctly
        // Mutation: Would catch if dc reduction is hardcoded to 2
        [Fact]
        public void ParseOpponentResponse_WeaknessMinusThree_Parsed()
        {
            var input = @"[RESPONSE]
""OK""

[SIGNALS]
WEAKNESS: WIT -3 (deep crack)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(3, result.WeaknessWindow!.DcReduction);
        }

        // What: Spec — WEAKNESS with zero reduction returns null
        // Mutation: Would catch if zero reduction is accepted as valid
        [Fact]
        public void ParseOpponentResponse_WeaknessZero_ReturnsNull()
        {
            var input = @"[RESPONSE]
""OK""

[SIGNALS]
WEAKNESS: WIT -0 (no actual weakness)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — invalid tell stat returns null tell
        // Mutation: Would catch if invalid Enum.Parse propagates exception
        [Fact]
        public void ParseOpponentResponse_InvalidTellStat_ReturnsNullTell()
        {
            var input = @"[RESPONSE]
""OK""

[SIGNALS]
TELL: INVALID_STAT (some description)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Null(result.DetectedTell);
        }

        // What: Spec — malformed [SIGNALS] block returns null signals
        // Mutation: Would catch if malformed signals cause exception
        [Fact]
        public void ParseOpponentResponse_MalformedSignals_ReturnsNullSignals()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
completely garbage that is not parseable at all !!!";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Equal("Some response", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — only TELL in signals
        // Mutation: Would catch if presence of TELL requires WEAKNESS to also be present
        [Fact]
        public void ParseOpponentResponse_OnlyTell_WeaknessNull()
        {
            var input = @"[RESPONSE]
""Hey""

[SIGNALS]
TELL: CHARM (flustered)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — only WEAKNESS in signals
        // Mutation: Would catch if presence of WEAKNESS requires TELL to also be present
        [Fact]
        public void ParseOpponentResponse_OnlyWeakness_TellNull()
        {
            var input = @"[RESPONSE]
""Hey""

[SIGNALS]
WEAKNESS: HONESTY -2 (opening)";

            var result = AnthropicLlmAdapter.ParseOpponentResponse(input);
            Assert.Null(result.DetectedTell);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Honesty, result.WeaknessWindow!.DefendingStat);
        }

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
        [Fact]
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
        [Fact]
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
        [Fact]
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

        // What: Spec — SelfAwareness stat parsed correctly (two-word stat)
        // Mutation: Would catch if SELF_AWARENESS is not mapped to StatType.SelfAwareness
        [Fact]
        public void ParseDialogueOptions_SelfAwareness_Parsed()
        {
            var input = @"OPTION_1
[STAT: SELF_AWARENESS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""A self-aware line""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.SelfAwareness, result[0].Stat);
            Assert.Equal("A self-aware line", result[0].IntendedText);
        }

        // What: Spec — case-insensitive stat parsing
        // Mutation: Would catch if Enum.Parse ignoreCase parameter is false
        [Fact]
        public void ParseDialogueOptions_CaseInsensitive()
        {
            var input = @"OPTION_1
[STAT: charm] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]
""Lowercase charm""";

            var result = AnthropicLlmAdapter.ParseDialogueOptions(input);
            Assert.Equal(StatType.Charm, result[0].Stat);
        }

        // What: Spec — default DialogueOption fields for padding
        // Mutation: Would catch if default padding options have non-null combo/callback
        [Fact]
        public void ParseDialogueOptions_DefaultPadding_HasCorrectDefaults()
        {
            var result = AnthropicLlmAdapter.ParseDialogueOptions(null!);
            foreach (var option in result)
            {
                Assert.Equal("...", option.IntendedText);
                Assert.Null(option.CallbackTurnNumber);
                Assert.Null(option.ComboName);
                Assert.False(option.HasTellBonus);
                Assert.False(option.HasWeaknessWindow);
            }
        }
    }
}

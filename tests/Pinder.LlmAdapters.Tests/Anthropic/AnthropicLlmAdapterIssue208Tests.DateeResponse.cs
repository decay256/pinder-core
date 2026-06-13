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
        // AC3: Datee response uses ONLY DateePrompt in system
        // ======================================================================

        // What: AC3 — GetDateeResponseAsync system blocks contain only datee prompt
        // Mutation: Would catch if player prompt is included in datee response system blocks
        [Fact]
        public async Task AC3_DateeResponse_OnlyDateePromptInSystem()
        {
            var handler = new MockHttpHandler
            {
                ResponseBody = MakeApiResponse("[RESPONSE]\n\"Hey back!\"")
            };
            using var client = new HttpClient(handler);
            using var adapter = new AnthropicLlmAdapter(DefaultOptions(), client);

            await adapter.GetDateeResponseAsync(MakeDateeContext());

            var body = JObject.Parse(handler.LastRequestBody!);
            var system = body["system"] as JArray;
            Assert.NotNull(system);
            var systemText = string.Join(" ", system!.Select(s => s["text"]?.ToString() ?? ""));
            Assert.Contains("Velvet", systemText);
            Assert.DoesNotContain("Thundercock", systemText);
        }

        // ======================================================================
        // ParseDateeResponse edge cases
        // ======================================================================

        // What: Spec — null input returns empty DateeResponse
        // Mutation: Would catch if null throws instead of returning default
        [Fact]
        public void ParseDateeResponse_Null_ReturnsEmpty()
        {
            var result = AnthropicLlmAdapter.ParseDateeResponse(null!);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — empty input returns empty DateeResponse
        // Mutation: Would catch if empty string is not handled
        [Fact]
        public void ParseDateeResponse_Empty_ReturnsEmpty()
        {
            var result = AnthropicLlmAdapter.ParseDateeResponse("");
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — no [RESPONSE] marker, plain text used as message
        // Mutation: Would catch if absence of marker causes empty message
        [Fact]
        public void ParseDateeResponse_NoMarker_PlainTextUsedAsMessage()
        {
            var result = AnthropicLlmAdapter.ParseDateeResponse("Just some plain text response");
            Assert.Equal("Just some plain text response", result.MessageText.Trim());
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — [RESPONSE] present, no [SIGNALS]
        // Mutation: Would catch if missing signals block causes exception
        [Fact]
        public void ParseDateeResponse_ResponseOnly_NoSignals()
        {
            var input = "[RESPONSE]\n\"Hey there, nice to meet you!\"";
            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Equal("Hey there, nice to meet you!", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — both TELL and WEAKNESS parsed
        // Mutation: Would catch if only one signal type is parsed
        [Fact]
        public void ParseDateeResponse_BothSignals_Parsed()
        {
            var input = @"[RESPONSE]
""Great conversation!""

[SIGNALS]
TELL: CHARM (flustered by compliments)
WEAKNESS: WIT -2 (overthinking responses)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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
        public void ParseDateeResponse_WeaknessMinusThree_Parsed()
        {
            var input = @"[RESPONSE]
""OK""

[SIGNALS]
WEAKNESS: WIT -3 (deep crack)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(3, result.WeaknessWindow!.DcReduction);
        }

        // What: Spec — WEAKNESS with zero reduction returns null
        // Mutation: Would catch if zero reduction is accepted as valid
        [Fact]
        public void ParseDateeResponse_WeaknessZero_ReturnsNull()
        {
            var input = @"[RESPONSE]
""OK""

[SIGNALS]
WEAKNESS: WIT -0 (no actual weakness)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — invalid tell stat returns null tell
        // Mutation: Would catch if invalid Enum.Parse propagates exception
        [Fact]
        public void ParseDateeResponse_InvalidTellStat_ReturnsNullTell()
        {
            var input = @"[RESPONSE]
""OK""

[SIGNALS]
TELL: INVALID_STAT (some description)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Null(result.DetectedTell);
        }

        // What: Spec — malformed [SIGNALS] block returns null signals
        // Mutation: Would catch if malformed signals cause exception
        [Fact]
        public void ParseDateeResponse_MalformedSignals_ReturnsNullSignals()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
completely garbage that is not parseable at all !!!";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Equal("Some response", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — only TELL in signals
        // Mutation: Would catch if presence of TELL requires WEAKNESS to also be present
        [Fact]
        public void ParseDateeResponse_OnlyTell_WeaknessNull()
        {
            var input = @"[RESPONSE]
""Hey""

[SIGNALS]
TELL: CHARM (flustered)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Equal(StatType.Charm, result.DetectedTell!.Stat);
            Assert.Null(result.WeaknessWindow);
        }

        // What: Spec — only WEAKNESS in signals
        // Mutation: Would catch if presence of WEAKNESS requires TELL to also be present
        [Fact]
        public void ParseDateeResponse_OnlyWeakness_TellNull()
        {
            var input = @"[RESPONSE]
""Hey""

[SIGNALS]
WEAKNESS: HONESTY -2 (opening)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Null(result.DetectedTell);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Honesty, result.WeaknessWindow!.DefendingStat);
        }
    }
}
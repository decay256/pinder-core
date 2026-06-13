using System;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterTests
    {
        [Fact]
        public void Null_input_returns_empty_response()
        {
            var result = AnthropicLlmAdapter.ParseDateeResponse(null);
            Assert.Equal("", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void Empty_input_returns_empty_response()
        {
            var result = AnthropicLlmAdapter.ParseDateeResponse("");
            Assert.Equal("", result.MessageText);
        }

        [Fact]
        public void Plain_text_without_markers_used_as_message()
        {
            var result = AnthropicLlmAdapter.ParseDateeResponse("Just a plain message");
            Assert.Equal("Just a plain message", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void Response_block_parsed_without_signals()
        {
            var input = @"[RESPONSE]
""Haha yeah, it's pretty wild out there.""";
            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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
TELL: CHARM (datee seems genuinely flustered by direct compliments)
WEAKNESS: WIT -2 (datee is clearly overthinking their responses)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public void SELF_AWARENESS_parsed_in_signals()
        {
            var input = @"[RESPONSE]
""Some response""

[SIGNALS]
TELL: SELF_AWARENESS (very self-reflective)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
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

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Equal("Some response", result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }
    }
}

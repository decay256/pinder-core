using System;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicLlmAdapterSpecTests
    {
        // ==============================================================================
        // ParseDateeResponse — additional edge cases
        // ==============================================================================

        // What: Spec edge - Tell description is extracted from parenthesized text
        // Mutation: Would catch if description is empty or includes the stat name
        [Fact]
        public void ParseDateeResponse_TellDescription_ExtractedFromParens()
        {
            var input = @"[RESPONSE]
""some text""

[SIGNALS]
TELL: CHARM (they blush easily at compliments)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.NotNull(result.DetectedTell);
            Assert.Contains("blush", result.DetectedTell!.Description);
        }

        // What: Spec edge - WEAKNESS with -2 DC reduction
        // Mutation: Would catch if DcReduction is always 3 or hardcoded
        [Fact]
        public void ParseDateeResponse_WeaknessMinusTwo_ParsedCorrectly()
        {
            var input = @"[RESPONSE]
""some text""

[SIGNALS]
WEAKNESS: CHARM -2 (opening for charm plays)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(StatType.Charm, result.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.WeaknessWindow.DcReduction);
        }

        // What: Spec edge - WEAKNESS with -3 DC reduction
        // Mutation: Would catch if -3 is parsed as 2 or some other value
        [Fact]
        public void ParseDateeResponse_WeaknessMinusThree_ParsedCorrectly()
        {
            var input = @"[RESPONSE]
""some text""

[SIGNALS]
WEAKNESS: HONESTY -3 (clearly deflecting)";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.NotNull(result.WeaknessWindow);
            Assert.Equal(3, result.WeaknessWindow!.DcReduction);
        }

        // What: Spec edge - Response with quotes extracted properly
        // Mutation: Would catch if outer quotes are included in MessageText
        [Fact]
        public void ParseDateeResponse_QuotesStrippedFromMessage()
        {
            var input = @"[RESPONSE]
""Hello there, nice to meet you""";

            var result = AnthropicLlmAdapter.ParseDateeResponse(input);
            Assert.Equal("Hello there, nice to meet you", result.MessageText);
            Assert.DoesNotContain("\"\"", result.MessageText);
        }
    }
}

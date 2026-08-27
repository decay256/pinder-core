using Newtonsoft.Json.Linq;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public sealed class DateeResponseParsersTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("plain visible reply")]
        [InlineData("visible reply\n[SIGNALS]\nTELL: HONESTY (legacy tell)")]
        public void ParseDateeResponseText_RejectsLegacyPlainTextWithoutFallback(string? raw)
        {
            LlmContractException ex = Assert.Throws<LlmContractException>(
                () => DateeResponseParsers.ParseDateeResponseText(raw));

            AssertLegacyRejection(ex, DateeResponseParsers.LegacyTextContractReason);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ParseDateeResponseTool_RejectsLegacyToolPayloadWithoutNullFallback(bool includeMessage)
        {
            var input = new JObject();
            if (includeMessage) input["message"] = "legacy tool reply";

            LlmContractException ex = Assert.Throws<LlmContractException>(
                () => DateeResponseParsers.ParseDateeResponseTool(input));

            AssertLegacyRejection(ex, DateeResponseParsers.LegacyToolContractReason);
        }

        [Fact]
        public void StripPersonaSelfTags_RemainsAvailableForCompatibilityCleanup()
        {
            Assert.Equal("visible) reply", DateeResponseParsers.StripPersonaSelfTags("visible /end) reply /rant"));
        }

        private static void AssertLegacyRejection(LlmContractException ex, string reason)
        {
            Assert.Equal(LlmPhase.DateeResponse, ex.Phase);
            Assert.Equal(reason, ex.Reason);
            Assert.Equal(DateeResponseParsers.LegacyParserName, ex.ParserName);
            Assert.Contains("datee_performance.v1", ex.Message);
            Assert.Equal(0, ex.SignalCount);
        }
    }
}

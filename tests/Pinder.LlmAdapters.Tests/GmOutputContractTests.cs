using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class GmOutputContractTests
    {
        [Fact]
        public void Emit_RejectsLegacySignalsContract()
        {
            var output = new GmTurnOutput("legacy reply");

            AssertLegacyRejection(Assert.Throws<LlmContractException>(() => GmOutputContract.Emit(output)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("plain visible reply")]
        [InlineData("visible reply\n[SIGNALS]\nTELL: HONESTY (legacy tell)")]
        public void Parse_RejectsLegacyTextWithoutMessageOnlyFallback(string? raw)
        {
            AssertLegacyRejection(Assert.Throws<LlmContractException>(() => GmOutputContract.Parse(raw)));
        }

        [Fact]
        public void ParseValidatedSignals_RejectsLegacySignalsWithoutFallback()
        {
            AssertLegacyRejection(Assert.Throws<LlmContractException>(
                () => GmOutputContract.ParseValidatedSignals(
                    "visible reply\n[SIGNALS]\nTELL: HONESTY (legacy tell)")));
        }

        [Fact]
        public void ValidateSignalsStrict_RejectsLegacySignalsContract()
        {
            LlmContractException ex = Assert.Throws<LlmContractException>(() =>
                GmOutputContract.ValidateSignalsStrict(
                    "visible reply\n[SIGNALS]\nTELL: HONESTY (legacy tell)",
                    out _));

            AssertLegacyRejection(ex);
        }

        private static void AssertLegacyRejection(LlmContractException ex)
        {
            Assert.Equal("gm_output", ex.Phase);
            Assert.Equal(GmOutputContract.LegacyContractReason, ex.Reason);
            Assert.Equal("GmOutputContract", ex.ParserName);
            Assert.Contains("datee_performance.v1", ex.Message);
            Assert.Equal(0, ex.SignalCount);
        }
    }
}

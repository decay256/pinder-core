using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalUsageCaptureTests
    {
        [Theory]
        [InlineData(0, AgentJournalUsageStatus.Unavailable, false)]
        [InlineData(1, AgentJournalUsageStatus.Incomplete, true)]
        [InlineData(2, AgentJournalUsageStatus.Incomplete, true)]
        public void Cumulative_delta_is_complete_only_for_exactly_one_provider_call(
            int callCount,
            AgentJournalUsageStatus expectedStatus,
            bool expectsUsage)
        {
            var provider = new MutableUsageProvider();
            TokenUsageMeasurement measurement = TokenUsageMeasurement.Start(provider);
            provider.Usage = new SessionTokenUsage
            {
                InputTokens = 17 * callCount,
                OutputTokens = 9 * callCount,
                CacheCreationInputTokens = 3 * callCount,
                CacheReadInputTokens = 4 * callCount,
                CallCount = callCount,
            };

            AgentJournalUsageCapture capture = AgentJournalUsageCapture.Capture(measurement);

            Assert.Equal(expectedStatus, capture.Status);
            if (callCount == 1)
            {
                Assert.Equal("legacy_cumulative_usage_delta", capture.UsageStatusReason);
            }
            Assert.Equal(expectsUsage, capture.Usage != null);
            if (expectsUsage)
            {
                Assert.Equal(17 * callCount, capture.Usage!.InputTokens);
                Assert.Equal(9 * callCount, capture.Usage.OutputTokens);
                Assert.Equal(26 * callCount, capture.Usage.TotalTokens);
                Assert.Equal(3 * callCount, capture.Usage.CacheCreationInputTokens);
                Assert.Equal(4 * callCount, capture.Usage.CacheReadInputTokens);
            }
        }

        private sealed class MutableUsageProvider : ITokenUsageProvider
        {
            public SessionTokenUsage Usage { get; set; } = new SessionTokenUsage();
            public SessionTokenUsage GetSessionUsage() => Usage;
        }
    }
}

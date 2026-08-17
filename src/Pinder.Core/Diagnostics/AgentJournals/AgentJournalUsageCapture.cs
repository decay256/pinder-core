using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    /// <summary>
    /// Classifies a cumulative provider usage delta for one durable invocation.
    /// Complete attribution requires exactly one measured provider call.
    /// </summary>
    public sealed class AgentJournalUsageCapture
    {
        public static readonly AgentJournalUsageCapture Unavailable =
            new AgentJournalUsageCapture(null, AgentJournalUsageStatus.Unavailable);

        private AgentJournalUsageCapture(
            AgentJournalUsage? usage,
            AgentJournalUsageStatus status)
        {
            Usage = usage;
            Status = status;
        }

        public AgentJournalUsage? Usage { get; }
        public AgentJournalUsageStatus Status { get; }

        public static AgentJournalUsageCapture Capture(TokenUsageMeasurement measurement)
        {
            if (measurement == null) throw new ArgumentNullException(nameof(measurement));

            SessionTokenUsage? measured = measurement.Complete();
            if (measured == null || measured.CallCount == 0)
            {
                return Unavailable;
            }

            var usage = new AgentJournalUsage(
                measured.InputTokens,
                measured.OutputTokens,
                measured.InputTokens + measured.OutputTokens,
                measured.CacheCreationInputTokens,
                measured.CacheReadInputTokens);
            return new AgentJournalUsageCapture(
                usage,
                measured.CallCount == 1
                    ? AgentJournalUsageStatus.Complete
                    : AgentJournalUsageStatus.Incomplete);
        }
    }
}

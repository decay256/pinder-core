using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    /// <summary>
    /// Classifies usage observed for one durable invocation.
    /// Attempt telemetry is canonical; cumulative deltas are legacy diagnostics only.
    /// </summary>
    public sealed class AgentJournalUsageCapture
    {
        public static readonly AgentJournalUsageCapture Unavailable =
            new AgentJournalUsageCapture(
                null,
                AgentJournalUsageStatus.Unavailable,
                "usage_unavailable");

        private AgentJournalUsageCapture(
            AgentJournalUsage? usage,
            AgentJournalUsageStatus status,
            string usageStatusReason,
            string? providerId = null,
            string? modelId = null,
            string? requestedProviderId = null,
            string? requestedModelId = null,
            long? observedStartedAtUnixMilliseconds = null,
            long? observedCompletedAtUnixMilliseconds = null,
            long? observedDurationMilliseconds = null,
            int? effectiveInputTokens = null,
            int? effectiveOutputTokens = null,
            int? effectiveTotalTokens = null,
            string? telemetryDiscrepancyCode = null)
        {
            if (string.IsNullOrWhiteSpace(usageStatusReason))
            {
                throw new ArgumentException("A machine-readable usage status reason is required.", nameof(usageStatusReason));
            }

            Usage = usage;
            Status = status;
            UsageStatusReason = usageStatusReason;
            ProviderId = providerId;
            ModelId = modelId;
            RequestedProviderId = requestedProviderId;
            RequestedModelId = requestedModelId;
            ObservedStartedAtUnixMilliseconds = observedStartedAtUnixMilliseconds;
            ObservedCompletedAtUnixMilliseconds = observedCompletedAtUnixMilliseconds;
            ObservedDurationMilliseconds = observedDurationMilliseconds;
            EffectiveInputTokens = effectiveInputTokens;
            EffectiveOutputTokens = effectiveOutputTokens;
            EffectiveTotalTokens = effectiveTotalTokens;
            TelemetryDiscrepancyCode = telemetryDiscrepancyCode;
        }

        public AgentJournalUsage? Usage { get; }
        public AgentJournalUsageStatus Status { get; }
        public string UsageStatusReason { get; }
        public string? ProviderId { get; }
        public string? ModelId { get; }
        public string? RequestedProviderId { get; }
        public string? RequestedModelId { get; }
        public long? ObservedStartedAtUnixMilliseconds { get; }
        public long? ObservedCompletedAtUnixMilliseconds { get; }
        public long? ObservedDurationMilliseconds { get; }
        public int? EffectiveInputTokens { get; }
        public int? EffectiveOutputTokens { get; }
        public int? EffectiveTotalTokens { get; }
        public string? TelemetryDiscrepancyCode { get; }

        public static AgentJournalUsageCapture Capture(IAgentJournalAttemptTelemetryScope telemetryScope)
        {
            if (telemetryScope == null) throw new ArgumentNullException(nameof(telemetryScope));
            return FromAttemptTelemetry(telemetryScope.Complete());
        }

        public static AgentJournalUsageCapture Capture(AgentJournalUsageMeasurementScope measurementScope)
        {
            if (measurementScope == null) throw new ArgumentNullException(nameof(measurementScope));
            return measurementScope.Complete();
        }

        public static AgentJournalUsageCapture FromAttemptTelemetry(AgentJournalAttemptTelemetry telemetry)
        {
            if (telemetry == null) throw new ArgumentNullException(nameof(telemetry));
            return new AgentJournalUsageCapture(
                telemetry.Usage,
                telemetry.Status,
                telemetry.UsageStatusReason,
                telemetry.ProviderId,
                telemetry.ModelId,
                telemetry.RequestedProviderId,
                telemetry.RequestedModelId,
                telemetry.ObservedStartedAtUnixMilliseconds,
                telemetry.ObservedCompletedAtUnixMilliseconds,
                telemetry.ObservedDurationMilliseconds,
                telemetry.EffectiveInputTokens,
                telemetry.EffectiveOutputTokens,
                telemetry.EffectiveTotalTokens,
                telemetry.TelemetryDiscrepancyCode);
        }

        public static AgentJournalUsageCapture FromResultUsage(
            AgentJournalUsage? usage,
            AgentJournalUsageStatus status,
            string usageStatusReason)
            => new AgentJournalUsageCapture(usage, status, usageStatusReason);

        public static AgentJournalUsageCapture Capture(TokenUsageMeasurement measurement)
        {
            if (measurement == null) throw new ArgumentNullException(nameof(measurement));

            SessionTokenUsage? measured = measurement.Complete();
            if (measured == null)
            {
                return new AgentJournalUsageCapture(
                    null,
                    AgentJournalUsageStatus.Unavailable,
                    "legacy_cumulative_usage_unavailable");
            }

            if (measured.CallCount == 0)
            {
                return new AgentJournalUsageCapture(
                    null,
                    AgentJournalUsageStatus.Unavailable,
                    "legacy_cumulative_zero_call_delta");
            }

            var usage = new AgentJournalUsage(
                measured.InputTokens,
                measured.OutputTokens,
                measured.InputTokens + measured.OutputTokens,
                measured.CacheCreationInputTokens,
                measured.CacheReadInputTokens);
            string reason = measured.CallCount == 1
                ? "legacy_cumulative_usage_delta"
                : "legacy_cumulative_multi_call_delta";
            return new AgentJournalUsageCapture(
                usage,
                AgentJournalUsageStatus.Incomplete,
                reason,
                effectiveInputTokens: measured.TotalBilledInput,
                effectiveOutputTokens: measured.OutputTokens,
                effectiveTotalTokens: measured.TotalBilledInput + measured.OutputTokens);
        }
    }
}

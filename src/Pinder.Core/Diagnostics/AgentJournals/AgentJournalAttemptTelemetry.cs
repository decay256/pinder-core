using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public interface IAgentJournalAttemptTelemetryProvider
    {
        IAgentJournalAttemptTelemetryScope StartAgentJournalAttemptTelemetry(string invocationId);
    }

    public interface IAgentJournalAttemptTelemetryScope : IDisposable
    {
        AgentJournalAttemptTelemetry Complete();
    }

    public sealed class AgentJournalAttemptTelemetry
    {
        public AgentJournalAttemptTelemetry(
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
    }

    public sealed class AgentJournalUsageMeasurementScope : IDisposable
    {
        private readonly IAgentJournalAttemptTelemetryScope? _attemptTelemetry;
        private readonly TokenUsageMeasurement? _usageMeasurement;
        private AgentJournalUsageCapture? _completedUsage;
        private bool _disposed;

        private AgentJournalUsageMeasurementScope(
            IAgentJournalAttemptTelemetryScope? attemptTelemetry,
            TokenUsageMeasurement? usageMeasurement)
        {
            _attemptTelemetry = attemptTelemetry;
            _usageMeasurement = usageMeasurement;
        }

        public static AgentJournalUsageMeasurementScope Unavailable()
            => new AgentJournalUsageMeasurementScope(null, null);

        public static AgentJournalUsageMeasurementScope Start(object? usageSource, string invocationId)
        {
            if (string.IsNullOrWhiteSpace(invocationId))
            {
                throw new ArgumentException("Agent journal invocation id is required.", nameof(invocationId));
            }

            if (usageSource is IAgentJournalAttemptTelemetryProvider telemetryProvider)
            {
                return new AgentJournalUsageMeasurementScope(
                    telemetryProvider.StartAgentJournalAttemptTelemetry(invocationId),
                    null);
            }

            return new AgentJournalUsageMeasurementScope(
                null,
                TokenUsageMeasurement.Start(usageSource));
        }

        public AgentJournalUsageCapture Complete()
        {
            if (_completedUsage != null)
            {
                return _completedUsage;
            }

            try
            {
                _completedUsage = _attemptTelemetry != null
                    ? AgentJournalUsageCapture.Capture(_attemptTelemetry)
                    : _usageMeasurement == null
                        ? AgentJournalUsageCapture.Unavailable
                        : AgentJournalUsageCapture.Capture(_usageMeasurement);
                return _completedUsage;
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _attemptTelemetry?.Dispose();
            _disposed = true;
        }
    }
}

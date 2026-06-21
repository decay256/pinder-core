using System;

namespace Pinder.LlmAdapters
{
    public enum OverlayOutcome
    {
        Degraded,
        Skipped
    }

    public sealed class OverlayDegradedEvent
    {
        public string OverlayType { get; }
        public string? Provider { get; }
        public string? Model { get; }
        public string Reason { get; }
        public OverlayOutcome Outcome { get; }
        public string? ErrorCode { get; }
        public string? SessionId { get; }
        public int? TurnId { get; }
        public string? TrapName { get; }

        public OverlayDegradedEvent(
            string overlayType,
            string? provider,
            string? model,
            string reason,
            OverlayOutcome outcome,
            string? errorCode = null,
            string? sessionId = null,
            int? turnId = null,
            string? trapName = null)
        {
            OverlayType = overlayType;
            Provider = provider;
            Model = model;
            Reason = reason;
            Outcome = outcome;
            ErrorCode = errorCode;
            SessionId = sessionId;
            TurnId = turnId;
            TrapName = trapName;
        }
    }
}

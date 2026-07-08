using System;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Optional callback configuration for structured provider-call telemetry.
    /// Hosts can attach session/turn/branch identifiers without exposing prompt
    /// text, response bodies, API keys, or provider payloads.
    /// </summary>
    public sealed class LlmCallTelemetryOptions
    {
        /// <summary>
        /// Process-wide fallback sink used when a transport is not given explicit
        /// telemetry options. Hosts that build transports in many places can set
        /// this once and still override it per transport when correlation context
        /// is available.
        /// </summary>
        public static Action<LlmCallTelemetryEvent>? DefaultOnEvent { get; set; }

        public Action<LlmCallTelemetryEvent>? OnEvent { get; }
        public string? SessionId { get; }
        public int? Turn { get; }
        public string? Branch { get; }
        public string? Option { get; }

        public LlmCallTelemetryOptions(
            Action<LlmCallTelemetryEvent>? onEvent,
            string? sessionId = null,
            int? turn = null,
            string? branch = null,
            string? option = null)
        {
            OnEvent = onEvent;
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
            Turn = turn;
            Branch = string.IsNullOrWhiteSpace(branch) ? null : branch;
            Option = string.IsNullOrWhiteSpace(option) ? null : option;
        }
    }

    public sealed class LlmCallTelemetryEvent
    {
        public string EventName { get; }
        public string Provider { get; }
        public string? Model { get; }
        public string? Phase { get; }
        public string? SessionId { get; }
        public int? Turn { get; }
        public string? Branch { get; }
        public string? Option { get; }
        public int? StatusCode { get; }
        public int Attempt { get; }
        public TimeSpan? RetryAfter { get; }
        public TimeSpan Duration { get; }
        public string? ExceptionType { get; }

        public LlmCallTelemetryEvent(
            string eventName,
            string provider,
            string? model,
            string? phase,
            string? sessionId,
            int? turn,
            string? branch,
            string? option,
            int? statusCode,
            int attempt,
            TimeSpan? retryAfter,
            TimeSpan duration,
            string? exceptionType)
        {
            EventName = eventName ?? string.Empty;
            Provider = provider ?? string.Empty;
            Model = model;
            Phase = phase;
            SessionId = sessionId;
            Turn = turn;
            Branch = branch;
            Option = option;
            StatusCode = statusCode;
            Attempt = attempt;
            RetryAfter = retryAfter;
            Duration = duration;
            ExceptionType = exceptionType;
        }
    }

    public static class LlmCallTelemetryEventNames
    {
        public const string Started = "llm.call.started";
        public const string Completed = "llm.call.completed";
        public const string Retry = "llm.call.retry";
        public const string Failed = "llm.call.failed";
    }

    internal static class LlmCallTelemetry
    {
        public static void Emit(
            LlmCallTelemetryOptions? options,
            string eventName,
            string provider,
            string? model,
            string? phase,
            int? statusCode,
            int attempt,
            TimeSpan? retryAfter,
            TimeSpan duration,
            string? exceptionType = null)
        {
            var sink = options?.OnEvent ?? LlmCallTelemetryOptions.DefaultOnEvent;
            if (sink == null)
            {
                return;
            }

            try
            {
                sink(new LlmCallTelemetryEvent(
                    eventName,
                    provider,
                    model,
                    string.IsNullOrWhiteSpace(phase) ? null : phase,
                    options?.SessionId,
                    options?.Turn,
                    options?.Branch,
                    options?.Option,
                    statusCode,
                    attempt,
                    retryAfter,
                    duration,
                    exceptionType));
            }
            catch
            {
                // Telemetry sinks must never alter model-call control flow.
            }
        }
    }
}

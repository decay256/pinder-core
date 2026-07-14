using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Severity for host-observable operational diagnostics emitted by reusable libraries.
    /// </summary>
    public enum OperationalDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum OperationalDiagnosticLifecycle
    {
        None,
        Start,
        Phase,
        Terminal
    }

    public enum OperationalDiagnosticOutcome
    {
        None,
        Succeeded,
        Failed,
        Cancelled,
        Degraded,
        Skipped
    }

    public enum OperationalDiagnosticFailureClassification
    {
        None,
        Transient,
        Permanent,
        Cancelled,
        Degraded
    }

    public enum OperationalDiagnosticBranchStatus
    {
        None,
        Started,
        Adopted,
        Discarded,
        Rerun
    }

    public static class OperationalDiagnosticOperationKind
    {
        public const string SetupSynthesis = "setup_synthesis";
        public const string DialogueOptions = "dialogue_options";
        public const string DateeResponse = "datee_response";
        public const string Delivery = "delivery";
        public const string Overlay = "overlay";
        public const string SpeculativeBranch = "speculative_branch";
    }

    public static class OperationalDiagnosticPhaseCode
    {
        public const string Start = "start";
        public const string BuildPrompt = "build_prompt";
        public const string TransportSend = "transport_send";
        public const string Parse = "parse";
        public const string ContractViolation = "contract_violation";
        public const string DeterministicDelivery = "deterministic_delivery";
        public const string Completed = "completed";
    }

    /// <summary>
    /// Typed diagnostic event for library code that needs host-controlled logging.
    /// </summary>
    public sealed class OperationalDiagnosticEvent
    {
        public string Source { get; }
        public string EventName { get; }
        public OperationalDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public string OperationKind { get; }
        public string PhaseCode { get; }
        public OperationalDiagnosticLifecycle Lifecycle { get; }
        public OperationalDiagnosticOutcome Outcome { get; }
        public OperationalDiagnosticFailureClassification FailureClassification { get; }
        public string? CorrelationId { get; }
        public string? CallId { get; }
        public IReadOnlyDictionary<string, string> CorrelationHints { get; }
        public string? BranchId { get; }
        public OperationalDiagnosticBranchStatus BranchStatus { get; }

        public OperationalDiagnosticEvent(
            string source,
            string eventName,
            OperationalDiagnosticSeverity severity,
            string message,
            Exception? exception = null,
            string? operationKind = null,
            string? phaseCode = null,
            OperationalDiagnosticLifecycle lifecycle = OperationalDiagnosticLifecycle.None,
            OperationalDiagnosticOutcome outcome = OperationalDiagnosticOutcome.None,
            OperationalDiagnosticFailureClassification failureClassification = OperationalDiagnosticFailureClassification.None,
            string? correlationId = null,
            string? callId = null,
            IReadOnlyDictionary<string, string>? correlationHints = null,
            string? branchId = null,
            OperationalDiagnosticBranchStatus branchStatus = OperationalDiagnosticBranchStatus.None)
        {
            Source = source ?? string.Empty;
            EventName = eventName ?? string.Empty;
            Severity = severity;
            Message = message ?? string.Empty;
            Exception = exception;
            OperationKind = operationKind ?? string.Empty;
            PhaseCode = phaseCode ?? string.Empty;
            Lifecycle = lifecycle;
            Outcome = outcome;
            FailureClassification = failureClassification;
            CorrelationId = correlationId;
            CallId = callId;
            CorrelationHints = correlationHints ?? EmptyCorrelationHints.Instance;
            BranchId = branchId;
            BranchStatus = branchStatus;
        }
    }

    /// <summary>
    /// No-op-by-default emitter for optional host diagnostic callbacks.
    /// </summary>
    public static class OperationalDiagnostics
    {
        public static string CreateCallId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static OperationalDiagnosticFailureClassification ClassifyException(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return OperationalDiagnosticFailureClassification.Cancelled;
            }

            if (exception is TimeoutException)
            {
                return OperationalDiagnosticFailureClassification.Transient;
            }

            if (exception is LlmTransportException transportException)
            {
                return transportException.FailureKind == LlmFailureKind.Network
                    || transportException.FailureKind == LlmFailureKind.RateLimited
                    ? OperationalDiagnosticFailureClassification.Transient
                    : OperationalDiagnosticFailureClassification.Permanent;
            }

            return OperationalDiagnosticFailureClassification.Permanent;
        }

        public static void Emit(Action<OperationalDiagnosticEvent>? sink, OperationalDiagnosticEvent diagnostic)
        {
            if (sink == null)
            {
                return;
            }

            try
            {
                sink(diagnostic);
            }
            catch
            {
                // Diagnostic callbacks must never alter gameplay/library control flow.
            }
        }
    }

    internal sealed class EmptyCorrelationHints : IReadOnlyDictionary<string, string>
    {
        public static readonly EmptyCorrelationHints Instance = new EmptyCorrelationHints();

        private EmptyCorrelationHints()
        {
        }

        public int Count => 0;

        public IEnumerable<string> Keys => Array.Empty<string>();

        public IEnumerable<string> Values => Array.Empty<string>();

        public string this[string key] => throw new KeyNotFoundException();

        public bool ContainsKey(string key)
        {
            return false;
        }

        public bool TryGetValue(string key, out string value)
        {
            value = string.Empty;
            return false;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>()).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

using System;

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

        public OperationalDiagnosticEvent(
            string source,
            string eventName,
            OperationalDiagnosticSeverity severity,
            string message,
            Exception? exception = null)
        {
            Source = source ?? string.Empty;
            EventName = eventName ?? string.Empty;
            Severity = severity;
            Message = message ?? string.Empty;
            Exception = exception;
        }
    }

    /// <summary>
    /// No-op-by-default emitter for optional host diagnostic callbacks.
    /// </summary>
    public static class OperationalDiagnostics
    {
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
}

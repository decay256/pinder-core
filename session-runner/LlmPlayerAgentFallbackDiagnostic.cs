using System;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Private diagnostic details for an LLM player-agent fallback.
    /// Kept out of user-facing reasoning while remaining available to tests and hosts.
    /// </summary>
    internal sealed class LlmPlayerAgentFallbackDiagnostic
    {
        public LlmPlayerAgentFallbackDiagnostic(
            string publicReason,
            string model,
            int? turnNumber,
            int? contextTurnNumber,
            string playerName,
            string dateeName,
            Exception? exception)
        {
            PublicReason = publicReason ?? string.Empty;
            Model = model ?? string.Empty;
            TurnNumber = turnNumber;
            ContextTurnNumber = contextTurnNumber;
            PlayerName = playerName ?? string.Empty;
            DateeName = dateeName ?? string.Empty;
            Exception = exception;
            ExceptionType = exception?.GetType().FullName ?? string.Empty;
            ExceptionMessage = exception?.Message ?? string.Empty;
            StackTrace = exception?.ToString() ?? string.Empty;
            Cause = BuildCause(exception);
        }

        public string PublicReason { get; }
        public string Model { get; }
        public int? TurnNumber { get; }
        public int? ContextTurnNumber { get; }
        public string PlayerName { get; }
        public string DateeName { get; }
        public Exception? Exception { get; }
        public string ExceptionType { get; }
        public string ExceptionMessage { get; }
        public string StackTrace { get; }
        public string Cause { get; }

        private static string BuildCause(Exception? exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            Exception cause = exception;
            while (cause.InnerException != null)
            {
                cause = cause.InnerException;
            }

            return cause.GetType().FullName + ": " + cause.Message;
        }
    }
}

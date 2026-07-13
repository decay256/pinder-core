using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Raised when a live turn needs cognitive subtext but the datee profile is
    /// missing the diagnosis fields required to derive it.
    /// </summary>
    public sealed class CognitiveSubtextException : InvalidOperationException
    {
        public CognitiveSubtextException(
            string missingField,
            string? dateeName = null,
            int? turnNumber = null,
            string reason = "missing_diagnosis_field")
            : base(BuildMessage(missingField, dateeName, turnNumber, reason))
        {
            MissingField = missingField ?? string.Empty;
            DateeName = dateeName ?? string.Empty;
            TurnNumber = turnNumber;
            Reason = reason ?? "missing_diagnosis_field";
        }

        public string MissingField { get; }

        public string DateeName { get; }

        public int? TurnNumber { get; }

        public string Reason { get; }

        private static string BuildMessage(
            string missingField,
            string? dateeName,
            int? turnNumber,
            string reason)
        {
            var message = $"Datee psychiatric_diagnosis is missing required field '{missingField}'.";
            if (!string.IsNullOrWhiteSpace(dateeName))
                message += $" datee='{dateeName}'";
            if (turnNumber.HasValue)
                message += $" turn={turnNumber.Value}";
            if (!string.IsNullOrWhiteSpace(reason))
                message += $" reason={reason}";
            return message;
        }
    }
}

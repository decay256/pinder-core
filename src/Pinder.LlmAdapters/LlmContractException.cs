using System;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Violation metadata containing privacy-safe structured information about a contract failure.
    /// </summary>
    public sealed class LlmContractViolation
    {
        public string Phase { get; }
        public string Reason { get; }
        public string? Provider { get; }
        public string? Model { get; }
        public string? ParserName { get; }
        public int? ExpectedOptionCount { get; }
        public int? ParsedOptionCount { get; }
        public int? OptionCount { get; }
        public int? SignalCount { get; }
        public string? SessionId { get; }
        public int? TurnId { get; }

        public LlmContractViolation(
            string phase,
            string reason,
            string? provider = null,
            string? model = null,
            string? parserName = null,
            int? expectedOptionCount = null,
            int? parsedOptionCount = null,
            int? optionCount = null,
            int? signalCount = null,
            string? sessionId = null,
            int? turnId = null)
        {
            Phase = phase;
            Reason = reason;
            Provider = provider;
            Model = model;
            ParserName = parserName;
            ExpectedOptionCount = expectedOptionCount;
            ParsedOptionCount = parsedOptionCount;
            OptionCount = optionCount;
            SignalCount = signalCount;
            SessionId = sessionId;
            TurnId = turnId;
        }
    }

    /// <summary>
    /// Exception thrown when the LLM provider fails to fulfill the contract required for gameplay.
    /// </summary>
    public sealed class LlmContractException : Exception
    {
        public string Phase { get; }
        public string Reason { get; }
        public string? Provider { get; }
        public string? Model { get; }
        public string? ParserName { get; }
        public int? ExpectedOptionCount { get; }
        public int? ParsedOptionCount { get; }
        public int? OptionCount { get; }
        public int? SignalCount { get; }
        public string? SessionId { get; }
        public int? TurnId { get; }

        public LlmContractException(
            string phase,
            string reason,
            string message,
            string? provider = null,
            string? model = null,
            string? parserName = null,
            int? expectedOptionCount = null,
            int? parsedOptionCount = null,
            int? optionCount = null,
            int? signalCount = null,
            string? sessionId = null,
            int? turnId = null) : base(message)
        {
            Phase = phase;
            Reason = reason;
            Provider = provider;
            Model = model;
            ParserName = parserName;
            ExpectedOptionCount = expectedOptionCount;
            ParsedOptionCount = parsedOptionCount;
            OptionCount = optionCount;
            SignalCount = signalCount;
            SessionId = sessionId;
            TurnId = turnId;
        }
    }
}

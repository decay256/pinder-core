using System;
using Xunit;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    public class ExceptionToStringTests
    {
        [Fact]
        public void LlmContractException_ToString_ContainsCustomDiagnosticParameters()
        {
            var exception = new LlmContractException(
                phase: "parsing",
                reason: "invalid_option_count",
                message: "A contract exception occurred",
                provider: "Anthropic",
                model: "claude-3-opus",
                parserName: "DialogueOptionParsers",
                expectedOptionCount: 4,
                parsedOptionCount: 2,
                optionCount: 3,
                signalCount: 1,
                sessionId: "session-abc-123",
                turnId: 4
            );

            var result = exception.ToString();

            Assert.Contains("[ContractViolationDetails]", result);
            Assert.Contains("Phase: parsing", result);
            Assert.Contains("Reason: invalid_option_count", result);
            Assert.Contains("Provider: Anthropic", result);
            Assert.Contains("Model: claude-3-opus", result);
            Assert.Contains("Parser: DialogueOptionParsers", result);
            Assert.Contains("ExpectedOptions: 4", result);
            Assert.Contains("ParsedOptions: 2", result);
            Assert.Contains("Options: 3", result);
            Assert.Contains("Signals: 1", result);
            Assert.Contains("SessionId: session-abc-123", result);
            Assert.Contains("TurnId: 4", result);
            Assert.Contains("A contract exception occurred", result);
        }
    }
}

using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalStateValidationTests
    {
        [Fact]
        public void InvocationValidation_RejectsEveryInvalidNestedEnum()
        {
            var source = new AgentJournalSourceIdentity(
                (AgentJournalSourceKind)999,
                "prompt.catalog",
                "prompt.key");
            var range = new AgentJournalProvenanceRange(
                "doc.system",
                0,
                3,
                (AgentJournalRangeKind)999,
                (AgentJournalRedactionClass)999,
                source);
            var document = new AgentJournalInputDocument(
                "doc.system",
                (AgentJournalInputRole)999,
                "abc",
                new[] { range });

            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(documents: new[] { document }));

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidInputRole);
            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidSourceKind);
            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidRangeKind);
            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidRedactionClass);
        }

        [Theory]
        [InlineData(AgentJournalTerminalStatus.Succeeded, null, null, null)]
        [InlineData(AgentJournalTerminalStatus.Succeeded, "output", null, "provider_error")]
        [InlineData(AgentJournalTerminalStatus.Failed, "output", null, "provider_error")]
        [InlineData(AgentJournalTerminalStatus.Failed, null, null, null)]
        [InlineData(AgentJournalTerminalStatus.Failed, null, "rejected", "provider_error")]
        [InlineData(AgentJournalTerminalStatus.Cancelled, "output", null, null)]
        [InlineData(AgentJournalTerminalStatus.Cancelled, null, "rejected", null)]
        [InlineData(AgentJournalTerminalStatus.Rejected, "output", "rejected", null)]
        [InlineData(AgentJournalTerminalStatus.Rejected, null, null, null)]
        [InlineData(AgentJournalTerminalStatus.Rejected, null, "rejected", "provider_error")]
        public void ResultValidation_RejectsInvalidTerminalStateCombinations(
            AgentJournalTerminalStatus status,
            string? output,
            string? validationCode,
            string? errorCode)
        {
            var record = new LlmResultRecord(
                AgentJournalTestRecords.Correlation(),
                status,
                output,
                null,
                validationCode,
                errorCode);

            var result = AgentJournalValidator.Validate(record);

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidStatusTransition);
        }

        [Theory]
        [InlineData(AgentJournalTerminalStatus.Succeeded, "output", "accepted", null)]
        [InlineData(AgentJournalTerminalStatus.Failed, null, null, "provider_error")]
        [InlineData(AgentJournalTerminalStatus.Cancelled, null, null, "cancelled")]
        [InlineData(AgentJournalTerminalStatus.Rejected, null, "schema_rejected", null)]
        public void ResultValidation_AcceptsAllowedTerminalStateCombinations(
            AgentJournalTerminalStatus status,
            string? output,
            string? validationCode,
            string? errorCode)
        {
            var record = new LlmResultRecord(
                AgentJournalTestRecords.Correlation(),
                status,
                output,
                null,
                validationCode,
                errorCode);

            var result = AgentJournalValidator.Validate(record);

            Assert.True(result.IsValid);
        }
    }
}

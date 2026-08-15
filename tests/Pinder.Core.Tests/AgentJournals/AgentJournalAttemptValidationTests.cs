using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalAttemptValidationTests
    {
        [Fact]
        public void InvocationValidation_RejectsMissingAttemptId()
        {
            var correlation = new AgentJournalCorrelationIds("game", "session", "invocation", "operation", 1);
            var record = new LlmInvocationRecord(
                correlation,
                "model",
                "phase",
                new[] { AgentJournalTestRecords.Document("doc", "abc", AgentJournalTestRecords.Range("doc", 0, 3)) });

            var result = AgentJournalValidator.Validate(record);

            Assert.Contains(result.Errors, error =>
                error.Code == AgentJournalValidator.MissingId
                && error.Path == "$.correlation.attempt_id");
        }
    }
}

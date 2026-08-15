using System.Linq;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalUtf16BoundaryValidationTests
    {
        [Fact]
        public void InvocationValidation_RejectsBoundariesInsideSurrogatePair()
        {
            string text = "A\U0001F600B";
            var document = AgentJournalTestRecords.Document(
                "doc.system",
                text,
                AgentJournalTestRecords.Range("doc.system", 0, 2),
                AgentJournalTestRecords.Range("doc.system", 2, 4));

            AgentJournalValidationResult result = AgentJournalValidator.Validate(
                AgentJournalTestRecords.Invocation(documents: new[] { document }));

            Assert.Equal(2, result.Errors.Count(error => error.Code == AgentJournalValidator.SurrogateSplitRange));
            Assert.Contains(result.Errors, error => error.Path.EndsWith("[0].end_utf16"));
            Assert.Contains(result.Errors, error => error.Path.EndsWith("[1].start_utf16"));
        }

        [Fact]
        public void InvocationValidation_AcceptsBoundariesAroundWholeSurrogatePair()
        {
            string text = "A\U0001F600B";
            var document = AgentJournalTestRecords.Document(
                "doc.system",
                text,
                AgentJournalTestRecords.Range("doc.system", 0, 1),
                AgentJournalTestRecords.Range("doc.system", 1, 3),
                AgentJournalTestRecords.Range("doc.system", 3, 4));

            AgentJournalValidationResult result = AgentJournalValidator.Validate(
                AgentJournalTestRecords.Invocation(documents: new[] { document }));

            Assert.DoesNotContain(result.Errors, error => error.Code == AgentJournalValidator.SurrogateSplitRange);
            Assert.True(result.IsValid);
        }
    }
}

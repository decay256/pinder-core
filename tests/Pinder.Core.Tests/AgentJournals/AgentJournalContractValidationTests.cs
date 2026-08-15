using System;
using System.Linq;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class AgentJournalContractValidationTests
    {
        [Fact]
        public void InvocationValidation_AcceptsCanonicalRecord()
        {
            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation());

            Assert.True(result.IsValid, string.Join(",", result.Errors.Select(error => error.Code)));
        }

        [Theory]
        [InlineData("", AgentJournalValidator.MissingId)]
        [InlineData("   ", AgentJournalValidator.MissingId)]
        public void InvocationValidation_RejectsMissingIds(string invocationId, string expected)
        {
            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(invocationId: invocationId));

            Assert.Contains(result.Errors, error => error.Code == expected && error.Path == "$.correlation.invocation_id");
        }

        [Fact]
        public void InvocationValidation_RejectsDuplicateDocumentIds()
        {
            var document = AgentJournalTestRecords.Document("doc.system", "abc", AgentJournalTestRecords.Range("doc.system", 0, 3));
            var record = AgentJournalTestRecords.Invocation(documents: new[] { document, document });

            var result = AgentJournalValidator.Validate(record);

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.DuplicateId);
        }

        [Fact]
        public void InvocationValidation_RejectsInvalidAttemptOrdinal()
        {
            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(attemptOrdinal: 0));

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidAttemptOrdinal);
        }

        [Theory]
        [InlineData(1, 1, AgentJournalValidator.ZeroLengthRange)]
        [InlineData(-1, 1, AgentJournalValidator.OutOfBoundsRange)]
        [InlineData(0, 4, AgentJournalValidator.OutOfBoundsRange)]
        public void InvocationValidation_RejectsInvalidUtf16Ranges(int start, int end, string expected)
        {
            var document = AgentJournalTestRecords.Document("doc.system", "abc", AgentJournalTestRecords.Range("doc.system", start, end));
            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(documents: new[] { document }));

            Assert.Contains(result.Errors, error => error.Code == expected);
        }

        [Fact]
        public void InvocationValidation_RejectsOverlappingRanges()
        {
            var document = AgentJournalTestRecords.Document(
                "doc.system",
                "abcd",
                AgentJournalTestRecords.Range("doc.system", 0, 3),
                AgentJournalTestRecords.Range("doc.system", 2, 4));

            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(documents: new[] { document }));

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.OverlappingRange);
            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.UnorderedRange);
        }

        [Fact]
        public void InvocationValidation_RejectsDocumentMismatchAndForbiddenLinks()
        {
            var source = new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                "/etc/passwd",
                "prompt.key",
                editorTargetId: "https://example.invalid/secret");
            var document = AgentJournalTestRecords.Document(
                "doc.system",
                "abc",
                new AgentJournalProvenanceRange(
                    "other",
                    0,
                    3,
                    AgentJournalRangeKind.Configured,
                    AgentJournalRedactionClass.SafeMetadata,
                    source));

            var result = AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(documents: new[] { document }));

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.RangeDocumentMismatch);
            Assert.Equal(2, result.Errors.Count(error => error.Code == AgentJournalValidator.ForbiddenSourceLink));
        }

        [Fact]
        public void ResultValidation_RejectsInvalidStatusAndNegativeUsage()
        {
            var record = new LlmResultRecord(
                AgentJournalTestRecords.Correlation(),
                (AgentJournalTerminalStatus)999,
                "nope",
                new AgentJournalUsage(-1, 2, -3));

            var result = AgentJournalValidator.Validate(record);

            Assert.Contains(result.Errors, error => error.Code == AgentJournalValidator.InvalidTerminalStatus);
            Assert.Equal(2, result.Errors.Count(error => error.Code == AgentJournalValidator.NegativeUsage));
        }

        [Fact]
        public void MessageLinkValidation_RejectsMissingIds()
        {
            var result = AgentJournalValidator.Validate(new MessageLinkRecord("", "", ""));

            Assert.Equal(3, result.Errors.Count(error => error.Code == AgentJournalValidator.MissingId));
        }
    }
}


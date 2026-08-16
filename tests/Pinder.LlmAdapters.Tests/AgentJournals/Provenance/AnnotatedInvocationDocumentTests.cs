using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Provenance
{
    public sealed class AnnotatedInvocationDocumentTests
    {
        private static readonly AgentJournalSourceIdentity TemplateSource =
            new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Configuration,
                "prompt.catalog",
                "dialogue.template",
                revision: "rev-template");

        private static readonly AgentJournalSourceIdentity NameSource =
            new AgentJournalSourceIdentity(
                AgentJournalSourceKind.Catalog,
                "character.catalog",
                "datee.name",
                contentHash: "sha256:name");

        [Fact]
        public void AC1_BuilderAppendSubstitutionAndTrim_PreserveExactTextAndUtf16Offsets()
        {
            AnnotatedInvocationDocument name = new AnnotatedInvocationDocumentBuilder()
                .AppendConfigured("\U0001F600Ada", NameSource)
                .Build("doc.name", AgentJournalInputRole.User, "fragment");
            var substitutions = new Dictionary<string, AnnotatedInvocationDocument>(StringComparer.Ordinal)
            {
                ["name"] = name,
            };

            AnnotatedInvocationDocument document = new AnnotatedInvocationDocumentBuilder()
                .AppendTemplate("Hello {name}. Again {name}.  ", substitutions, TemplateSource)
                .Trim()
                .Build("doc.user", AgentJournalInputRole.User, "dialogue-options");

            Assert.Equal("Hello \U0001F600Ada. Again \U0001F600Ada.", document.Text);
            Assert.True(document.ValidationResult.IsValid);
            Assert.Equal(new[] { 0, 6, 11, 19, 24 }, document.Ranges.Select(range => range.StartUtf16).ToArray());
            Assert.Equal(new[] { 6, 11, 19, 24, 25 }, document.Ranges.Select(range => range.EndUtf16).ToArray());
            Assert.Equal(new[] { "dialogue.template", "datee.name", "dialogue.template", "datee.name", "dialogue.template" }, document.Ranges.Select(range => range.Source.KeyPath).ToArray());
            Assert.Equal("sha256:177c1beffd5adbe4cc13fa8f3b6ee012aa7bf79ac1cf602cf44ee7d4504b4552", document.ContentHash);
        }

        [Theory]
        [MemberData(nameof(InvalidDocuments))]
        public void AC2_ValidationRejectsDeterministicFalsifiers(
            string label,
            AnnotatedInvocationDocument document,
            string expectedCode)
        {
            Assert.False(document.ValidationResult.IsValid);
            Assert.Contains(document.ValidationResult.Errors, error => error.Code == expectedCode);
            Assert.NotEmpty(label);
        }

        [Fact]
        public void AC2_AdjacentMergeableRangesWithWrongDocumentId_ReportRangeDocumentMismatch()
        {
            var first = new AgentJournalProvenanceRange(
                "doc.other",
                0,
                1,
                AgentJournalRangeKind.Configured,
                AgentJournalRedactionClass.SafeMetadata,
                TemplateSource);
            var second = new AgentJournalProvenanceRange(
                "doc.other",
                1,
                3,
                AgentJournalRangeKind.Configured,
                AgentJournalRedactionClass.SafeMetadata,
                TemplateSource);

            AnnotatedInvocationDocument document = Create("abc", first, second);

            Assert.False(document.ValidationResult.IsValid);
            Assert.Contains(document.ValidationResult.Errors, error => error.Code == AgentJournalValidator.RangeDocumentMismatch);
            Assert.Equal(2, document.Ranges.Count);
            Assert.All(document.Ranges, range => Assert.Equal("doc.other", range.DocumentId));
        }

        [Fact]
        public void AC3_PromptTraceResultAdapter_PreservesTextRangesAndMarksLegacyMetadata()
        {
            string text = "raw\U0001F600conf";
            var trace = new PromptTraceResult(
                text,
                new[] { new AnnotatedSpan(5, 9, "data/prompts/structural.yaml", "dialogue.system") });

            AnnotatedInvocationDocument document = PromptProvenanceAdapter.FromPromptTraceResult(
                trace,
                "doc.system",
                AgentJournalInputRole.System,
                "system-prompt",
                PromptTraceResolver.Map("data/prompts/structural.yaml", "prompt.catalog"));

            Assert.Equal(text, document.Text);
            Assert.True(document.ValidationResult.IsValid);
            Assert.Equal(new[] { 0, 5 }, document.Ranges.Select(range => range.StartUtf16).ToArray());
            Assert.Equal(new[] { 5, 9 }, document.Ranges.Select(range => range.EndUtf16).ToArray());
            Assert.Equal(AgentJournalRangeKind.RuntimeGenerated, document.Ranges[0].RangeKind);
            Assert.Equal(AgentJournalRangeKind.Configured, document.Ranges[1].RangeKind);
            Assert.Equal(PromptProvenanceAdapter.LegacyMissingSourceRevision, document.Ranges[1].Source.Revision);
        }

        [Fact]
        public void AC4_CanonicalJsonAndHash_AreStableAcrossRuns()
        {
            AnnotatedInvocationDocument first = StableDocument();
            AnnotatedInvocationDocument second = StableDocument();

            Assert.Equal(first.GetCanonicalJson(), second.GetCanonicalJson());
            Assert.Equal(first.GetCanonicalHash(), second.GetCanonicalHash());
            Assert.Equal(
                "{\"document_id\":\"doc.system\",\"role\":\"system\",\"kind\":\"system-prompt\",\"text\":\"alpha\",\"content_hash\":\"sha256:8ed3f6ad685b959ead7022518e1af76cd816f8e8ec7ccdda1ed4018e8f2223f8\",\"ranges\":[{\"document_id\":\"doc.system\",\"start_utf16\":0,\"end_utf16\":5,\"range_kind\":\"configured\",\"redaction_class\":\"safe_metadata\",\"source\":{\"kind\":\"configuration\",\"source_id\":\"prompt.catalog\",\"key_path\":\"dialogue.template\",\"revision\":\"rev-template\"}}]}",
                first.GetCanonicalJson());
            Assert.Equal("sha256:e9314a0ea17c9b6c9b4c1aea246298875e6bafda69f796bbe46bcfb48a84f3e5", first.GetCanonicalHash());
        }

        [Fact]
        public void AC5_ConversionToIssue1370InputDocument_PreservesContractValidity()
        {
            AnnotatedInvocationDocument document = StableDocument();

            AgentJournalInputDocument input = document.ToAgentJournalInputDocument();
            var invocation = new LlmInvocationRecord(
                new AgentJournalCorrelationIds("game-run-001", "agent-session-datee", "invocation-001", "operation-001", 1, attemptId: "attempt-001"),
                "test-model",
                "dialogue_options",
                new[] { input });

            Assert.True(AgentJournalValidator.Validate(invocation).IsValid);
            Assert.Equal(document.Text, input.Text);
            Assert.Equal(document.Ranges.Select(range => range.StartUtf16), input.Ranges.Select(range => range.StartUtf16));
        }

        public static IEnumerable<object[]> InvalidDocuments()
        {
            yield return new object[]
            {
                "gap",
                Create("abc", Range(0, 1), Range(2, 3)),
                AgentJournalValidator.MissingRange,
            };
            yield return new object[]
            {
                "out-of-bounds",
                Create("abc", Range(0, 4)),
                AgentJournalValidator.OutOfBoundsRange,
            };
            yield return new object[]
            {
                "zero-length",
                Create("abc", Range(0, 0), Range(0, 3)),
                AgentJournalValidator.ZeroLengthRange,
            };
            yield return new object[]
            {
                "overlap",
                Create("abc", Range(0, 2), Range(1, 3)),
                AgentJournalValidator.OverlappingRange,
            };
            yield return new object[]
            {
                "missing-revision",
                Create("abc", Range(0, 3, new AgentJournalSourceIdentity(AgentJournalSourceKind.Configuration, "prompt.catalog", "dialogue.template"))),
                AgentJournalValidator.MissingId,
            };
            yield return new object[]
            {
                "invalid-classification",
                Create("abc", Range(0, 3, TemplateSource, AgentJournalRangeKind.RuntimeGenerated)),
                AgentJournalValidator.InvalidSourceKind,
            };
        }

        private static AnnotatedInvocationDocument StableDocument()
            => new AnnotatedInvocationDocumentBuilder()
                .AppendConfigured("alpha", TemplateSource)
                .Build("doc.system", AgentJournalInputRole.System, "system-prompt");

        private static AnnotatedInvocationDocument Create(
            string text,
            params AgentJournalProvenanceRange[] ranges)
            => AnnotatedInvocationDocument.Create(
                "doc.test",
                AgentJournalInputRole.System,
                "test-kind",
                text,
                ranges);

        private static AgentJournalProvenanceRange Range(
            int start,
            int end,
            AgentJournalSourceIdentity? source = null,
            AgentJournalRangeKind rangeKind = AgentJournalRangeKind.Configured)
            => new AgentJournalProvenanceRange(
                "doc.test",
                start,
                end,
                rangeKind,
                rangeKind == AgentJournalRangeKind.Configured
                    ? AgentJournalRedactionClass.SafeMetadata
                    : AgentJournalRedactionClass.None,
                source ?? TemplateSource);

        private sealed class PromptTraceResolver : IPromptTraceSourceIdentityResolver
        {
            private readonly string _sourceFile;
            private readonly string _sourceId;

            private PromptTraceResolver(string sourceFile, string sourceId)
            {
                _sourceFile = sourceFile;
                _sourceId = sourceId;
            }

            public static PromptTraceResolver Map(string sourceFile, string sourceId)
                => new PromptTraceResolver(sourceFile, sourceId);

            public bool TryResolve(string? annotatedSourceFile, out string? sourceId)
            {
                if (string.Equals(annotatedSourceFile, _sourceFile, StringComparison.Ordinal))
                {
                    sourceId = _sourceId;
                    return true;
                }

                sourceId = null;
                return false;
            }
        }
    }
}

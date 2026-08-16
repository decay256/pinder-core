using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters.AgentJournals
{
    public static class PromptProvenanceAdapter
    {
        public const string LegacyMissingSourceRevision = "legacy-metadata-missing";

        public static AnnotatedInvocationDocument FromPromptTraceResult(
            PromptTraceResult trace,
            string documentId,
            AgentJournalInputRole role,
            string kind,
            IPromptTraceSourceIdentityResolver sourceIdentityResolver)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (documentId == null) throw new ArgumentNullException(nameof(documentId));
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (sourceIdentityResolver == null) throw new ArgumentNullException(nameof(sourceIdentityResolver));

            var ranges = new List<AgentJournalProvenanceRange>();
            int cursor = 0;
            foreach (AnnotatedSpan span in trace.Spans.OrderBy(span => span.Start).ThenBy(span => span.End))
            {
                if (span.Start > cursor)
                {
                    ranges.Add(CreateRuntimeRange(documentId, cursor, span.Start, "generated"));
                }

                if (span.End > span.Start)
                {
                    ranges.Add(CreateSpanRange(trace, documentId, span, sourceIdentityResolver));
                    cursor = Math.Max(cursor, span.End);
                }
            }

            if (cursor < trace.Text.Length)
            {
                ranges.Add(CreateRuntimeRange(documentId, cursor, trace.Text.Length, "generated"));
            }
            if (trace.Text.Length == 0)
            {
                ranges.Clear();
            }

            return AnnotatedInvocationDocument.Create(
                documentId,
                role,
                kind,
                trace.Text,
                ranges);
        }

        private static AgentJournalProvenanceRange CreateSpanRange(
            PromptTraceResult trace,
            string documentId,
            AnnotatedSpan span,
            IPromptTraceSourceIdentityResolver sourceIdentityResolver)
        {
            string sourceId = ResolveSourceId(span.SourceFile, sourceIdentityResolver);
            string keyPath = string.IsNullOrWhiteSpace(span.Key) ? "unknown" : span.Key!;
            if (string.Equals(sourceId, "runtime", StringComparison.Ordinal)
                || GameRunPromptSourceIdentityResolver.IsRuntimeSource(span.SourceFile))
            {
                return CreateRuntimeRange(documentId, span.Start, span.End, keyPath);
            }

            return new AgentJournalProvenanceRange(
                documentId,
                span.Start,
                span.End,
                AgentJournalRangeKind.Configured,
                AgentJournalRedactionClass.SafeMetadata,
                new AgentJournalSourceIdentity(
                    AgentJournalSourceKind.Configuration,
                    sourceId,
                    keyPath,
                    revision: LegacyMissingSourceRevision,
                    contentHash: ComputeSha256(trace.Text.Substring(span.Start, span.End - span.Start))));
        }

        private static string ResolveSourceId(
            string? annotatedSourceFile,
            IPromptTraceSourceIdentityResolver resolver)
        {
            if (!resolver.TryResolve(annotatedSourceFile, out string? sourceId)
                || string.IsNullOrWhiteSpace(sourceId))
            {
                throw new PromptTraceSourceIdentityException(
                    PromptTraceSourceIdentityException.UnmappedSourceIdentity,
                    "Prompt trace source has no registered journal identity mapping.");
            }

            return sourceId;
        }

        private static AgentJournalProvenanceRange CreateRuntimeRange(
            string documentId,
            int start,
            int end,
            string keyPath)
            => new AgentJournalProvenanceRange(
                documentId,
                start,
                end,
                AgentJournalRangeKind.RuntimeGenerated,
                AgentJournalRedactionClass.None,
                new AgentJournalSourceIdentity(
                    AgentJournalSourceKind.RuntimeGenerated,
                    "runtime",
                    string.IsNullOrWhiteSpace(keyPath) ? "generated" : keyPath));

        private static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return "sha256:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}

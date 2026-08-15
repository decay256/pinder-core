using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Text
{
    public interface IPromptTraceSourceIdentityResolver
    {
        bool TryResolve(string? annotatedSourceFile, out string? sourceId);
    }

    public sealed class PromptTraceSourceIdentityException : Exception
    {
        public const string UnmappedSourceIdentity = "unmapped_source_identity";
        public const string InvalidResolvedSourceIdentity = "invalid_resolved_source_identity";

        public PromptTraceSourceIdentityException(string code, string message)
            : base(message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        public string Code { get; }
    }

    public static class PromptTraceAgentJournalExtensions
    {
        public static AgentJournalInputDocument ToAgentJournalInputDocument(
            this PromptTraceResult trace,
            string documentId,
            AgentJournalInputRole role,
            IPromptTraceSourceIdentityResolver sourceIdentityResolver)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id is required.", nameof(documentId));
            if (sourceIdentityResolver == null) throw new ArgumentNullException(nameof(sourceIdentityResolver));

            var ranges = new List<AgentJournalProvenanceRange>();
            int cursor = 0;
            foreach (AnnotatedSpan span in trace.Spans.OrderBy(span => span.Start).ThenBy(span => span.End))
            {
                if (span.Start > cursor)
                {
                    ranges.Add(CreateRuntimeRange(documentId, cursor, span.Start));
                }
                if (span.End > span.Start)
                {
                    string sourceId = ResolveSourceId(span.SourceFile, sourceIdentityResolver);
                    string keyPath = string.IsNullOrWhiteSpace(span.Key) ? "unknown" : span.Key!;
                    string? keyError = AgentJournalSourceIdentifierPolicy.GetErrorCode(keyPath);
                    if (keyError != null)
                    {
                        throw new PromptTraceSourceIdentityException(
                            PromptTraceSourceIdentityException.InvalidResolvedSourceIdentity,
                            "Prompt trace key is not an opaque allowlisted identifier.");
                    }
                    ranges.Add(new AgentJournalProvenanceRange(
                        documentId,
                        span.Start,
                        span.End,
                        AgentJournalRangeKind.Configured,
                        AgentJournalRedactionClass.SafeMetadata,
                        new AgentJournalSourceIdentity(
                            AgentJournalSourceKind.Configuration,
                            sourceId,
                            keyPath)));
                    cursor = Math.Max(cursor, span.End);
                }
            }

            if (cursor < trace.Text.Length)
            {
                ranges.Add(CreateRuntimeRange(documentId, cursor, trace.Text.Length));
            }
            if (trace.Text.Length == 0)
            {
                ranges.Clear();
            }

            return new AgentJournalInputDocument(documentId, role, trace.Text, ranges);
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
            if (AgentJournalSourceIdentifierPolicy.GetErrorCode(sourceId) != null)
            {
                throw new PromptTraceSourceIdentityException(
                    PromptTraceSourceIdentityException.InvalidResolvedSourceIdentity,
                    "Resolved prompt trace source is not an opaque allowlisted identifier.");
            }
            return sourceId;
        }

        private static AgentJournalProvenanceRange CreateRuntimeRange(string documentId, int start, int end)
            => new AgentJournalProvenanceRange(
                documentId,
                start,
                end,
                AgentJournalRangeKind.RuntimeGenerated,
                AgentJournalRedactionClass.None,
                new AgentJournalSourceIdentity(
                    AgentJournalSourceKind.RuntimeGenerated,
                    "runtime",
                    "generated"));
    }
}

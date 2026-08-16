using System;
using System.Collections.Generic;
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

            AgentJournalInputDocument legacyDocument = trace.ToAgentJournalInputDocument(
                documentId,
                role,
                sourceIdentityResolver);
            var ranges = new List<AgentJournalProvenanceRange>(legacyDocument.Ranges.Count);
            for (int i = 0; i < legacyDocument.Ranges.Count; i++)
            {
                AgentJournalProvenanceRange range = legacyDocument.Ranges[i];
                ranges.Add(new AgentJournalProvenanceRange(
                    documentId,
                    range.StartUtf16,
                    range.EndUtf16,
                    range.RangeKind,
                    range.RedactionClass,
                    AddLegacyMetadataMarker(range.Source, range.RangeKind)));
            }

            return AnnotatedInvocationDocument.Create(
                documentId,
                role,
                kind,
                legacyDocument.Text,
                ranges);
        }
        private static AgentJournalSourceIdentity AddLegacyMetadataMarker(
            AgentJournalSourceIdentity source,
            AgentJournalRangeKind rangeKind)
        {
            if (rangeKind != AgentJournalRangeKind.Configured)
            {
                return source;
            }

            return new AgentJournalSourceIdentity(
                source.Kind,
                source.SourceId,
                source.KeyPath,
                revision: string.IsNullOrWhiteSpace(source.Revision)
                    ? LegacyMissingSourceRevision
                    : source.Revision,
                contentHash: source.ContentHash,
                editorTargetId: source.EditorTargetId);
        }
    }
}

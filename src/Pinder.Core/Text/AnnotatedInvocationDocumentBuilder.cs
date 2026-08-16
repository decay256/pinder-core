using System;
using System.Collections.Generic;
using System.Text;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Text
{
    public sealed class AnnotatedInvocationDocumentBuilder
    {
        private readonly StringBuilder _text = new StringBuilder();
        private readonly List<PendingRange> _ranges = new List<PendingRange>();

        public int Length => _text.Length;

        public AnnotatedInvocationDocumentBuilder Append(string? value)
            => AppendRuntimeGenerated(value, "generated");

        public AnnotatedInvocationDocumentBuilder AppendRuntimeGenerated(string? value, string keyPath = "generated")
            => AppendRange(
                value,
                AgentJournalRangeKind.RuntimeGenerated,
                AgentJournalRedactionClass.None,
                AnnotatedInvocationDocument.RuntimeGeneratedSource(keyPath));

        public AnnotatedInvocationDocumentBuilder AppendGeneratedLiteral(string? value, string keyPath = "literal")
            => AppendRuntimeGenerated(value, keyPath);

        public AnnotatedInvocationDocumentBuilder AppendConfigured(
            string? value,
            AgentJournalSourceIdentity source,
            AgentJournalRedactionClass redactionClass = AgentJournalRedactionClass.SafeMetadata)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return AppendRange(value, AgentJournalRangeKind.Configured, redactionClass, source);
        }

        public AnnotatedInvocationDocumentBuilder AppendDocument(AnnotatedInvocationDocument? document)
        {
            if (document == null)
            {
                return this;
            }

            int offset = _text.Length;
            _text.Append(document.Text);
            for (int i = 0; i < document.Ranges.Count; i++)
            {
                AgentJournalProvenanceRange range = document.Ranges[i];
                _ranges.Add(new PendingRange(
                    offset + range.StartUtf16,
                    offset + range.EndUtf16,
                    range.RangeKind,
                    range.RedactionClass,
                    range.Source));
            }

            return this;
        }

        public AnnotatedInvocationDocumentBuilder AppendTemplate(
            string template,
            IReadOnlyDictionary<string, AnnotatedInvocationDocument> substitutions,
            AgentJournalSourceIdentity templateSource)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (substitutions == null) throw new ArgumentNullException(nameof(substitutions));
            if (templateSource == null) throw new ArgumentNullException(nameof(templateSource));

            int literalStart = 0;
            int cursor = 0;
            while (cursor < template.Length)
            {
                int open = template.IndexOf('{', cursor);
                if (open < 0)
                {
                    break;
                }

                int close = template.IndexOf('}', open + 1);
                if (close < 0)
                {
                    break;
                }

                string key = template.Substring(open + 1, close - open - 1);
                if (!substitutions.TryGetValue(key, out AnnotatedInvocationDocument? substitution))
                {
                    cursor = close + 1;
                    continue;
                }

                AppendConfigured(template.Substring(literalStart, open - literalStart), templateSource);
                AppendDocument(substitution);
                cursor = close + 1;
                literalStart = cursor;
            }

            AppendConfigured(template.Substring(literalStart), templateSource);
            return this;
        }

        public AnnotatedInvocationDocumentBuilder Trim()
        {
            int originalLength = _text.Length;
            if (originalLength == 0)
            {
                return this;
            }

            int start = 0;
            while (start < originalLength && char.IsWhiteSpace(_text[start]))
            {
                start++;
            }

            int endExclusive = originalLength;
            while (endExclusive > start && char.IsWhiteSpace(_text[endExclusive - 1]))
            {
                endExclusive--;
            }

            if (start == 0 && endExclusive == originalLength)
            {
                return this;
            }

            string trimmed = _text.ToString(start, endExclusive - start);
            _text.Clear();
            _text.Append(trimmed);

            var adjusted = new List<PendingRange>();
            for (int i = 0; i < _ranges.Count; i++)
            {
                PendingRange range = _ranges[i];
                int clippedStart = Math.Max(range.StartUtf16, start);
                int clippedEnd = Math.Min(range.EndUtf16, endExclusive);
                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                adjusted.Add(new PendingRange(
                    clippedStart - start,
                    clippedEnd - start,
                    range.RangeKind,
                    range.RedactionClass,
                    range.Source));
            }

            _ranges.Clear();
            _ranges.AddRange(adjusted);
            return this;
        }

        public AnnotatedInvocationDocument Build(
            string documentId,
            AgentJournalInputRole role,
            string kind)
        {
            if (documentId == null) throw new ArgumentNullException(nameof(documentId));
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            return AnnotatedInvocationDocument.Create(documentId, role, kind, _text.ToString(), ToRanges(documentId));
        }

        private AnnotatedInvocationDocumentBuilder AppendRange(
            string? value,
            AgentJournalRangeKind rangeKind,
            AgentJournalRedactionClass redactionClass,
            AgentJournalSourceIdentity source)
        {
            if (value == null || value.Length == 0)
            {
                return this;
            }

            int start = _text.Length;
            _text.Append(value);
            _ranges.Add(new PendingRange(start, _text.Length, rangeKind, redactionClass, source));
            return this;
        }

        private AgentJournalProvenanceRange[] ToRanges(string documentId)
        {
            var ranges = new List<AgentJournalProvenanceRange>();
            for (int i = 0; i < _ranges.Count; i++)
            {
                PendingRange range = _ranges[i];
                if (range.EndUtf16 <= range.StartUtf16)
                {
                    continue;
                }

                if (ranges.Count > 0)
                {
                    AgentJournalProvenanceRange previous = ranges[ranges.Count - 1];
                    if (previous.EndUtf16 == range.StartUtf16
                        && previous.RangeKind == range.RangeKind
                        && previous.RedactionClass == range.RedactionClass
                        && SourceEquals(previous.Source, range.Source))
                    {
                        ranges[ranges.Count - 1] = new AgentJournalProvenanceRange(
                            documentId,
                            previous.StartUtf16,
                            range.EndUtf16,
                            previous.RangeKind,
                            previous.RedactionClass,
                            previous.Source);
                        continue;
                    }
                }

                ranges.Add(new AgentJournalProvenanceRange(
                    documentId,
                    range.StartUtf16,
                    range.EndUtf16,
                    range.RangeKind,
                    range.RedactionClass,
                    range.Source));
            }

            return ranges.ToArray();
        }

        private static bool SourceEquals(AgentJournalSourceIdentity left, AgentJournalSourceIdentity right)
            => left.Kind == right.Kind
                && string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal)
                && string.Equals(left.KeyPath, right.KeyPath, StringComparison.Ordinal)
                && string.Equals(left.Revision, right.Revision, StringComparison.Ordinal)
                && string.Equals(left.ContentHash, right.ContentHash, StringComparison.Ordinal)
                && string.Equals(left.EditorTargetId, right.EditorTargetId, StringComparison.Ordinal);

        private sealed class PendingRange
        {
            public PendingRange(
                int startUtf16,
                int endUtf16,
                AgentJournalRangeKind rangeKind,
                AgentJournalRedactionClass redactionClass,
                AgentJournalSourceIdentity source)
            {
                StartUtf16 = startUtf16;
                EndUtf16 = endUtf16;
                RangeKind = rangeKind;
                RedactionClass = redactionClass;
                Source = source;
            }

            public int StartUtf16 { get; }
            public int EndUtf16 { get; }
            public AgentJournalRangeKind RangeKind { get; }
            public AgentJournalRedactionClass RedactionClass { get; }
            public AgentJournalSourceIdentity Source { get; }
        }
    }
}

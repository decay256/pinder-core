using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Text
{
    public sealed class AnnotatedInvocationDocument
    {
        private AnnotatedInvocationDocument(
            string documentId,
            AgentJournalInputRole role,
            string kind,
            string text,
            IReadOnlyList<AgentJournalProvenanceRange> ranges)
        {
            DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            Role = role;
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            IReadOnlyList<AgentJournalProvenanceRange> rawRanges =
                (ranges ?? throw new ArgumentNullException(nameof(ranges))).ToArray();
            ValidationResult = ValidateDocument(DocumentId, Role, Kind, Text, rawRanges);
            Ranges = ValidationResult.IsValid
                ? CanonicalizeRanges(rawRanges)
                : rawRanges;
            ContentHash = ComputeSha256(Text);
        }

        public string DocumentId { get; }
        public AgentJournalInputRole Role { get; }
        public string Kind { get; }
        public string Text { get; }
        public IReadOnlyList<AgentJournalProvenanceRange> Ranges { get; }
        public string ContentHash { get; }
        public AgentJournalValidationResult ValidationResult { get; }

        public static AnnotatedInvocationDocument Create(
            string documentId,
            AgentJournalInputRole role,
            string kind,
            string text,
            IReadOnlyList<AgentJournalProvenanceRange> ranges)
            => new AnnotatedInvocationDocument(documentId, role, kind, text, ranges);

        public AgentJournalInputDocument ToAgentJournalInputDocument()
        {
            if (!ValidationResult.IsValid)
            {
                throw new InvalidOperationException("Cannot convert invalid annotated invocation document.");
            }

            return new AgentJournalInputDocument(DocumentId, Role, Text, Ranges);
        }

        public string GetCanonicalJson()
            => AgentJournalJson.Serialize(new CanonicalDocument(
                DocumentId,
                Role,
                Kind,
                Text,
                ContentHash,
                Ranges));

        public string GetCanonicalHash()
            => ComputeSha256(GetCanonicalJson());

        internal static AgentJournalSourceIdentity RuntimeGeneratedSource(string keyPath)
            => new AgentJournalSourceIdentity(
                AgentJournalSourceKind.RuntimeGenerated,
                "runtime",
                string.IsNullOrWhiteSpace(keyPath) ? "generated" : keyPath);

        internal static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder("sha256:", "sha256:".Length + hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static AgentJournalValidationResult ValidateDocument(
            string documentId,
            AgentJournalInputRole role,
            string kind,
            string text,
            IReadOnlyList<AgentJournalProvenanceRange> ranges)
        {
            var errors = new List<AgentJournalValidationError>();
            AddMissing(documentId, "$.document_id", errors);
            AddMissing(kind, "$.kind", errors);
            if (AgentJournalSourceIdentifierPolicy.GetErrorCode(kind) != null)
            {
                errors.Add(new AgentJournalValidationError(
                    AgentJournalValidator.ForbiddenSourceLink,
                    "$.kind"));
            }

            var invocation = new LlmInvocationRecord(
                new AgentJournalCorrelationIds(
                    "validation-game-run",
                    "validation-agent-session",
                    "validation-invocation",
                    "validation-operation",
                    1,
                    attemptId: "validation-attempt"),
                "validation-model",
                "validation_phase",
                new[] { new AgentJournalInputDocument(documentId, role, text, ranges) });
            errors.AddRange(AgentJournalValidator.Validate(invocation).Errors);

            for (int i = 0; i < ranges.Count; i++)
            {
                AgentJournalProvenanceRange range = ranges[i];
                if (range == null)
                {
                    continue;
                }

                string path = "$.ranges[" + i + "]";
                ValidateClassification(range, path, errors);
                ValidateRevision(range, path, errors);
            }

            return AgentJournalValidationResult.From(errors);
        }

        private static void ValidateClassification(
            AgentJournalProvenanceRange range,
            string path,
            ICollection<AgentJournalValidationError> errors)
        {
            if (!Enum.IsDefined(typeof(AgentJournalRangeKind), range.RangeKind)
                || !Enum.IsDefined(typeof(AgentJournalSourceKind), range.Source.Kind))
            {
                return;
            }

            if (range.RangeKind == AgentJournalRangeKind.RuntimeGenerated)
            {
                if (range.Source.Kind != AgentJournalSourceKind.RuntimeGenerated)
                {
                    errors.Add(new AgentJournalValidationError(
                        AgentJournalValidator.InvalidSourceKind,
                        path + ".source.kind"));
                }

                return;
            }

            if (range.RangeKind == AgentJournalRangeKind.Configured)
            {
                if (range.Source.Kind != AgentJournalSourceKind.Configuration
                    && range.Source.Kind != AgentJournalSourceKind.Catalog)
                {
                    errors.Add(new AgentJournalValidationError(
                        AgentJournalValidator.InvalidSourceKind,
                        path + ".source.kind"));
                }
            }
        }

        private static void ValidateRevision(
            AgentJournalProvenanceRange range,
            string path,
            ICollection<AgentJournalValidationError> errors)
        {
            if (!Enum.IsDefined(typeof(AgentJournalRangeKind), range.RangeKind)
                || range.RangeKind != AgentJournalRangeKind.Configured)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(range.Source.Revision)
                && string.IsNullOrWhiteSpace(range.Source.ContentHash))
            {
                errors.Add(new AgentJournalValidationError(
                    AgentJournalValidator.MissingId,
                    path + ".source.revision"));
            }
        }

        private static void AddMissing(
            string? value,
            string path,
            ICollection<AgentJournalValidationError> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new AgentJournalValidationError(AgentJournalValidator.MissingId, path));
            }
        }

        private static IReadOnlyList<AgentJournalProvenanceRange> CanonicalizeRanges(
            IReadOnlyList<AgentJournalProvenanceRange> ranges)
        {
            var canonical = new List<AgentJournalProvenanceRange>();
            foreach (AgentJournalProvenanceRange range in ranges)
            {
                if (range == null)
                {
                    canonical.Add(range!);
                    continue;
                }

                if (canonical.Count == 0)
                {
                    canonical.Add(range);
                    continue;
                }

                AgentJournalProvenanceRange previous = canonical[canonical.Count - 1];
                if (previous != null
                    && previous.EndUtf16 == range.StartUtf16
                    && previous.EndUtf16 > previous.StartUtf16
                    && range.EndUtf16 > range.StartUtf16
                    && RangeIdentityEquals(previous, range))
                {
                    canonical[canonical.Count - 1] = new AgentJournalProvenanceRange(
                        previous.DocumentId,
                        previous.StartUtf16,
                        range.EndUtf16,
                        previous.RangeKind,
                        previous.RedactionClass,
                        previous.Source);
                }
                else
                {
                    canonical.Add(range);
                }
            }

            return canonical.ToArray();
        }

        private static bool RangeIdentityEquals(
            AgentJournalProvenanceRange left,
            AgentJournalProvenanceRange right)
            => string.Equals(left.DocumentId, right.DocumentId, StringComparison.Ordinal)
                && left.RangeKind == right.RangeKind
                && left.RedactionClass == right.RedactionClass
                && SourceEquals(left.Source, right.Source);

        private static bool SourceEquals(
            AgentJournalSourceIdentity left,
            AgentJournalSourceIdentity right)
            => left.Kind == right.Kind
                && string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal)
                && string.Equals(left.KeyPath, right.KeyPath, StringComparison.Ordinal)
                && string.Equals(left.Revision, right.Revision, StringComparison.Ordinal)
                && string.Equals(left.ContentHash, right.ContentHash, StringComparison.Ordinal)
                && string.Equals(left.EditorTargetId, right.EditorTargetId, StringComparison.Ordinal);

        private sealed class CanonicalDocument
        {
            public CanonicalDocument(
                string documentId,
                AgentJournalInputRole role,
                string kind,
                string text,
                string contentHash,
                IReadOnlyList<AgentJournalProvenanceRange> ranges)
            {
                DocumentId = documentId;
                Role = role;
                Kind = kind;
                Text = text;
                ContentHash = contentHash;
                Ranges = ranges.Select(CanonicalRange.From).ToArray();
            }

            public string DocumentId { get; }
            public AgentJournalInputRole Role { get; }
            public string Kind { get; }
            public string Text { get; }
            public string ContentHash { get; }
            public IReadOnlyList<CanonicalRange> Ranges { get; }
        }

        private sealed class CanonicalRange
        {
            private CanonicalRange(
                string documentId,
                int startUtf16,
                int endUtf16,
                AgentJournalRangeKind rangeKind,
                AgentJournalRedactionClass redactionClass,
                CanonicalSource source)
            {
                DocumentId = documentId;
                StartUtf16 = startUtf16;
                EndUtf16 = endUtf16;
                RangeKind = rangeKind;
                RedactionClass = redactionClass;
                Source = source;
            }

            public string DocumentId { get; }
            public int StartUtf16 { get; }
            public int EndUtf16 { get; }
            public AgentJournalRangeKind RangeKind { get; }
            public AgentJournalRedactionClass RedactionClass { get; }
            public CanonicalSource Source { get; }

            public static CanonicalRange From(AgentJournalProvenanceRange range)
                => new CanonicalRange(
                    range.DocumentId,
                    range.StartUtf16,
                    range.EndUtf16,
                    range.RangeKind,
                    range.RedactionClass,
                    CanonicalSource.From(range.Source));
        }

        private sealed class CanonicalSource
        {
            private CanonicalSource(
                AgentJournalSourceKind kind,
                string sourceId,
                string keyPath,
                string? revision,
                string? contentHash,
                string? editorTargetId)
            {
                Kind = kind;
                SourceId = sourceId;
                KeyPath = keyPath;
                Revision = revision;
                ContentHash = contentHash;
                EditorTargetId = editorTargetId;
            }

            public AgentJournalSourceKind Kind { get; }
            public string SourceId { get; }
            public string KeyPath { get; }
            public string? Revision { get; }
            public string? ContentHash { get; }
            public string? EditorTargetId { get; }

            public static CanonicalSource From(AgentJournalSourceIdentity source)
                => new CanonicalSource(
                    source.Kind,
                    source.SourceId,
                    source.KeyPath,
                    source.Revision,
                    source.ContentHash,
                    source.EditorTargetId);
        }
    }
}

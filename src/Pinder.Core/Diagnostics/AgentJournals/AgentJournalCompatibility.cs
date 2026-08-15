using System;
using System.Collections.Generic;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public sealed class AgentJournalCompatibilityResult
    {
        public AgentJournalCompatibilityResult(
            AgentJournalCompatibilityKind kind,
            string customType,
            string? warning,
            string? opaqueJson,
            IReadOnlyList<AgentJournalValidationError>? errors = null)
        {
            Kind = kind;
            CustomType = customType ?? throw new ArgumentNullException(nameof(customType));
            Warning = warning;
            OpaqueJson = opaqueJson;
            Errors = errors ?? Array.Empty<AgentJournalValidationError>();
        }

        public AgentJournalCompatibilityKind Kind { get; }
        public string CustomType { get; }
        public string? Warning { get; }
        public string? OpaqueJson { get; }
        public IReadOnlyList<AgentJournalValidationError> Errors { get; }
    }
}

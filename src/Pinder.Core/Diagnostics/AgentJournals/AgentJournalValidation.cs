using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public sealed class AgentJournalValidationError
    {
        public AgentJournalValidationError(string code, string path)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Code { get; }
        public string Path { get; }
    }

    public sealed class AgentJournalValidationResult
    {
        private AgentJournalValidationResult(IReadOnlyList<AgentJournalValidationError> errors)
        {
            Errors = errors;
        }

        public IReadOnlyList<AgentJournalValidationError> Errors { get; }
        public bool IsValid => Errors.Count == 0;

        public static AgentJournalValidationResult From(IReadOnlyList<AgentJournalValidationError> errors)
            => new AgentJournalValidationResult(errors);
    }

    public static class AgentJournalValidator
    {
        public const string MissingId = "missing_id";
        public const string DuplicateId = "duplicate_id";
        public const string InvalidAttemptOrdinal = "invalid_attempt_ordinal";
        public const string InvalidInputRole = "invalid_input_role";
        public const string InvalidSourceKind = "invalid_source_kind";
        public const string InvalidRangeKind = "invalid_range_kind";
        public const string InvalidRedactionClass = "invalid_redaction_class";
        public const string InvalidTerminalStatus = "invalid_terminal_status";
        public const string InvalidStatusTransition = "invalid_status_transition";
        public const string MissingInputDocument = "missing_input_document";
        public const string MissingRange = "missing_range";
        public const string ZeroLengthRange = "zero_length_range";
        public const string UnorderedRange = "unordered_range";
        public const string OverlappingRange = "overlapping_range";
        public const string OutOfBoundsRange = "out_of_bounds_range";
        public const string RangeDocumentMismatch = "range_document_mismatch";
        public const string ForbiddenSourceLink = "forbidden_source_link";
        public const string CredentialShapedSourceIdentifier = "credential_shaped_source_identifier";
        public const string SurrogateSplitRange = "surrogate_split_range";
        public const string NegativeUsage = "negative_usage";
        public const string InvalidUsageStatus = "invalid_usage_status";
        public const string InvalidUsageCompleteness = "invalid_usage_completeness";
        public const string ForbiddenOwnerId = "forbidden_owner_id";
        public const string InvalidRoleFactDecision = "invalid_role_fact_decision";


        public static AgentJournalValidationResult Validate(AgentJournalRoleFactPolicyDecisionRecord record)
        {
            var errors = new List<AgentJournalValidationError>();
            if (record == null)
            {
                errors.Add(new AgentJournalValidationError(MissingId, "$"));
                return AgentJournalValidationResult.From(errors);
            }

            if (record.SchemaVersion != AgentJournalRoleFactPolicyDecisionRecord.CurrentSchemaVersion)
                errors.Add(new AgentJournalValidationError(InvalidRoleFactDecision, "$.schema_version"));
            if (record.Correlation == null)
            {
                errors.Add(new AgentJournalValidationError(MissingId, "$.correlation"));
            }
            else
            {
                AddMissing(record.Correlation.GameRunId, "$.correlation.game_run_id", errors);
                AddMissing(record.Correlation.AgentSessionId, "$.correlation.agent_session_id", errors);
                AddMissing(record.Correlation.RequestId, "$.correlation.request_id", errors);
                AddMissing(record.Correlation.TurnId, "$.correlation.turn_id", errors);
                AddOpaqueIdentifier(record.Correlation.GameRunId, "$.correlation.game_run_id", errors);
                AddOpaqueIdentifier(record.Correlation.AgentSessionId, "$.correlation.agent_session_id", errors);
                AddOpaqueIdentifier(record.Correlation.RequestId, "$.correlation.request_id", errors);
                AddOpaqueIdentifier(record.Correlation.TurnId, "$.correlation.turn_id", errors);
                AddOpaqueIdentifier(record.Correlation.BranchId, "$.correlation.branch_id", errors);
            }
            AddMissing(record.OperationKind, "$.operation_kind", errors);
            AddMissing(record.FactSourceId, "$.fact_source_id", errors);
            AddMissing(record.DecisionCode, "$.decision_code", errors);
            AddOpaqueIdentifier(record.OperationKind, "$.operation_kind", errors);
            AddOpaqueIdentifier(record.FactSourceId, "$.fact_source_id", errors);
            AddOpaqueIdentifier(record.DecisionCode, "$.decision_code", errors);
            if (!Enum.IsDefined(typeof(PromptFactSourceKind), record.FactSourceKind)
                || record.OwnerCharacterId == Guid.Empty
                || record.RecipientCharacterId == Guid.Empty
                || !Enum.IsDefined(typeof(ConversationParticipantRole), record.OwnerRole)
                || !Enum.IsDefined(typeof(ConversationParticipantRole), record.RecipientRole)
                || !Enum.IsDefined(typeof(PromptFactVisibility), record.Visibility))
            {
                errors.Add(new AgentJournalValidationError(InvalidRoleFactDecision, "$"));
            }
            return AgentJournalValidationResult.From(errors);
        }

        public static AgentJournalValidationResult Validate(LlmInvocationRecord record)
        {
            var errors = new List<AgentJournalValidationError>();
            if (record == null)
            {
                errors.Add(new AgentJournalValidationError(MissingId, "$"));
                return AgentJournalValidationResult.From(errors);
            }

            ValidateCorrelation(record.Correlation, "$.correlation", errors);
            AddMissing(record.ModelId, "$.model_id", errors);
            AddMissing(record.Phase, "$.phase", errors);
            if (record.InputDocuments == null || record.InputDocuments.Count == 0)
            {
                errors.Add(new AgentJournalValidationError(MissingInputDocument, "$.input_documents"));
                return AgentJournalValidationResult.From(errors);
            }

            var documentIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < record.InputDocuments.Count; i++)
            {
                AgentJournalInputDocument document = record.InputDocuments[i];
                if (document == null)
                {
                    errors.Add(new AgentJournalValidationError(MissingInputDocument, "$.input_documents[" + i + "]"));
                    continue;
                }
                ValidateDocument(document, "$.input_documents[" + i + "]", documentIds, errors);
            }

            if (record.RoleFactAccessDecisions != null)
            {
                for (int i = 0; i < record.RoleFactAccessDecisions.Count; i++)
                {
                    AgentJournalRoleFactAccessDecision decision = record.RoleFactAccessDecisions[i];
                    string path = "$.role_fact_access_decisions[" + i + "]";
                    if (decision == null
                        || decision.SchemaVersion != AgentJournalRoleFactAccessDecision.CurrentSchemaVersion
                        || string.IsNullOrWhiteSpace(decision.Code)
                        || string.IsNullOrWhiteSpace(decision.FactSourceId)
                        || !Enum.IsDefined(typeof(PromptFactSourceKind), decision.FactSourceKind)
                        || decision.SubjectCharacterId == Guid.Empty
                        || decision.RecipientCharacterId == Guid.Empty
                        || !Enum.IsDefined(typeof(ConversationParticipantRole), decision.SubjectRole)
                        || !Enum.IsDefined(typeof(ConversationParticipantRole), decision.RecipientRole)
                        || !Enum.IsDefined(typeof(PromptFactVisibility), decision.Visibility))
                    {
                        errors.Add(new AgentJournalValidationError(InvalidRoleFactDecision, path));
                    }
                }
            }

            return AgentJournalValidationResult.From(errors);
        }

        public static AgentJournalValidationResult Validate(LlmResultRecord record)
        {
            var errors = new List<AgentJournalValidationError>();
            if (record == null)
            {
                errors.Add(new AgentJournalValidationError(MissingId, "$"));
                return AgentJournalValidationResult.From(errors);
            }

            ValidateCorrelation(record.Correlation, "$.correlation", errors);
            if (!Enum.IsDefined(typeof(AgentJournalTerminalStatus), record.TerminalStatus))
            {
                errors.Add(new AgentJournalValidationError(InvalidTerminalStatus, "$.terminal_status"));
            }
            else
            {
                ValidateTerminalState(record, errors);
            }
            if (!Enum.IsDefined(typeof(AgentJournalUsageStatus), record.UsageStatus))
            {
                errors.Add(new AgentJournalValidationError(InvalidUsageStatus, "$.usage_status"));
            }
            if (record.Usage != null)
            {
                AddNegative(record.Usage.InputTokens, "$.usage.input_tokens", errors);
                AddNegative(record.Usage.OutputTokens, "$.usage.output_tokens", errors);
                AddNegative(record.Usage.TotalTokens, "$.usage.total_tokens", errors);
                AddNegative(record.Usage.CacheCreationInputTokens, "$.usage.cache_creation_input_tokens", errors);
                AddNegative(record.Usage.CacheReadInputTokens, "$.usage.cache_read_input_tokens", errors);
            }
            ValidateUsageCompleteness(record, errors);

            return AgentJournalValidationResult.From(errors);
        }

        private static void ValidateUsageCompleteness(
            LlmResultRecord record,
            ICollection<AgentJournalValidationError> errors)
        {
            if (record.UsageStatus == AgentJournalUsageStatus.Complete)
            {
                AgentJournalUsage? usage = record.Usage;
                if (usage == null
                    || !usage.InputTokens.HasValue
                    || !usage.OutputTokens.HasValue
                    || !usage.TotalTokens.HasValue
                    || !usage.CacheCreationInputTokens.HasValue
                    || !usage.CacheReadInputTokens.HasValue)
                {
                    errors.Add(new AgentJournalValidationError(
                        InvalidUsageCompleteness,
                        "$.usage_status"));
                }
            }
            else if (record.UsageStatus == AgentJournalUsageStatus.Unavailable
                && record.Usage != null)
            {
                errors.Add(new AgentJournalValidationError(
                    InvalidUsageCompleteness,
                    "$.usage_status"));
            }
        }

        public static AgentJournalValidationResult Validate(MessageLinkRecord record)
        {
            var errors = new List<AgentJournalValidationError>();
            if (record == null)
            {
                errors.Add(new AgentJournalValidationError(MissingId, "$"));
                return AgentJournalValidationResult.From(errors);
            }

            AddMissing(record.SemanticEntryId, "$.semantic_entry_id", errors);
            AddMissing(record.InvocationId, "$.invocation_id", errors);
            AddMissing(record.AgentSessionId, "$.agent_session_id", errors);
            AddOpaqueIdentifier(record.SemanticEntryId, "$.semantic_entry_id", errors);
            AddOpaqueIdentifier(record.InvocationId, "$.invocation_id", errors);
            AddOpaqueIdentifier(record.AgentSessionId, "$.agent_session_id", errors);
            AddOpaqueIdentifier(record.TurnId, "$.turn_id", errors);
            AddOpaqueIdentifier(record.BranchId, "$.branch_id", errors);
            return AgentJournalValidationResult.From(errors);
        }

        public static bool IsAllowedSourceValue(string? value)
            => AgentJournalSourceIdentifierPolicy.GetErrorCode(value) == null;

        private static void ValidateCorrelation(AgentJournalCorrelationIds correlation, string path, ICollection<AgentJournalValidationError> errors)
        {
            if (correlation == null)
            {
                errors.Add(new AgentJournalValidationError(MissingId, path));
                return;
            }

            AddMissing(correlation.GameRunId, path + ".game_run_id", errors);
            bool hasNoSessionOwner = !string.IsNullOrWhiteSpace(correlation.Owner)
                || !string.IsNullOrWhiteSpace(correlation.JournalDestination)
                || !string.IsNullOrWhiteSpace(correlation.ExecutionClass);
            if (hasNoSessionOwner)
            {
                AddMissing(correlation.Owner, path + ".owner", errors);
                AddMissing(correlation.JournalDestination, path + ".journal_destination", errors);
                AddMissing(correlation.ExecutionClass, path + ".execution_class", errors);
                if (!string.IsNullOrWhiteSpace(correlation.AgentSessionId))
                {
                    errors.Add(new AgentJournalValidationError(ForbiddenOwnerId, path + ".agent_session_id"));
                }
            }
            else
            {
                AddMissing(correlation.AgentSessionId, path + ".agent_session_id", errors);
            }
            AddMissing(correlation.InvocationId, path + ".invocation_id", errors);
            AddMissing(correlation.OperationId, path + ".operation_id", errors);
            AddOpaqueIdentifier(correlation.GameRunId, path + ".game_run_id", errors);
            AddOpaqueIdentifier(correlation.AgentSessionId, path + ".agent_session_id", errors);
            AddOpaqueIdentifier(correlation.InvocationId, path + ".invocation_id", errors);
            AddOpaqueIdentifier(correlation.OperationId, path + ".operation_id", errors);
            AddOpaqueIdentifier(correlation.AttemptId, path + ".attempt_id", errors);
            AddOpaqueIdentifier(correlation.RequestId, path + ".request_id", errors);
            AddOpaqueIdentifier(correlation.TurnId, path + ".turn_id", errors);
            AddOpaqueIdentifier(correlation.BranchId, path + ".branch_id", errors);
            if (correlation.AttemptOrdinal < 1)
            {
                errors.Add(new AgentJournalValidationError(InvalidAttemptOrdinal, path + ".attempt_ordinal"));
            }
            AddMissing(correlation.AttemptId, path + ".attempt_id", errors);
            AddForbiddenLink(correlation.GameRunId, path + ".game_run_id", errors);
            AddForbiddenLink(correlation.AgentSessionId, path + ".agent_session_id", errors);
            AddForbiddenLink(correlation.InvocationId, path + ".invocation_id", errors);
            AddForbiddenLink(correlation.OperationId, path + ".operation_id", errors);
            AddForbiddenLink(correlation.AttemptId, path + ".attempt_id", errors);
            AddForbiddenLink(correlation.RequestId, path + ".request_id", errors);
            AddForbiddenLink(correlation.TurnId, path + ".turn_id", errors);
            AddForbiddenLink(correlation.BranchId, path + ".branch_id", errors);
            AddForbiddenLink(correlation.Owner, path + ".owner", errors);
            AddForbiddenLink(correlation.JournalDestination, path + ".journal_destination", errors);
            AddForbiddenLink(correlation.ExecutionClass, path + ".execution_class", errors);
            AddForbiddenLink(correlation.OutputLinkId, path + ".output_link_id", errors);
            if (correlation.Context != null)
            {
                foreach (KeyValuePair<string, string> entry in correlation.Context)
                {
                    AddMissing(entry.Key, path + ".context.key", errors);
                    AddMissing(entry.Value, path + ".context." + entry.Key, errors);
                    AddForbiddenLink(entry.Key, path + ".context.key", errors);
                    AddForbiddenLink(entry.Value, path + ".context." + entry.Key, errors);
                }
            }
        }

        private static void ValidateDocument(
            AgentJournalInputDocument document,
            string path,
            ISet<string> documentIds,
            ICollection<AgentJournalValidationError> errors)
        {
            AddMissing(document.DocumentId, path + ".document_id", errors);
            if (!Enum.IsDefined(typeof(AgentJournalInputRole), document.Role))
            {
                errors.Add(new AgentJournalValidationError(InvalidInputRole, path + ".role"));
            }
            if (!string.IsNullOrWhiteSpace(document.DocumentId) && !documentIds.Add(document.DocumentId))
            {
                errors.Add(new AgentJournalValidationError(DuplicateId, path + ".document_id"));
            }
            if (document.Ranges == null || document.Ranges.Count == 0 && document.Text.Length > 0)
            {
                errors.Add(new AgentJournalValidationError(MissingRange, path + ".ranges"));
                return;
            }

            int previousEnd = 0;
            for (int i = 0; i < document.Ranges.Count; i++)
            {
                AgentJournalProvenanceRange range = document.Ranges[i];
                string rangePath = path + ".ranges[" + i + "]";
                if (range == null)
                {
                    errors.Add(new AgentJournalValidationError(MissingRange, rangePath));
                    continue;
                }
                if (range.StartUtf16 > previousEnd)
                {
                    errors.Add(new AgentJournalValidationError(MissingRange, rangePath));
                }
                if (!string.Equals(document.DocumentId, range.DocumentId, StringComparison.Ordinal))
                {
                    errors.Add(new AgentJournalValidationError(RangeDocumentMismatch, rangePath + ".document_id"));
                }
                if (!Enum.IsDefined(typeof(AgentJournalRangeKind), range.RangeKind))
                {
                    errors.Add(new AgentJournalValidationError(InvalidRangeKind, rangePath + ".range_kind"));
                }
                if (!Enum.IsDefined(typeof(AgentJournalRedactionClass), range.RedactionClass))
                {
                    errors.Add(new AgentJournalValidationError(InvalidRedactionClass, rangePath + ".redaction_class"));
                }
                if (range.StartUtf16 < 0 || range.EndUtf16 > document.Text.Length || range.EndUtf16 < range.StartUtf16)
                {
                    errors.Add(new AgentJournalValidationError(OutOfBoundsRange, rangePath));
                }
                else
                {
                    AddSurrogateSplit(document.Text, range.StartUtf16, rangePath + ".start_utf16", errors);
                    AddSurrogateSplit(document.Text, range.EndUtf16, rangePath + ".end_utf16", errors);
                }
                if (range.EndUtf16 == range.StartUtf16)
                {
                    errors.Add(new AgentJournalValidationError(ZeroLengthRange, rangePath));
                }
                if (previousEnd > range.StartUtf16)
                {
                    errors.Add(new AgentJournalValidationError(OverlappingRange, rangePath));
                    errors.Add(new AgentJournalValidationError(UnorderedRange, rangePath));
                }
                ValidateSource(range.Source, rangePath + ".source", errors);
                previousEnd = range.EndUtf16;
            }
            if (document.Ranges.Count > 0 && previousEnd < document.Text.Length)
            {
                errors.Add(new AgentJournalValidationError(MissingRange, path + ".ranges"));
            }
        }

        private static void ValidateSource(AgentJournalSourceIdentity source, string path, ICollection<AgentJournalValidationError> errors)
        {
            if (!Enum.IsDefined(typeof(AgentJournalSourceKind), source.Kind))
            {
                errors.Add(new AgentJournalValidationError(InvalidSourceKind, path + ".kind"));
            }
            AddMissing(source.SourceId, path + ".source_id", errors);
            AddMissing(source.KeyPath, path + ".key_path", errors);
            AddOpaqueIdentifier(source.SourceId, path + ".source_id", errors);
            AddOpaqueIdentifier(source.KeyPath, path + ".key_path", errors);
            AddOpaqueIdentifier(source.Revision, path + ".revision", errors);
            AddOpaqueIdentifier(source.ContentHash, path + ".content_hash", errors);
            AddOpaqueIdentifier(source.EditorTargetId, path + ".editor_target_id", errors);
        }

        private static void ValidateTerminalState(LlmResultRecord record, ICollection<AgentJournalValidationError> errors)
        {
            bool hasOutput = record.OutputText != null;
            bool hasValidation = !string.IsNullOrWhiteSpace(record.ValidationCode);
            bool hasError = !string.IsNullOrWhiteSpace(record.ErrorCode);
            bool valid;
            switch (record.TerminalStatus)
            {
                case AgentJournalTerminalStatus.Succeeded:
                    valid = hasOutput && !hasError;
                    break;
                case AgentJournalTerminalStatus.Failed:
                    valid = !hasOutput && !hasValidation && hasError;
                    break;
                case AgentJournalTerminalStatus.Cancelled:
                    valid = !hasOutput && !hasValidation;
                    break;
                case AgentJournalTerminalStatus.Rejected:
                    valid = !hasOutput && hasValidation && !hasError;
                    break;
                default:
                    valid = false;
                    break;
            }
            if (!valid)
            {
                errors.Add(new AgentJournalValidationError(InvalidStatusTransition, "$.terminal_status"));
            }
        }

        private static void AddMissing(string? value, string path, ICollection<AgentJournalValidationError> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new AgentJournalValidationError(MissingId, path));
            }
        }

        private static void AddNegative(int? value, string path, ICollection<AgentJournalValidationError> errors)
        {
            if (value.HasValue && value.Value < 0)
            {
                errors.Add(new AgentJournalValidationError(NegativeUsage, path));
            }
        }

        private static void AddOpaqueIdentifier(
            string? value,
            string path,
            ICollection<AgentJournalValidationError> errors)
        {
            string? errorCode = AgentJournalSourceIdentifierPolicy.GetErrorCode(value);
            if (errorCode != null)
            {
                errors.Add(new AgentJournalValidationError(errorCode, path));
            }
        }

        private static void AddForbiddenLink(string? value, string path, ICollection<AgentJournalValidationError> errors)
        {
            string? errorCode = AgentJournalSourceIdentifierPolicy.GetErrorCode(value);
            if (string.Equals(errorCode, ForbiddenSourceLink, StringComparison.Ordinal))
            {
                errors.Add(new AgentJournalValidationError(errorCode, path));
            }
        }

        private static void AddSurrogateSplit(
            string text,
            int boundary,
            string path,
            ICollection<AgentJournalValidationError> errors)
        {
            if (boundary > 0
                && boundary < text.Length
                && char.IsHighSurrogate(text[boundary - 1])
                && char.IsLowSurrogate(text[boundary]))
            {
                errors.Add(new AgentJournalValidationError(SurrogateSplitRange, path));
            }
        }
    }
}

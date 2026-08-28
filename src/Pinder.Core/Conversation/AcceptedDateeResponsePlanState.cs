using System;

namespace Pinder.Core.Conversation
{
    /// <summary>Versioned private provenance retained with an accepted DATEE plan.</summary>
    public sealed class DateeResponsePlanProvenance
    {
        public const int CurrentSchemaVersion = 1;

        public DateeResponsePlanProvenance(
            string sourceArtifactId,
            string compilerArtifactId,
            string acceptedArtifactId,
            string? reconciliationInvocationId = null,
            string? reconciliationResultId = null,
            int schemaVersion = CurrentSchemaVersion)
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException("datee_response_plan_provenance.schema_version.unsupported");
            if ((reconciliationInvocationId == null) != (reconciliationResultId == null))
                throw new InvalidOperationException("datee_response_plan_provenance.reconciliation_chain.incomplete");

            SchemaVersion = schemaVersion;
            SourceArtifactId = Required(sourceArtifactId, nameof(sourceArtifactId));
            CompilerArtifactId = Required(compilerArtifactId, nameof(compilerArtifactId));
            AcceptedArtifactId = Required(acceptedArtifactId, nameof(acceptedArtifactId));
            ReconciliationInvocationId = Optional(reconciliationInvocationId);
            ReconciliationResultId = Optional(reconciliationResultId);
        }

        public int SchemaVersion { get; }
        public string SourceArtifactId { get; }
        public string CompilerArtifactId { get; }
        public string AcceptedArtifactId { get; }
        public string? ReconciliationInvocationId { get; }
        public string? ReconciliationResultId { get; }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;

        private static string? Optional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Immutable snapshot payload for one accepted plan. The canonical JSON and
    /// explicit turn/message identity prevent a restored artifact from drifting
    /// into a later ordinary turn.
    /// </summary>
    public sealed class AcceptedDateeResponsePlanState
    {
        public const int CurrentSchemaVersion = 1;

        public AcceptedDateeResponsePlanState(
            string canonicalPlanJson,
            int originatingTurn,
            string messageReference,
            string visibleMessageText,
            DateeResponsePlanProvenance provenance,
            int schemaVersion = CurrentSchemaVersion)
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException("accepted_datee_response_plan.schema_version.unsupported");
            if (originatingTurn < 0)
                throw new ArgumentOutOfRangeException(nameof(originatingTurn));

            DateeResponsePlan plan = DateeResponsePlanJson.ParseStrict(Required(canonicalPlanJson, nameof(canonicalPlanJson)));
            string canonical = DateeResponsePlanJson.Serialize(plan);
            if (!string.Equals(canonicalPlanJson, canonical, StringComparison.Ordinal))
                throw new InvalidOperationException("accepted_datee_response_plan.canonical_json.required");
            if (plan.VisibleEvidence.MessageReference.Turn != originatingTurn
                || !string.Equals(plan.VisibleEvidence.MessageReference.Value, messageReference, StringComparison.Ordinal)
                || !string.Equals(plan.VisibleEvidence.Text, visibleMessageText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("accepted_datee_response_plan.identity.mismatch");
            }

            SchemaVersion = schemaVersion;
            CanonicalPlanJson = canonicalPlanJson;
            Plan = plan;
            OriginatingTurn = originatingTurn;
            MessageReference = Required(messageReference, nameof(messageReference));
            VisibleMessageText = Required(visibleMessageText, nameof(visibleMessageText));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        }

        public static AcceptedDateeResponsePlanState Create(
            DateeResponsePlan plan,
            DateeResponsePlanProvenance provenance)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return new AcceptedDateeResponsePlanState(
                DateeResponsePlanJson.Serialize(plan),
                plan.VisibleEvidence.MessageReference.Turn,
                plan.VisibleEvidence.MessageReference.Value,
                plan.VisibleEvidence.Text,
                provenance);
        }

        public int SchemaVersion { get; }
        public string CanonicalPlanJson { get; }
        public DateeResponsePlan Plan { get; }
        public int OriginatingTurn { get; }
        public string MessageReference { get; }
        public string VisibleMessageText { get; }
        public DateeResponsePlanProvenance Provenance { get; }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;
    }

    /// <summary>Explicit opt-in selecting one accepted artifact for response-only replay.</summary>
    public sealed class DateeResponsePlanReplaySelection
    {
        public const int CurrentSchemaVersion = 1;

        public DateeResponsePlanReplaySelection(
            int originatingTurn,
            string messageReference,
            string visibleMessageText,
            string acceptedArtifactId,
            int schemaVersion = CurrentSchemaVersion)
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException("datee_response_plan_replay.schema_version.unsupported");
            if (originatingTurn < 0) throw new ArgumentOutOfRangeException(nameof(originatingTurn));
            SchemaVersion = schemaVersion;
            OriginatingTurn = originatingTurn;
            MessageReference = Required(messageReference, nameof(messageReference));
            VisibleMessageText = Required(visibleMessageText, nameof(visibleMessageText));
            AcceptedArtifactId = Required(acceptedArtifactId, nameof(acceptedArtifactId));
        }

        public int SchemaVersion { get; }
        public int OriginatingTurn { get; }
        public string MessageReference { get; }
        public string VisibleMessageText { get; }
        public string AcceptedArtifactId { get; }

        public static DateeResponsePlanReplaySelection From(AcceptedDateeResponsePlanState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return new DateeResponsePlanReplaySelection(
                state.OriginatingTurn,
                state.MessageReference,
                state.VisibleMessageText,
                state.Provenance.AcceptedArtifactId);
        }

        public bool Selects(AcceptedDateeResponsePlanState state, string deliveredMessage)
            => state != null
                && OriginatingTurn == state.OriginatingTurn
                && string.Equals(MessageReference, state.MessageReference, StringComparison.Ordinal)
                && string.Equals(VisibleMessageText, deliveredMessage, StringComparison.Ordinal)
                && string.Equals(VisibleMessageText, state.VisibleMessageText, StringComparison.Ordinal)
                && string.Equals(AcceptedArtifactId, state.Provenance.AcceptedArtifactId, StringComparison.Ordinal);

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;
    }
}

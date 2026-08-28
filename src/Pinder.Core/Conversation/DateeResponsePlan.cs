using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Pinder.Core.Conversation
{
    public enum DateeResponseMovement { Open, Hold, Withdraw, Mixed }
    public enum DateeResponseDisclosure { None, Voluntary, Involuntary }
    public enum DateeConversationalMove { Reveal, Challenge, Tease, Misunderstand, Escalate, Reverse, Redirect }
    public enum DateePlanConstraintSeverity { Hard, Soft }
    public enum DateePlanSourceKind { VisibleMessage, RelationshipState, EmotionalDirection, RevelationTarget, CognitivePressure, Trap, Archetype, DramaticArc, Reconciliation }

    public sealed class DateeResponsePlanContractException : InvalidOperationException
    {
        public DateeResponsePlanContractException(string code, string message, string? sourceId = null, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = Required(code, nameof(code));
            SourceId = sourceId;
        }

        public string Code { get; }
        public string? SourceId { get; }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;
    }

    public sealed class DateeVisibleEvidence
    {
        public DateeVisibleEvidence(string text, ConversationMessageReference messageReference)
        {
            Text = Required(text, nameof(text));
            MessageReference = messageReference ?? throw new ArgumentNullException(nameof(messageReference));
        }

        public string Text { get; }
        public ConversationMessageReference MessageReference { get; }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;
    }

    public sealed class DateeResponseMovementStage
    {
        public DateeResponseMovementStage(DateeResponseMovement movement, bool ownsDisclosure)
        {
            if (movement == DateeResponseMovement.Mixed || !Enum.IsDefined(typeof(DateeResponseMovement), movement))
                throw new ArgumentOutOfRangeException(nameof(movement));
            Movement = movement;
            OwnsDisclosure = ownsDisclosure;
        }

        public DateeResponseMovement Movement { get; }
        public bool OwnsDisclosure { get; }
    }

    public sealed class DateeResponsePlanConstraint
    {
        public DateeResponsePlanConstraint(string id, DateePlanConstraintSeverity severity, string sourceId, string? value = null)
        {
            Id = Required(id, nameof(id));
            if (!Enum.IsDefined(typeof(DateePlanConstraintSeverity), severity))
                throw new ArgumentOutOfRangeException(nameof(severity));
            Severity = severity;
            SourceId = Required(sourceId, nameof(sourceId));
            Value = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public string Id { get; }
        public DateePlanConstraintSeverity Severity { get; }
        public string SourceId { get; }
        public string? Value { get; }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;
    }

    public sealed class DateeResponsePlanSource
    {
        public DateeResponsePlanSource(string id, DateePlanSourceKind kind)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A non-empty source id is required.", nameof(id)) : id;
            if (!Enum.IsDefined(typeof(DateePlanSourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
        }

        public string Id { get; }
        public DateePlanSourceKind Kind { get; }
    }

    /// <summary>Immutable provider-neutral private behavior plan for one DATEE response.</summary>
    public sealed class DateeResponsePlan
    {
        public const string CurrentSchemaVersion = "datee_response_plan.v1";

        public DateeResponsePlan(
            DateeVisibleEvidence visibleEvidence,
            string dateeInterpretation,
            DateeResponseMovement movement,
            IReadOnlyList<DateeResponseMovementStage>? movementStages,
            string primaryEmotion,
            string secondaryEmotion,
            string regulatoryState,
            int activation,
            string trajectory,
            DateeResponseDisclosure disclosure,
            DateeConversationalMove conversationalMove,
            string? dramaticArcSourceId,
            IReadOnlyList<DateeResponsePlanConstraint> constraints,
            IReadOnlyList<DateeResponsePlanSource> sources)
        {
            VisibleEvidence = visibleEvidence ?? throw new ArgumentNullException(nameof(visibleEvidence));
            DateeInterpretation = Required(dateeInterpretation, nameof(dateeInterpretation));
            Movement = movement;
            MovementStages = Snapshot(movementStages);
            PrimaryEmotion = Required(primaryEmotion, nameof(primaryEmotion));
            SecondaryEmotion = Required(secondaryEmotion, nameof(secondaryEmotion));
            RegulatoryState = Required(regulatoryState, nameof(regulatoryState));
            Activation = activation;
            Trajectory = Required(trajectory, nameof(trajectory));
            Disclosure = disclosure;
            ConversationalMove = conversationalMove;
            DramaticArcSourceId = string.IsNullOrWhiteSpace(dramaticArcSourceId) ? null : dramaticArcSourceId;
            Constraints = Snapshot(constraints);
            Sources = Snapshot(sources);
            DateeResponsePlanValidator.Validate(this);
        }

        public string SchemaVersion => CurrentSchemaVersion;
        public DateeVisibleEvidence VisibleEvidence { get; }
        public string DateeInterpretation { get; }
        public DateeResponseMovement Movement { get; }
        public IReadOnlyList<DateeResponseMovementStage> MovementStages { get; }
        public string PrimaryEmotion { get; }
        public string SecondaryEmotion { get; }
        public string RegulatoryState { get; }
        public int Activation { get; }
        public string Trajectory { get; }
        public DateeResponseDisclosure Disclosure { get; }
        public DateeConversationalMove ConversationalMove { get; }
        public string? DramaticArcSourceId { get; }
        public IReadOnlyList<DateeResponsePlanConstraint> Constraints { get; }
        public IReadOnlyList<DateeResponsePlanSource> Sources { get; }

        private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? values)
            => new ReadOnlyCollection<T>((values ?? Array.Empty<T>()).ToArray());

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", name) : value;
    }

    public static class DateeResponsePlanValidator
    {
        public static void Validate(DateeResponsePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            RequireEnum(plan.Movement, "movement.invalid");
            RequireEnum(plan.Disclosure, "disclosure.invalid");
            RequireEnum(plan.ConversationalMove, "conversational_move.invalid");
            if (plan.Activation < 1 || plan.Activation > 5)
                throw Invalid("activation.invalid", "Activation must be between 1 and 5.");
            if (!plan.DateeInterpretation.StartsWith("Possibly, ", StringComparison.Ordinal))
                throw Invalid("interpretation.certainty_forbidden", "DATEE interpretation must be explicitly uncertain.");
            if (plan.Sources.Count == 0)
                throw Invalid("sources.required", "At least one source is required.");

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DateeResponsePlanSource source in plan.Sources)
            {
                if (!sourceIds.Add(source.Id))
                    throw Invalid("sources.duplicate", "Plan source ids must be unique.", source.Id);
            }
            if (!sourceIds.Contains(plan.VisibleEvidence.MessageReference.Value))
                throw Invalid("visible_evidence.source_missing", "Visible evidence provenance must appear in sources.", plan.VisibleEvidence.MessageReference.Value);
            if (plan.DramaticArcSourceId != null && !sourceIds.Contains(plan.DramaticArcSourceId))
                throw Invalid("dramatic_arc.source_missing", "Dramatic arc reference must appear in sources.", plan.DramaticArcSourceId);

            var constraintIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DateeResponsePlanConstraint constraint in plan.Constraints)
            {
                if (!constraintIds.Add(constraint.Id))
                    throw Invalid("constraints.duplicate", "Constraint ids must be unique.", constraint.Id);
                if (!sourceIds.Contains(constraint.SourceId))
                    throw Invalid("constraint.source_missing", "Constraint source must appear in sources.", constraint.SourceId);
            }

            if (plan.Movement == DateeResponseMovement.Mixed)
            {
                if (plan.MovementStages.Count != 2)
                    throw Invalid("movement_stages.count", "Mixed movement requires exactly two stages.");
                if (plan.MovementStages[0].Movement == plan.MovementStages[1].Movement)
                    throw Invalid("movement_stages.distinct", "Mixed movement stages must be different.");
                int owners = plan.MovementStages.Count(stage => stage.OwnsDisclosure);
                if (plan.Disclosure == DateeResponseDisclosure.None && owners != 0)
                    throw Invalid("movement_stages.disclosure_owner_unexpected", "A no-disclosure plan cannot assign a disclosure stage.");
                if (plan.Disclosure != DateeResponseDisclosure.None && owners != 1)
                    throw Invalid("movement_stages.disclosure_owner_required", "A mixed disclosure plan requires exactly one disclosure-owning stage.");
            }
            else if (plan.MovementStages.Count != 0)
            {
                throw Invalid("movement_stages.non_mixed", "Non-mixed movement cannot contain stages.");
            }

            if (plan.Disclosure != DateeResponseDisclosure.None && plan.ConversationalMove != DateeConversationalMove.Reveal)
                throw Invalid("disclosure.move_incompatible", "Disclosure plans must use the reveal conversational move.");
            if (plan.Disclosure == DateeResponseDisclosure.Involuntary
                && !plan.Constraints.Any(c => string.Equals(c.Id, "disclosure.traumatic_leakage", StringComparison.Ordinal)))
                throw Invalid("disclosure.involuntary_source_missing", "Involuntary disclosure requires traumatic-leakage provenance.");
        }

        private static void RequireEnum<T>(T value, string code) where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value)) throw Invalid(code, "Unknown enum value.");
        }

        private static DateeResponsePlanContractException Invalid(string code, string message, string? sourceId = null)
            => new DateeResponsePlanContractException("datee_response_plan_incompatible." + code, message, sourceId);
    }

    /// <summary>Canonical strict JSON representation used by snapshots, journals and reconciliation.</summary>
    public static class DateeResponsePlanJson
    {
        private static readonly string[] RootFields =
        {
            "schema_version", "visible_evidence", "datee_interpretation", "movement", "movement_stages",
            "primary_emotion", "secondary_emotion", "regulatory_state", "activation", "trajectory",
            "disclosure", "conversational_move", "dramatic_arc_source_id", "constraints", "sources"
        };

        public static string Serialize(DateeResponsePlan plan)
        {
            DateeResponsePlanValidator.Validate(plan);
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("schema_version", DateeResponsePlan.CurrentSchemaVersion);
                    writer.WriteStartObject("visible_evidence");
                    writer.WriteString("text", plan.VisibleEvidence.Text);
                    writer.WriteString("message_reference", plan.VisibleEvidence.MessageReference.Value);
                    writer.WriteEndObject();
                    writer.WriteString("datee_interpretation", plan.DateeInterpretation);
                    writer.WriteString("movement", Token(plan.Movement));
                    writer.WriteStartArray("movement_stages");
                    foreach (DateeResponseMovementStage stage in plan.MovementStages)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("movement", Token(stage.Movement));
                        writer.WriteBoolean("owns_disclosure", stage.OwnsDisclosure);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteString("primary_emotion", plan.PrimaryEmotion);
                    writer.WriteString("secondary_emotion", plan.SecondaryEmotion);
                    writer.WriteString("regulatory_state", plan.RegulatoryState);
                    writer.WriteNumber("activation", plan.Activation);
                    writer.WriteString("trajectory", plan.Trajectory);
                    writer.WriteString("disclosure", Token(plan.Disclosure));
                    writer.WriteString("conversational_move", Token(plan.ConversationalMove));
                    if (plan.DramaticArcSourceId == null) writer.WriteNull("dramatic_arc_source_id");
                    else writer.WriteString("dramatic_arc_source_id", plan.DramaticArcSourceId);
                    writer.WriteStartArray("constraints");
                    foreach (DateeResponsePlanConstraint constraint in plan.Constraints)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", constraint.Id);
                        writer.WriteString("severity", Token(constraint.Severity));
                        writer.WriteString("source_id", constraint.SourceId);
                        if (constraint.Value == null) writer.WriteNull("value"); else writer.WriteString("value", constraint.Value);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteStartArray("sources");
                    foreach (DateeResponsePlanSource source in plan.Sources)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", source.Id);
                        writer.WriteString("kind", Token(source.Kind));
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static DateeResponsePlan ParseStrict(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Invalid("json.empty", "Plan JSON is empty.");
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = RequireObject(document.RootElement, "root");
                    RequireOnly(root, RootFields);
                    RequireString(root, "schema_version", DateeResponsePlan.CurrentSchemaVersion);
                    JsonElement evidence = RequireObject(root.GetProperty("visible_evidence"), "visible_evidence");
                    RequireOnly(evidence, "text", "message_reference");
                    var visible = new DateeVisibleEvidence(
                        RequireString(evidence, "text"),
                        ConversationMessageReference.Parse(RequireString(evidence, "message_reference")));
                    var stages = new List<DateeResponseMovementStage>();
                    foreach (JsonElement item in RequireArray(root, "movement_stages").EnumerateArray())
                    {
                        JsonElement stage = RequireObject(item, "movement_stages[]");
                        RequireOnly(stage, "movement", "owns_disclosure");
                        stages.Add(new DateeResponseMovementStage(
                            ParseEnum<DateeResponseMovement>(RequireString(stage, "movement"), "movement"),
                            RequireBoolean(stage, "owns_disclosure")));
                    }
                    var constraints = new List<DateeResponsePlanConstraint>();
                    foreach (JsonElement item in RequireArray(root, "constraints").EnumerateArray())
                    {
                        JsonElement constraint = RequireObject(item, "constraints[]");
                        RequireOnly(constraint, "id", "severity", "source_id", "value");
                        constraints.Add(new DateeResponsePlanConstraint(
                            RequireString(constraint, "id"),
                            ParseEnum<DateePlanConstraintSeverity>(RequireString(constraint, "severity"), "severity"),
                            RequireString(constraint, "source_id"),
                            OptionalString(constraint, "value")));
                    }
                    var sources = new List<DateeResponsePlanSource>();
                    foreach (JsonElement item in RequireArray(root, "sources").EnumerateArray())
                    {
                        JsonElement source = RequireObject(item, "sources[]");
                        RequireOnly(source, "id", "kind");
                        sources.Add(new DateeResponsePlanSource(
                            RequireString(source, "id"),
                            ParseEnum<DateePlanSourceKind>(RequireString(source, "kind"), "kind")));
                    }
                    int activation = RequireInt32(root, "activation");
                    return new DateeResponsePlan(
                        visible,
                        RequireString(root, "datee_interpretation"),
                        ParseEnum<DateeResponseMovement>(RequireString(root, "movement"), "movement"),
                        stages,
                        RequireString(root, "primary_emotion"),
                        RequireString(root, "secondary_emotion"),
                        RequireString(root, "regulatory_state"),
                        activation,
                        RequireString(root, "trajectory"),
                        ParseEnum<DateeResponseDisclosure>(RequireString(root, "disclosure"), "disclosure"),
                        ParseEnum<DateeConversationalMove>(RequireString(root, "conversational_move"), "conversational_move"),
                        OptionalString(root, "dramatic_arc_source_id"),
                        constraints,
                        sources);
                }
            }
            catch (DateeResponsePlanContractException) { throw; }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is FormatException || ex is ArgumentException)
            {
                throw Invalid("json.invalid", "Plan JSON is invalid: " + ex.Message, inner: ex);
            }
        }

        public static string Token<T>(T value) where T : struct
        {
            string token = value.ToString();
            var builder = new StringBuilder(token.Length + 4);
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (char.IsUpper(c) && i > 0) builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        private static T ParseEnum<T>(string token, string field) where T : struct
        {
            foreach (T value in Enum.GetValues(typeof(T)))
                if (string.Equals(Token(value), token, StringComparison.Ordinal)) return value;
            throw Invalid(field + ".invalid", "Unknown " + field + " value.");
        }

        private static JsonElement RequireObject(JsonElement value, string field)
            => value.ValueKind == JsonValueKind.Object ? value : throw Invalid(field + ".type", field + " must be an object.");
        private static JsonElement RequireArray(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
                throw Invalid(name + ".type", name + " must be an array.");
            return value;
        }
        private static string RequireString(JsonElement parent, string name, string? expected = null)
        {
            if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                throw Invalid(name + ".type", name + " must be a string.");
            string result = value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(result) || (expected != null && !string.Equals(result, expected, StringComparison.Ordinal)))
                throw Invalid(name + ".invalid", name + " has an invalid value.");
            return result;
        }
        private static string? OptionalString(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value)) throw Invalid(name + ".required", name + " is required.");
            if (value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String) throw Invalid(name + ".type", name + " must be a string or null.");
            string? result = value.GetString();
            return string.IsNullOrWhiteSpace(result) ? throw Invalid(name + ".invalid", name + " cannot be empty.") : result;
        }
        private static int RequireInt32(JsonElement parent, string name)
            => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
                ? result : throw Invalid(name + ".type", name + " must be an integer.");
        private static bool RequireBoolean(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value) || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                throw Invalid(name + ".type", name + " must be boolean.");
            return value.GetBoolean();
        }
        private static void RequireOnly(JsonElement value, params string[] allowed)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("json.duplicate_property", "Duplicate property '" + property.Name + "'.");
                if (!allowedSet.Contains(property.Name)) throw Invalid("json.unexpected_property", "Unexpected property '" + property.Name + "'.");
            }
            foreach (string name in allowed)
                if (!names.Contains(name)) throw Invalid(name + ".required", "Required property '" + name + "' is missing.");
        }
        private static DateeResponsePlanContractException Invalid(string code, string message, Exception? inner = null)
            => new DateeResponsePlanContractException("datee_response_plan_incompatible." + code, message, innerException: inner);
    }
}

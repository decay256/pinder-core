using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Runtime contract for the flat therapist diagnosis map generated during
    /// character synthesis and consumed by cognitive subtext. Validation
    /// requires the canonical fields but intentionally tolerates extras;
    /// generation, loading, and authored-schema admission apply their own
    /// boundary-specific unknown-field policies.
    /// </summary>
    public static class TherapistDiagnosisContract
    {
        public const string DerivedFeelingKey = "derived_feeling";
        public const string DefenseReactionKey = "defense_reaction";

        public const string MissingDiagnosisCode = "diagnosis_missing";
        public const string MissingRequiredFieldCode = "diagnosis_required_field_missing";
        public const string BlankRequiredFieldCode = "diagnosis_required_field_blank";

        private static readonly string[] RequiredFieldsArray =
        {
            DerivedFeelingKey,
            DefenseReactionKey,
        };

        public static IReadOnlyList<string> RequiredFields { get; } =
            Array.AsReadOnly(RequiredFieldsArray);

        public static TherapistDiagnosisValidationResult ValidateRequiredFields(
            IReadOnlyDictionary<string, string>? diagnosis)
        {
            if (diagnosis == null)
            {
                return TherapistDiagnosisValidationResult.Invalid(
                    new TherapistDiagnosisViolation(
                        MissingDiagnosisCode,
                        DerivedFeelingKey,
                        "Therapist diagnosis is missing."));
            }

            foreach (string requiredField in RequiredFieldsArray)
            {
                if (!diagnosis.TryGetValue(requiredField, out var value))
                {
                    return TherapistDiagnosisValidationResult.Invalid(
                        new TherapistDiagnosisViolation(
                            MissingRequiredFieldCode,
                            requiredField,
                            $"Therapist diagnosis is missing required field '{requiredField}'."));
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    return TherapistDiagnosisValidationResult.Invalid(
                        new TherapistDiagnosisViolation(
                            BlankRequiredFieldCode,
                            requiredField,
                            $"Therapist diagnosis required field '{requiredField}' must be nonblank."));
                }
            }

            return TherapistDiagnosisValidationResult.Valid;
        }
    }

    public sealed class TherapistDiagnosisValidationResult
    {
        private static readonly TherapistDiagnosisValidationResult ValidResult =
            new TherapistDiagnosisValidationResult(null);

        private TherapistDiagnosisValidationResult(TherapistDiagnosisViolation? violation)
        {
            Violation = violation;
        }

        public bool IsValid => Violation == null;

        public TherapistDiagnosisViolation? Violation { get; }

        public static TherapistDiagnosisValidationResult Valid => ValidResult;

        public static TherapistDiagnosisValidationResult Invalid(
            TherapistDiagnosisViolation violation)
        {
            if (violation == null) throw new ArgumentNullException(nameof(violation));
            return new TherapistDiagnosisValidationResult(violation);
        }
    }

    public sealed class TherapistDiagnosisViolation
    {
        public TherapistDiagnosisViolation(string code, string field, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public string Code { get; }

        public string Field { get; }

        public string Message { get; }
    }
}

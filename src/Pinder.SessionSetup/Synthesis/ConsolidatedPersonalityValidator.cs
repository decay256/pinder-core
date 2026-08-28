using System;
using Pinder.Core.Prompts;

namespace Pinder.SessionSetup
{
    public static class ConsolidatedPersonalityValidator
    {
        public static ConsolidatedPersonalityValidationResult Validate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ConsolidatedPersonalityValidationResult.Invalid("content.empty");

            string? violation = PersonalitySurfaceStyleDetector.FindViolation(value);
            return violation == null
                ? ConsolidatedPersonalityValidationResult.Valid()
                : ConsolidatedPersonalityValidationResult.Invalid(violation);
        }
    }

    public sealed class ConsolidatedPersonalityValidationResult
    {
        private ConsolidatedPersonalityValidationResult(bool isValid, string? violationCode)
        {
            IsValid = isValid;
            ViolationCode = violationCode;
        }
        public bool IsValid { get; }
        public string? ViolationCode { get; }
        public static ConsolidatedPersonalityValidationResult Valid() => new ConsolidatedPersonalityValidationResult(true, null);
        public static ConsolidatedPersonalityValidationResult Invalid(string violationCode) => new ConsolidatedPersonalityValidationResult(false, violationCode);
    }

    public sealed class PersonalityConsolidationContractException : InvalidOperationException
    {
        public PersonalityConsolidationContractException(string violationCode)
            : base("Personality consolidation output violated the behavioral-layer contract. Code=" + violationCode + ".")
        {
            ViolationCode = violationCode ?? throw new ArgumentNullException(nameof(violationCode));
        }
        public string ViolationCode { get; }
    }
}

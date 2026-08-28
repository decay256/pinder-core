using System;
using System.Text.RegularExpressions;

namespace Pinder.SessionSetup
{
    public static class ConsolidatedPersonalityValidator
    {
        private static readonly Rule[] SurfaceRules =
        {
            new Rule("surface.punctuation", @"\b(?:ellipsis|three dots|punctuation|periods?|question marks?|exclamation marks?)\b|\bends?\s+with\b|\bfinish(?:es)?\s+with\b"),
            new Rule("surface.casing", @"\b(?:lowercase|upper ?case|capital letters|all caps|casing)\b"),
            new Rule("surface.line_break", @"\b(?:line breaks?|new lines?|paragraph breaks?)\b"),
            new Rule("surface.emoji", @"\b(?:emoji|emoticons?)\b"),
            new Rule("surface.fixed_opening", @"\b(?:open|opens|start|starts|begin|begins)\s+(?:every\s+)?(?:reply|message|text|with)\b|\b(?:replies|messages|texts)\s+open\b"),
            new Rule("surface.sentence_template", @"\b(?:one sentence|two sentences|three sentences|sentence template|template|cadence|message format|clipped)\b"),
        };

        public static ConsolidatedPersonalityValidationResult Validate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ConsolidatedPersonalityValidationResult.Invalid("content.empty");

            foreach (Rule rule in SurfaceRules)
            {
                if (rule.Pattern.IsMatch(value))
                    return ConsolidatedPersonalityValidationResult.Invalid(rule.Code);
            }
            return ConsolidatedPersonalityValidationResult.Valid();
        }

        private sealed class Rule
        {
            public Rule(string code, string pattern)
            {
                Code = code;
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            public string Code { get; }
            public Regex Pattern { get; }
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

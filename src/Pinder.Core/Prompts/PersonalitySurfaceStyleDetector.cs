using System;
using System.Text.RegularExpressions;

namespace Pinder.Core.Prompts
{
    /// <summary>
    /// Shared deterministic ownership check used by synthesis output validation and
    /// prompt-catalog publication. Only a meta-level rejection governing the matched
    /// style clause is exempt; prescribing absence of a surface feature remains authority.
    /// </summary>
    public static class PersonalitySurfaceStyleDetector
    {
        private const string PrescriptionActionOrSubject =
            @"(?:use|uses|include|includes|follow|follows|end|ends|finish|finishes|write|writes|make|makes|keep|keeps|put|puts|add|adds|insert|inserts|open|opens|start|starts|begin|begins|each|every|all|reply|replies|messages?|texts?)";
        private const string PrescriptionAdverb =
            @"(?:always|never|only|usually|typically|generally|consistently|often)";
        private const string PrescriptionActor = @"(?:(?:they|he|she|it|you|we)\s+)?";
        private const string PrescriptionAdverbPrefix = @"(?:" + PrescriptionAdverb + @"\s+)*";
        private const string PositiveModalPrefix = @"(?:(?:must|will|should|would|can|could)\s+" + PrescriptionAdverbPrefix + @")?";
        private const string NegativeAuxiliaryPrefix = @"(?:(?:do|does|did|should|must|will|would|can|could)\s+" + PrescriptionAdverbPrefix + @"(?:not|never)\s+)";
        private const string PrescriptionLeadPrefix = PrescriptionActor + PrescriptionAdverbPrefix + @"(?:" + NegativeAuxiliaryPrefix + @"|" + PositiveModalPrefix + @")";
        private const string PrescriptionClauseLead = PrescriptionLeadPrefix + PrescriptionActionOrSubject;
        private const string MetaAntiMandateVerbLead =
            @"(?:reject|refuse|ignore|resist|oppose|avoid)(?:s|ed|ing)?";
        private const string MetaAntiMandateGovernanceNoun =
            @"(?:instructions?|rules?|mandates?|directives?|requirements?)";
        private const string MetaAntiMandateAuxiliaryLead =
            @"(?:do|does|did|should|must|will|would|can|could)\s+" + PrescriptionAdverbPrefix + @"not\s+(?:(?:require|instruct|mandate|force|tell)|(?:be\s+)?(?:required|instructed|forced|told|bound))";
        private const string MetaAntiMandatePassiveLead =
            @"not\s+(?:be\s+)?(?:required|instructed|forced|told|bound)";
        private const string MetaAntiMandateClauseLead = PrescriptionActor + PrescriptionAdverbPrefix + @"(?:" + MetaAntiMandateVerbLead + @"|" + MetaAntiMandateAuxiliaryLead + @"|" + MetaAntiMandatePassiveLead + @")";
        private const string Directive = @"\b" + PrescriptionClauseLead + @"\b[^.\n]{0,96}";
        private const string Message = @"\b(?:reply|replies|messages?|texts?)\b";
        private static readonly Rule[] Rules =
        {
            new Rule("surface.punctuation", Directive + @"\b(?:punctuation|ellipsis|periods?|dots?|question marks?|exclamation marks?)\b|\b(?:every|each|all)\b[^.\n]{0,24}" + Message + @"[^.\n]{0,48}\b(?:end|ends|finish|finishes)\b[^.\n]{0,48}\b(?:punctuation|ellipsis|periods?|dots?|question marks?|exclamation marks?)\b|\b(?:end|finish)\s+(?:every|each|all)\s+" + Message + @"[^.\n]{0,48}\b(?:punctuation|ellipsis|periods?|dots?|question marks?|exclamation marks?)\b|" + Message + @"[^.\n]{0,40}\b(?:have|use|include|contain)\s+no\s+(?:punctuation|periods?|dots?|question marks?|exclamation marks?)\b"),
            new Rule("surface.casing", Directive + @"\b(?:lowercase|upper ?case|capital letters|all caps|casing)\b|" + Message + @"[^.\n]{0,24}\b(?:are|stay|remain)\b[^.\n]{0,24}\b(?:lowercase|upper ?case|all caps)\b|\b(?:write|make|keep|put)\b[^.\n]{0,32}" + Message + @"[^.\n]{0,32}\b(?:lowercase|upper ?case|all caps)\b|\breply\s+in\s+(?:lowercase|upper ?case|all caps)\b|\bnever\s+(?:use|include|write)\b[^.\n]{0,32}\b(?:lowercase|upper ?case|capital letters|all caps)\b[^.\n]{0,24}" + Message + @"|" + Message + @"[^.\n]{0,40}\b(?:have|use|include|contain)\s+no\s+(?:lowercase|upper ?case|capital letters|all caps|casing)\b"),
            new Rule("surface.line_break", Directive + @"\b(?:line breaks?|new lines?|paragraph breaks?)\b|\b(?:every|each|all)\b[^.\n]{0,24}" + Message + @"[^.\n]{0,48}\b(?:line breaks?|new lines?|paragraph breaks?)\b|" + Message + @"[^.\n]{0,40}\b(?:have|use|include|contain)\s+no\s+(?:line breaks?|new lines?|paragraph breaks?)\b"),
            new Rule("surface.emoji", Directive + @"\b(?:emoji|emoticon)s?\b|" + Message + @"[^.\n]{0,48}\b(?:emoji|emoticon)s?\b|\b(?:put|add|insert)\b[^.\n]{0,32}\b(?:emoji|emoticon)s?\b[^.\n]{0,32}\b(?:each|every|all)\b[^.\n]{0,16}" + Message + @"|" + Message + @"[^.\n]{0,40}\b(?:have|use|include|contain)\s+no\s+(?:emoji|emoticon)s?\b"),
            new Rule("surface.fixed_opening", @"\b" + PrescriptionLeadPrefix + @"(?:open|start|begin)\b[^.\n]{0,48}(?:" + Message + @"|\b(?:opening|greeting)s?\b)|" + Message + @"[^.\n]{0,24}\b(?:open|opens|start|starts|begin|begins)\b|\b(?:open|start|begin)s?\b[^.\n]{0,24}\b(?:every|each|all)\b[^.\n]{0,24}" + Message + @"|\b(?:open|start|begin)\s+(?:every|each|all)\s+" + Message + @"|" + Message + @"[^.\n]{0,40}\b(?:have|use)\s+no\s+(?:fixed\s+)?(?:opening|greeting)s?\b|" + Directive + @"\bwithout\s+(?:a\s+)?(?:fixed\s+)?(?:opening|greeting)\b"),
            new Rule("surface.sentence_template", Directive + @"\b(?:one sentence|two sentences|three sentences|sentence template|template|cadence|message format)\b|" + Message + @"[^.\n]{0,24}\b(?:are|stay|remain|follow)\b[^.\n]{0,40}\b(?:brief|short|clipped|terse|one sentence|two sentences|three sentences|template|cadence|format)\b|\b(?:use|follow|write|keep)\b[^.\n]{0,40}\b(?:brief|short|clipped|terse|template|cadence|message format|one sentence|two sentences|three sentences)\b|\bkeep\s+(?:every\s+|each\s+|all\s+)?" + Message + @"\s+(?:brief|short|clipped|terse)\b|" + Message + @"[^.\n]{0,40}\b(?:have|use|follow)\s+no\s+(?:fixed\s+)?(?:template|cadence|format)\b"),
        };

        private static readonly Regex MetaAntiMandate = new Regex(
            @"\b" + PrescriptionActor + PrescriptionAdverbPrefix + @"(?:(?:" + MetaAntiMandateVerbLead + @")\b[^.\n]{0,48}\b" + MetaAntiMandateGovernanceNoun + @"\b|" + MetaAntiMandateAuxiliaryLead + @"\b|" + MetaAntiMandatePassiveLead + @"\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ClauseBoundary = new Regex(
            @"[.!?;\n]+|,|\b(?:but|whereas|while|yet)\b|\band\b(?=\s+(?:" + PrescriptionClauseLead + @"\b|" + MetaAntiMandateClauseLead + @"\b))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string? FindViolation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            foreach (Rule rule in Rules)
            {
                if (HasUnexemptedMatch(value, rule))
                    return rule.Code;
            }
            return null;
        }

        private static bool HasUnexemptedMatch(string value, Rule rule)
        {
            int clauseStart = 0;
            foreach (Match boundary in ClauseBoundary.Matches(value))
            {
                if (ClauseHasUnexemptedMatch(value, clauseStart, boundary.Index - clauseStart, rule))
                    return true;
                clauseStart = boundary.Index + boundary.Length;
            }
            return ClauseHasUnexemptedMatch(value, clauseStart, value.Length - clauseStart, rule);
        }

        private static bool ClauseHasUnexemptedMatch(string value, int start, int length, Rule rule)
        {
            if (length <= 0) return false;
            string clause = value.Substring(start, length);
            return rule.Pattern.IsMatch(clause) && !MetaAntiMandate.IsMatch(clause);
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
}

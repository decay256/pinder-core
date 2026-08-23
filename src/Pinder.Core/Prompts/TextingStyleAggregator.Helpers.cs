using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Pinder.Core.Characters;

namespace Pinder.Core.Prompts
{
    public static partial class TextingStyleAggregator
    {
        // ------------------------------------------------------------------
        // Parsing helpers - extract axis maps from a single
        // texting_style_fragment block. The canonical block shape is:
        //
        //   SYNTAX:
        //   - emoji: <line>
        //   - shorthand: <line>
        //   - grammar: <line>
        //   - structure: <line>
        //   - length: <line>
        //   - tics: <line>
        //   TONE:
        //   - directness (<key>): <line>
        //   - affect (<key>): <line>
        //   - rhythm (<key>): <line>
        //
        // Until the content rewrite lands, legacy TONE axis keys are
        // accepted as input aliases: stance -> directness, register ->
        // affect, pacing -> rhythm. Parsed output is always canonical.
        // The parser is forgiving on whitespace and parenthesised sub-keys
        // and silently drops lines it cannot classify so future content
        // additions do not crash the pipeline.
        // ------------------------------------------------------------------

        private static readonly string[] SyntaxAxisNames =
        {
            "emoji", "shorthand", "grammar", "structure", "length", "tics",
        };

        private static readonly string[] ExpressionAxisNames =
        {
            "directness", "affect", "rhythm",
        };

        internal static string NormalizeExpressionAxisName(string axis)
        {
            if (string.Equals(axis, "stance", StringComparison.OrdinalIgnoreCase)) return "directness";
            if (string.Equals(axis, "register", StringComparison.OrdinalIgnoreCase)) return "affect";
            if (string.Equals(axis, "pacing", StringComparison.OrdinalIgnoreCase)) return "rhythm";
            return axis;
        }

        internal static IReadOnlyDictionary<string, string> ParseSyntaxAxes(string fragment)
            => FirstCandidates(ParseSyntaxAxisCandidates(fragment));

        internal static IReadOnlyDictionary<string, string> ParseToneAxes(string fragment)
            => FirstCandidates(ParseExpressionAxisCandidates(fragment));

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseSyntaxAxisCandidates(string fragment)
            => ParseAxisCandidates(
                fragment,
                new[] { "SYNTAX:" },
                SyntaxAxisNames,
                allowLegacyExpressionAliases: false);

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseExpressionAxisCandidates(string fragment)
            => ParseAxisCandidates(
                fragment,
                new[] { "EXPRESSION:", "TONE:" },
                ExpressionAxisNames,
                allowLegacyExpressionAliases: true);

        private static IReadOnlyDictionary<string, string> FirstCandidates(
            IReadOnlyDictionary<string, IReadOnlyList<string>> candidatesByAxis)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in candidatesByAxis)
            {
                if (pair.Value.Count > 0)
                    result[pair.Key] = pair.Value[0];
            }

            return result;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseAxisCandidates(
            string fragment,
            IReadOnlyList<string> sectionHeaders,
            string[] axisNames,
            bool allowLegacyExpressionAliases)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(fragment)) return ToReadOnly(result);

            bool inSection = false;
            string? currentAxis = null;
            var lines = fragment.Replace("\r\n", "\n").Split('\n');
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (IsTextingStyleSectionHeader(line))
                {
                    if (sectionHeaders.Any(header => string.Equals(header, line, StringComparison.Ordinal)))
                    {
                        if (!inSection)
                        {
                            inSection = true;
                            currentAxis = null;
                            continue;
                        }
                    }

                    if (inSection)
                        break;

                    continue;
                }

                if (!inSection) continue;
                if (!line.StartsWith("-", StringComparison.Ordinal)) continue;

                string bullet = line.Substring(1).Trim();
                if (bullet.Length == 0) continue;

                int colon = bullet.IndexOf(':');
                if (colon > 0)
                {
                    string axisToken = bullet.Substring(0, colon).Trim();
                    int paren = axisToken.IndexOf('(');
                    if (paren > 0) axisToken = axisToken.Substring(0, paren).Trim();
                    string value = bullet.Substring(colon + 1).Trim();
                    string? axis = CanonicalizeAxis(axisToken, axisNames, allowLegacyExpressionAliases);
                    if (axis != null)
                    {
                        currentAxis = axis;
                        if (value.Length > 0)
                            AddCandidate(result, axis, value);
                        continue;
                    }
                }

                if (CountLeadingWhitespace(rawLine) > 0 && currentAxis != null)
                {
                    AddCandidate(result, currentAxis, bullet);
                    continue;
                }

                currentAxis = null;
            }

            return ToReadOnly(result);
        }

        private static IReadOnlyDictionary<string, string> SelectAxisCandidates(
            IReadOnlyDictionary<string, IReadOnlyList<string>> candidatesByAxis,
            TextingStyleFragmentSource source,
            string? seedKey)
        {
            var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in candidatesByAxis)
            {
                if (pair.Value.Count == 0) continue;
                selected[pair.Key] = SelectCandidate(seedKey, source, pair.Key, pair.Value);
            }

            return selected;
        }

        private static string SelectCandidate(
            string? seedKey,
            TextingStyleFragmentSource source,
            string axis,
            IReadOnlyList<string> candidates)
        {
            if (candidates.Count == 1)
                return candidates[0];

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalSelector(seedKey, source, axis, candidates)));
                ulong value = 0;
                for (int i = 0; i < 8; i++)
                {
                    value = (value << 8) | hash[i];
                }

                return candidates[(int)(value % (ulong)candidates.Count)];
            }
        }

        private static string CanonicalSelector(
            string? seedKey,
            TextingStyleFragmentSource source,
            string axis,
            IReadOnlyList<string> candidates)
        {
            var sb = new StringBuilder();
            AppendSelectorPart(sb, "salt", "pinder-texting-style-v2");
            AppendSelectorPart(sb, "seedKey", seedKey ?? string.Empty);
            AppendSelectorPart(sb, "kind", source.Kind ?? string.Empty);
            AppendSelectorPart(sb, "source", source.Source ?? string.Empty);
            AppendSelectorPart(sb, "sourceId", source.SourceId ?? string.Empty);
            AppendSelectorPart(sb, "slotOrParameter", source.SlotOrParameter ?? string.Empty);
            AppendSelectorPart(sb, "bandIndex", source.BandIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-1");
            AppendSelectorPart(sb, "axis", axis ?? string.Empty);
            AppendSelectorPart(sb, "candidateCount", candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < candidates.Count; i++)
            {
                AppendSelectorPart(sb, "candidate" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), candidates[i] ?? string.Empty);
            }

            return sb.ToString();
        }

        private static void AppendSelectorPart(StringBuilder sb, string key, string value)
        {
            sb.Append(key);
            sb.Append(':');
            sb.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(':');
            sb.Append(value);
            sb.Append('\n');
        }

        private static void AddCandidate(Dictionary<string, List<string>> result, string axis, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!result.TryGetValue(axis, out var candidates))
            {
                candidates = new List<string>();
                result[axis] = candidates;
            }

            candidates.Add(value.Trim());
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToReadOnly(Dictionary<string, List<string>> result)
        {
            var readOnly = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in result)
            {
                readOnly[pair.Key] = pair.Value;
            }

            return readOnly;
        }

        private static bool IsTextingStyleSectionHeader(string line)
            => string.Equals(line, "SYNTAX:", StringComparison.Ordinal)
               || string.Equals(line, "EXPRESSION:", StringComparison.Ordinal)
               || string.Equals(line, "TONE:", StringComparison.Ordinal);

        private static int CountLeadingWhitespace(string value)
        {
            int count = 0;
            while (count < value.Length && char.IsWhiteSpace(value[count]))
                count++;
            return count;
        }

        private static string? CanonicalizeAxis(
            string axisToken,
            string[] axisNames,
            bool allowLegacyExpressionAliases)
        {
            foreach (var axis in axisNames)
            {
                if (string.Equals(axisToken, axis, StringComparison.OrdinalIgnoreCase))
                    return axis;
            }

            if (!allowLegacyExpressionAliases)
                return null;

            axisToken = NormalizeExpressionAxisName(axisToken);
            foreach (var axis in axisNames)
            {
                if (string.Equals(axisToken, axis, StringComparison.OrdinalIgnoreCase))
                    return axis;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Expression aggregation - majority vote across an anatomy group.
        // ------------------------------------------------------------------

        internal sealed class ExpressionVoteResult
        {
            public string WinnerLine { get; }
            public string ParamId { get; }

            public ExpressionVoteResult(string winnerLine, string paramId)
            {
                WinnerLine = winnerLine;
                ParamId = paramId;
            }
        }

        private static ExpressionVoteResult? MajorityVote(
            string axisName,
            IReadOnlyList<string> groupParams,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> expressionByParam)
        {
            // Tally per text. Keep a parallel "first source rank" so the
            // tie-break (group order) is correct: if two lines tie at the
            // highest count, the one whose earliest source-param sits
            // earlier in groupParams wins.
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var firstRank = new Dictionary<string, int>(StringComparer.Ordinal);
            var firstParamId = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int rank = 0; rank < groupParams.Count; rank++)
            {
                var paramId = groupParams[rank];
                if (!expressionByParam.TryGetValue(paramId, out var expressionAxes)) continue;
                if (!expressionAxes.TryGetValue(axisName, out var line)) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                counts.TryGetValue(line, out int c);
                counts[line] = c + 1;
                if (!firstRank.ContainsKey(line))
                {
                    firstRank[line] = rank;
                    firstParamId[line] = paramId;
                }
            }

            if (counts.Count == 0) return null;

            // Sort: most votes first, then earliest first-source rank.
            var winner = counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => firstRank[kv.Key])
                .First()
                .Key;

            return new ExpressionVoteResult($"{axisName}: {winner}", firstParamId[winner]);
        }

        // ------------------------------------------------------------------
        // Output ordering helpers.
        // ------------------------------------------------------------------

        private static string AxisOf(string axisPrefixedLine)
        {
            int colon = axisPrefixedLine.IndexOf(':');
            return colon > 0 ? axisPrefixedLine.Substring(0, colon) : axisPrefixedLine;
        }

        private static (string axis, string value) AxisValuePairOf(string axisPrefixedLine)
        {
            int colon = axisPrefixedLine.IndexOf(':');
            if (colon <= 0) return (axisPrefixedLine, string.Empty);
            return (
                axisPrefixedLine.Substring(0, colon).Trim(),
                axisPrefixedLine.Substring(colon + 1).Trim()
            );
        }
    }
}

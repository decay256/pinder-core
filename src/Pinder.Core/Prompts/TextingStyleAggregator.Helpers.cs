using System;
using System.Collections.Generic;
using System.Linq;

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
            => ParseAxes(fragment, "SYNTAX:", "TONE:", SyntaxAxisNames, allowLegacyExpressionAliases: false);

        internal static IReadOnlyDictionary<string, string> ParseToneAxes(string fragment)
            => ParseAxes(fragment, "TONE:", null, ExpressionAxisNames, allowLegacyExpressionAliases: true);

        private static IReadOnlyDictionary<string, string> ParseAxes(
            string fragment,
            string sectionHeader,
            string? endHeader,
            string[] axisNames,
            bool allowLegacyExpressionAliases)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(fragment)) return result;

            int sectionStart = fragment.IndexOf(sectionHeader, StringComparison.Ordinal);
            if (sectionStart < 0) return result;
            int bodyStart = sectionStart + sectionHeader.Length;

            int bodyEnd = fragment.Length;
            if (endHeader != null)
            {
                int endIdx = fragment.IndexOf(endHeader, bodyStart, StringComparison.Ordinal);
                if (endIdx >= 0) bodyEnd = endIdx;
            }

            string body = fragment.Substring(bodyStart, bodyEnd - bodyStart);
            var lines = body.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (!line.StartsWith("-", StringComparison.Ordinal)) continue;
                line = line.Substring(1).Trim();

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string axisToken = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (value.Length == 0) continue;

                // Axis token may carry a parenthesised sub-key, e.g.
                // "directness (guarded)" or legacy "stance (guarded)".
                int paren = axisToken.IndexOf('(');
                if (paren > 0) axisToken = axisToken.Substring(0, paren).Trim();

                string? axis = CanonicalizeAxis(axisToken, axisNames, allowLegacyExpressionAliases);
                if (axis == null) continue;
                if (!result.ContainsKey(axis))
                    result[axis] = value;
            }

            return result;
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Pinder.Core.Prompts
{
    public static partial class TextingStyleAggregator
    {
        // ------------------------------------------------------------------
        // Parsing helpers — extract axis maps from a single
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
        //   - stance (<key>): <line>
        //   - register (<key>): <line>
        //   - pacing (<key>): <line>
        //
        // The parser is forgiving on whitespace and parenthesised
        // sub-keys (e.g. "stance (escalator):") and silently drops lines
        // it can't classify so future content additions don't crash the
        // pipeline.
        // ------------------------------------------------------------------

        private static readonly string[] SyntaxAxisNames =
        {
            "emoji", "shorthand", "grammar", "structure", "length", "tics",
        };

        private static readonly string[] ToneAxisNames =
        {
            "stance", "register", "pacing",
        };

        internal static IReadOnlyDictionary<string, string> ParseSyntaxAxes(string fragment)
            => ParseAxes(fragment, "SYNTAX:", "TONE:", SyntaxAxisNames);

        internal static IReadOnlyDictionary<string, string> ParseToneAxes(string fragment)
            => ParseAxes(fragment, "TONE:", null, ToneAxisNames);

        private static IReadOnlyDictionary<string, string> ParseAxes(
            string fragment,
            string sectionHeader,
            string? endHeader,
            string[] axisNames)
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

                // axis token may carry a parenthesised sub-key, e.g.
                // "stance (escalator)". Strip it.
                int paren = axisToken.IndexOf('(');
                if (paren > 0) axisToken = axisToken.Substring(0, paren).Trim();

                foreach (var axis in axisNames)
                {
                    if (string.Equals(axisToken, axis, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!result.ContainsKey(axis))
                            result[axis] = value;
                        break;
                    }
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Tone aggregation — majority vote across an anatomy group.
        // ------------------------------------------------------------------

        private static string? MajorityVote(
            string axisName,
            IReadOnlyList<string> groupParams,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> toneByParam)
        {
            // Tally per text. Keep a parallel "first source rank" so the
            // tie-break (group order) is correct: if two lines tie at the
            // highest count, the one whose earliest source-param sits
            // earlier in groupParams wins.
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var firstRank = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int rank = 0; rank < groupParams.Count; rank++)
            {
                var paramId = groupParams[rank];
                if (!toneByParam.TryGetValue(paramId, out var tone)) continue;
                if (!tone.TryGetValue(axisName, out var line)) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                counts.TryGetValue(line, out int c);
                counts[line] = c + 1;
                if (!firstRank.ContainsKey(line))
                    firstRank[line] = rank;
            }

            if (counts.Count == 0) return null;

            // Sort: most votes first, then earliest first-source rank.
            var winner = counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => firstRank[kv.Key])
                .First()
                .Key;

            return $"{axisName}: {winner}";
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

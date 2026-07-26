using System;
using System.Globalization;

namespace Pinder.LlmAdapters
{
    internal static class EmotionalDirectionLeakGuard
    {
        internal const string Reason = "private_direction_leak";

        private const string Header = "DATEE EMOTIONAL PERFORMANCE DIRECTION";

        private static readonly string[] FieldPrefixes =
        {
            "Primary emotion:",
            "Intensity:",
            "Underlying feeling:",
            "Interpretation:",
            "Impulse:",
            "Restraint:",
            "Response posture:",
        };

        private static readonly string[] FieldPlaceholders =
        {
            "primary_emotion",
            "intensity",
            "underlying_feeling",
            "interpretation",
            "impulse",
            "restraint",
            "response_posture",
        };

        internal static void ThrowIfDetected(string responseText, int turnId)
        {
            if (!ContainsPrivateMarker(responseText))
                return;

            throw new LlmContractException(
                phase: "datee_response",
                reason: Reason,
                message: "LLM datee_response exposed private emotional direction metadata.",
                provider: null,
                model: null,
                parserName: nameof(EmotionalDirectionLeakGuard),
                expectedOptionCount: null,
                parsedOptionCount: null,
                optionCount: null,
                signalCount: null,
                sessionId: null,
                turnId: turnId);
        }

        internal static void ValidatePerformanceTemplate(string template, string key)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (key == null) throw new ArgumentNullException(nameof(key));

            string[] lines = SplitLines(template);
            RequireStructuralLine(lines, Header, key);
            for (int index = 0; index < FieldPrefixes.Length; index++)
            {
                RequireStructuralLine(
                    lines,
                    FieldPrefixes[index] + " {" + FieldPlaceholders[index] + "}",
                    key);
            }
        }

        private static bool ContainsPrivateMarker(string responseText)
        {
            string[] lines = SplitLines(responseText);
            foreach (string line in lines)
            {
                string candidate = NormalizeLeadingDecoration(line);
                if (StartsWithHeader(candidate))
                    return true;

                foreach (string prefix in FieldPrefixes)
                {
                    if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static string[] SplitLines(string value)
        {
            return value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static string NormalizeLeadingDecoration(string line)
        {
            int index = 0;
            while (index < line.Length)
            {
                bool advanced = false;
                while (index < line.Length && IsDecorationAt(line, index, out int decorationLength))
                {
                    index += decorationLength;
                    advanced = true;
                }

                int numberedListLength = GetNumberedListLength(line, index);
                if (numberedListLength > 0)
                {
                    index += numberedListLength;
                    continue;
                }

                if (!advanced)
                    break;
            }

            return line.Substring(index);
        }

        private static int GetNumberedListLength(string value, int start)
        {
            int index = start;
            while (index < value.Length && char.IsDigit(value[index]))
                index++;

            if (index == start
                || index + 1 >= value.Length
                || (value[index] != '.' && value[index] != ')')
                || !char.IsWhiteSpace(value[index + 1]))
            {
                return 0;
            }

            return index - start + 1;
        }

        private static bool StartsWithHeader(string candidate)
        {
            if (!candidate.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
                return false;

            return candidate.Length == Header.Length
                || IsDecorationAt(candidate, Header.Length, out _);
        }

        private static bool IsDecorationAt(string value, int index, out int length)
        {
            length = char.IsSurrogatePair(value, index) ? 2 : 1;
            if (char.IsWhiteSpace(value, index))
                return true;

            switch (CharUnicodeInfo.GetUnicodeCategory(value, index))
            {
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.MathSymbol:
                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.OtherSymbol:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
                default:
                    return false;
            }
        }

        private static void RequireStructuralLine(string[] lines, string expected, string key)
        {
            foreach (string line in lines)
            {
                if (string.Equals(line.Trim(), expected, StringComparison.Ordinal))
                    return;
            }

            throw new InvalidOperationException(
                $"prompt-catalog: key '{key}' system_prompt must include protected structural line '{expected}'.");
        }
    }
}

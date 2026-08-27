using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;

namespace Pinder.LlmAdapters
{
    internal static class DateeVisibleMessageDuplicateGuard
    {
        private static readonly Regex WhitespaceRegex =
            new Regex(@"\s+", RegexOptions.Compiled);

        public static bool IsDuplicateAcceptedVisibleMessage(
            string visibleMessage,
            IReadOnlyList<ConversationMessage>? semanticHistory)
        {
            string normalizedVisible = NormalizeVisibleMessage(visibleMessage);
            if (normalizedVisible.Length == 0 || semanticHistory == null)
                return false;

            foreach (ConversationMessage entry in semanticHistory)
            {
                if (entry == null || entry.Role != ConversationMessage.AssistantRole)
                    continue;

                if (string.Equals(
                    normalizedVisible,
                    NormalizeVisibleMessage(entry.Content),
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static string NormalizeVisibleMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            string normalized = message!.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
            normalized = WhitespaceRegex.Replace(normalized, " ");
            normalized = TrimSurroundingPunctuationSymbolOrWhitespace(normalized);
            return normalized;
        }

        private static string TrimSurroundingPunctuationSymbolOrWhitespace(string value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end
                && IsTrimmedBoundaryCodePoint(value, start, out int charsConsumed))
            {
                start += charsConsumed;
            }
            while (end > start)
            {
                int boundaryIndex = end - 1;
                if (boundaryIndex > start
                    && char.IsLowSurrogate(value[boundaryIndex])
                    && char.IsHighSurrogate(value[boundaryIndex - 1]))
                {
                    boundaryIndex--;
                }

                if (!IsTrimmedBoundaryCodePoint(value, boundaryIndex, out int charsConsumed)
                    || boundaryIndex + charsConsumed != end)
                {
                    break;
                }
                end = boundaryIndex;
            }
            return start == end ? string.Empty : value.Substring(start, end - start);
        }

        private static bool IsTrimmedBoundaryCodePoint(
            string value,
            int index,
            out int charsConsumed)
        {
            charsConsumed = index + 1 < value.Length
                && char.IsSurrogatePair(value[index], value[index + 1])
                ? 2
                : 1;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (category == UnicodeCategory.SpaceSeparator
                || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }

            return category == UnicodeCategory.ConnectorPunctuation
                || category == UnicodeCategory.DashPunctuation
                || category == UnicodeCategory.OpenPunctuation
                || category == UnicodeCategory.ClosePunctuation
                || category == UnicodeCategory.InitialQuotePunctuation
                || category == UnicodeCategory.FinalQuotePunctuation
                || category == UnicodeCategory.OtherPunctuation
                || category == UnicodeCategory.MathSymbol
                || category == UnicodeCategory.CurrencySymbol
                || category == UnicodeCategory.ModifierSymbol
                || category == UnicodeCategory.OtherSymbol;
        }
    }
}

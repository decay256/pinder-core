using System;
using System.Collections.Generic;

namespace Pinder.Core.Text
{
    /// <summary>
    /// Issue #988: Centralized sanitizer that coordinates post-process passes
    /// like callback stripping and meta-prefix stripping.
    /// </summary>
    public static class TextSanitizer
    {
        /// <summary>
        /// Sanitizes raw text using the specified pass layer (e.g. MetaPrefixStripper or CallbackStripper).
        /// If a change is made, computes a word-level diff and adds it to the provided list.
        /// </summary>
        public static string Sanitize(string rawText, string layerName, List<TextDiff> diffList)
        {
            if (rawText == null) return string.Empty;
            if (diffList == null) throw new ArgumentNullException(nameof(diffList));

            string sanitizedText;

            if (layerName == MetaPrefixStripper.LayerName)
            {
                sanitizedText = MetaPrefixStripper.Strip(rawText);
            }
            else if (layerName == CallbackStripper.LayerName)
            {
                sanitizedText = CallbackStripper.Strip(rawText);
            }
            else
            {
                throw new ArgumentException($"Unknown sanitization layer: '{layerName}'", nameof(layerName));
            }

            if (sanitizedText != rawText)
            {
                var stripSpans = WordDiff.Compute(rawText, sanitizedText);
                diffList.Add(new TextDiff(layerName, stripSpans, rawText, sanitizedText));
            }

            return sanitizedText;
        }
    }
}

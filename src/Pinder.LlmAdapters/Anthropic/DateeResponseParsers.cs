using Newtonsoft.Json.Linq;
using System;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Contains logic for parsing Anthropic LLM responses into DateeResponse DTOs,
    /// including the main message and embedded signals like Tell and WeaknessWindow,
    /// from both raw text and structured tool_use JSON.
    /// </summary>
    internal static class DateeResponseParsers
    {
        // #1124: the [SIGNALS] block (TELL/WEAKNESS) is parsed via the single
        // canonical GmOutputContract — the ONE output-format contract shared by
        // both GM sessions — rather than a duplicate regex here. The datee path
        // keeps its own message-cleaning (eval headers, quotes, persona tags).

        /// <summary>
        /// Defensively removes the persona "self-tag" tics ("/end", "/rant") that the
        /// model is instructed to use as a terminal suffix but frequently misplaces
        /// mid-message (e.g. inside a parenthetical: "...leave that one open /end)\n\n...").
        /// These tags are meta markers, not chat content, so they must never be persisted
        /// to the saved chat history. Only the exact literal tokens are removed; legitimate
        /// prose is left untouched.
        /// </summary>
        internal static string StripPersonaSelfTags(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            var result = text!;

            // Mid-text tag immediately before a closing paren: "...open /end)" -> "...open)".
            // Handle the spaced form first so we don't leave a stray space before ')'.
            result = result.Replace(" /end)", ")").Replace(" /rant)", ")");
            result = result.Replace("/end)", ")").Replace("/rant)", ")");

            // Mid-text tag surrounded by spaces: "blah /end blah" -> "blah blah".
            result = result.Replace(" /end ", " ").Replace(" /rant ", " ");

            // Trailing tag suffix: "...anyway. /end" -> "...anyway.".
            result = StripTrailingTag(result, " /end");
            result = StripTrailingTag(result, " /rant");

            // The ordered replacements above never introduce double spaces, so a final
            // Trim() is sufficient; we deliberately do NOT collapse runs of spaces here
            // to avoid mangling the legitimate "double space after periods" persona tic.
            return result.Trim();
        }

        private static string StripTrailingTag(string text, string tag)
        {
            var trimmedEnd = text.TrimEnd();
            if (trimmedEnd.EndsWith(tag, StringComparison.Ordinal))
            {
                return trimmedEnd.Substring(0, trimmedEnd.Length - tag.Length);
            }
            return text;
        }

        /// <summary>
        /// Parses structured LLM text output with optional [SIGNALS] blocks.
        /// Never throws — returns DateeResponse with null signals on parse failure.
        /// <para>
        /// NOTE: This lenient parser is best-effort and diagnostics-only.
        /// Gameplay production uses strict validation logic.
        /// </para>
        /// </summary>
        public static DateeResponse ParseDateeResponseText(string? llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return new DateeResponse("", null, null);
            }

            var response = llmResponse!;
            string messageText;
            Tell? tell = null;
            WeaknessWindow? weakness = null;

            try
            {
                // Extract message text (everything before [SIGNALS])
                var signalIdx = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                messageText = signalIdx >= 0
                    ? response.Substring(0, signalIdx).Trim()
                    : response.Trim();

                // Strip [RESPONSE] tag if the LLM still generates it
                var responseTagIdx = messageText.IndexOf("[RESPONSE]", StringComparison.OrdinalIgnoreCase);
                if (responseTagIdx >= 0)
                {
                    messageText = messageText.Substring(responseTagIdx + "[RESPONSE]".Length).Trim();
                }

                // Strip improvement-loop evaluation headers that leaked into the response.
                var evalEndMarkers = new[] {
                    "The content works as written.",
                    "content works as written",
                    "4. AUDIENCE:",
                    "4. Audience:"
                };
                foreach (var marker in evalEndMarkers)
                {
                    var markerIdx = messageText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (markerIdx >= 0)
                    {
                        var afterMarker = messageText.Substring(markerIdx + marker.Length).Trim();
                        if (!string.IsNullOrWhiteSpace(afterMarker))
                            messageText = afterMarker;
                        break;
                    }
                }

                // Strip surrounding quotes if present
                if (messageText.Length >= 2 && messageText[0] == '"' && messageText[messageText.Length - 1] == '"')
                {
                    messageText = messageText.Substring(1, messageText.Length - 2).Trim();
                }

                // Strip persona self-tag tics ("/end", "/rant") that must never persist to chat history.
                messageText = StripPersonaSelfTags(messageText);

                // #1124: parse the optional [SIGNALS] block via the single
                // canonical contract shared by both GM sessions.
                var signals = GmOutputContract.Parse(response);
                tell = signals.Tell;
                weakness = signals.Weakness;
            }
            catch
            {
                // Any unexpected error — return empty response
                return new DateeResponse(response.Trim(), null, null);
            }

            return new DateeResponse(messageText, tell, weakness);
        }

        /// <summary>
        /// Parses datee response from a tool_use JSON input.
        /// Returns null if the input is malformed (caller should fall back to text parsing).
        /// <para>
        /// NOTE: This lenient parser is best-effort and diagnostics-only.
        /// Gameplay production uses strict validation logic.
        /// </para>
        /// </summary>
        public static DateeResponse? ParseDateeResponseTool(JObject toolInput)
        {
            try
            {
                var message = toolInput.Value<string>("message");
                if (string.IsNullOrWhiteSpace(message))
                    return null;

                // Strip persona self-tag tics ("/end", "/rant") that must never persist to chat history.
                message = StripPersonaSelfTags(message);

                Tell? tell = null;
                var tellObj = toolInput["tell"] as JObject;
                if (tellObj != null)
                {
                    var statStr = StatNameNormalizer.NormalizeStatName(tellObj.Value<string>("stat") ?? "");
                    try
                    {
                        var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        var desc = tellObj.Value<string>("description") ?? "";
                        tell = new Tell(stat, desc);
                    }
                    catch (ArgumentException)
                    {
                        // Invalid stat — tell stays null
                    }
                }

                WeaknessWindow? weakness = null;
                var weakObj = toolInput["weakness"] as JObject;
                if (weakObj != null)
                {
                    var statStr = StatNameNormalizer.NormalizeStatName(weakObj.Value<string>("defending_stat") ?? "");
                    try
                    {
                        var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        var reduction = weakObj.Value<int?>("dc_reduction") ?? 0;
                        if (reduction > 0)
                        {
                            weakness = new WeaknessWindow(stat, reduction);
                        }
                    }
                    catch (Exception)
                    {
                        // Invalid stat or reduction — weakness stays null
                    }
                }

                return new DateeResponse(message, tell, weakness);
            }
            catch
            {
                return null; // Malformed — fall back to text parsing
            }
        }
    }
}

using Newtonsoft.Json.Linq;
using System;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Contains logic for parsing Anthropic LLM responses into OpponentResponse DTOs,
    /// including the main message and embedded signals like Tell and WeaknessWindow,
    /// from both raw text and structured tool_use JSON.
    /// </summary>
    internal static class OpponentResponseParsers
    {
        private static readonly Regex TellSignalRegex = new Regex(
            @"TELL:\s*(\w+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WeaknessSignalRegex = new Regex(
            @"WEAKNESS:\s*(\w+)\s*-(\d+)\s*\(([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parses structured LLM text output with optional [SIGNALS] blocks.
        /// Never throws — returns OpponentResponse with null signals on parse failure.
        /// </summary>
        public static OpponentResponse ParseOpponentResponseText(string? llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return new OpponentResponse("", null, null);
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

                // Parse optional [SIGNALS] block
                var signalsIndex = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                if (signalsIndex >= 0)
                {
                    var signalsBlock = response.Substring(signalsIndex);

                    var tellMatch = TellSignalRegex.Match(signalsBlock);
                    if (tellMatch.Success)
                    {
                        var statStr = StatNameNormalizer.NormalizeStatName(tellMatch.Groups[1].Value.Trim());
                        try
                        {
                            var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                            var description = tellMatch.Groups[2].Value.Trim();
                            tell = new Tell(stat, description);
                        }
                        catch (ArgumentException)
                        {
                            // Invalid stat — tell stays null
                        }
                    }

                    var weaknessMatch = WeaknessSignalRegex.Match(signalsBlock);
                    if (weaknessMatch.Success)
                    {
                        var statStr = StatNameNormalizer.NormalizeStatName(weaknessMatch.Groups[1].Value.Trim());
                        try
                        {
                            var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                            var reduction = int.Parse(weaknessMatch.Groups[2].Value.Trim());
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
                }
            }
            catch
            {
                // Any unexpected error — return empty response
                return new OpponentResponse(response.Trim(), null, null);
            }

            return new OpponentResponse(messageText, tell, weakness);
        }

        /// <summary>
        /// Parses opponent response from a tool_use JSON input.
        /// Returns null if the input is malformed (caller should fall back to text parsing).
        /// </summary>
        public static OpponentResponse? ParseOpponentResponseTool(JObject toolInput)
        {
            try
            {
                var message = toolInput.Value<string>("message");
                if (string.IsNullOrWhiteSpace(message))
                    return null;

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

                return new OpponentResponse(message, tell, weakness);
            }
            catch
            {
                return null; // Malformed — fall back to text parsing
            }
        }
    }
}

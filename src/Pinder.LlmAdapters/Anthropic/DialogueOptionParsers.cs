using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Contains logic for parsing Anthropic LLM responses into DialogueOption DTOs,
    /// supporting both raw text and structured tool_use JSON formats.
    /// Includes functionality for ensuring a consistent number of dialogue options.
    /// </summary>
    internal static class DialogueOptionParsers
    {
        // Regex patterns for parsing LLM responses
        private static readonly Regex OptionHeaderRegex = new Regex(
            @"OPTION_\d+",
            RegexOptions.Compiled);

        private static readonly Regex StatRegex = new Regex(
            @"\[STAT:\s*(\w+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CallbackRegex = new Regex(
            @"\[CALLBACK:\s*([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ComboRegex = new Regex(
            @"\[COMBO:\s*([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TellBonusRegex = new Regex(
            @"\[TELL_BONUS:\s*(\w+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex QuotedTextRegex = new Regex(
            @"""([^""]+)""",
            RegexOptions.Compiled);

        // Default padding stats for ParseDialogueOptions fallback
        private static readonly StatType[] DefaultPaddingStats = new[]
        {
            StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos
        };

        /// <summary>
        /// Parses structured LLM text output into DialogueOption array.
        /// Never throws — returns 3 options, padding with defaults if needed.
        /// </summary>
        public static DialogueOption[] ParseDialogueOptionsText(string? llmResponse)
        {
            var parsed = new List<DialogueOption>();

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                try
                {
                    // Split by OPTION_N headers
                    var sections = OptionHeaderRegex.Split(llmResponse);

                    foreach (var section in sections)
                    {
                        if (string.IsNullOrWhiteSpace(section)) continue;
                        if (parsed.Count >= 3) break;

                        var statMatch = StatRegex.Match(section);
                        if (!statMatch.Success) continue;

                        var statStr = StatNameNormalizer.NormalizeStatName(statMatch.Groups[1].Value.Trim());
                        StatType stat;
                        try
                        {
                            stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        }
                        catch (ArgumentException)
                        {
                            continue; // Invalid stat — skip this option
                        }

                        var textMatch = QuotedTextRegex.Match(section);
                        if (!textMatch.Success) continue; // No text = invalid option

                        var text = textMatch.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(text)) continue;

                        // Parse optional metadata
                        int? callbackTurn = null;
                        var callbackMatch = CallbackRegex.Match(section);
                        if (callbackMatch.Success)
                        {
                            var cbVal = callbackMatch.Groups[1].Value.Trim();
                            if (!string.Equals(cbVal, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(cbVal, out int turnNum))
                                {
                                    callbackTurn = turnNum;
                                }
                                else if (cbVal.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                                         int.TryParse(cbVal.Substring(5), out int turnNum2))
                                {
                                    callbackTurn = turnNum2;
                                }
                            }
                        }

                        string? comboName = null;
                        var comboMatch = ComboRegex.Match(section);
                        if (comboMatch.Success)
                        {
                            var comboVal = comboMatch.Groups[1].Value.Trim();
                            if (!string.Equals(comboVal, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                comboName = comboVal;
                            }
                        }

                        bool hasTellBonus = false;
                        var tellMatch = TellBonusRegex.Match(section);
                        if (tellMatch.Success)
                        {
                            hasTellBonus = string.Equals(
                                tellMatch.Groups[1].Value.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
                        }

                        parsed.Add(new DialogueOption(
                            stat, text, callbackTurn, comboName, hasTellBonus, hasWeaknessWindow: false));
                    }
                }
                catch
                {
                    // Swallow any unexpected parse error — we'll pad with defaults below
                }
            }

            return PadDialogueOptionsToThree(parsed);
        }

        /// <summary>
        /// Parses dialogue options from a tool_use JSON input.
        /// Returns null if the input is malformed (caller should fall back to text parsing).
        /// </summary>
        public static DialogueOption[]? ParseDialogueOptionsTool(JObject toolInput)
        {
            try
            {
                var optionsArray = toolInput["options"] as JArray;
                if (optionsArray == null || optionsArray.Count == 0)
                    return null;

                var parsed = new List<DialogueOption>();
                foreach (var item in optionsArray)
                {
                    if (parsed.Count >= 3) break;

                    var statStr = StatNameNormalizer.NormalizeStatName(item.Value<string>("stat") ?? "");
                    StatType stat;
                    try
                    {
                        stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                    }
                    catch (ArgumentException)
                    {
                        continue; // Invalid stat — skip
                    }

                    var text = item.Value<string>("text");
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // Parse callback
                    int? callbackTurn = null;
                    var callbackVal = item.Value<string>("callback");
                    if (!string.IsNullOrEmpty(callbackVal) &&
                        !string.Equals(callbackVal, "none", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(callbackVal, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(callbackVal, out int turnNum))
                        {
                            callbackTurn = turnNum;
                        }
                        else if (callbackVal.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                                 int.TryParse(callbackVal.Substring(5), out int turnNum2))
                        {
                            callbackTurn = turnNum2;
                        }
                    }

                    // Parse combo
                    string? comboName = null;
                    var comboVal = item.Value<string>("combo");
                    if (!string.IsNullOrEmpty(comboVal) &&
                        !string.Equals(comboVal, "none", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(comboVal, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        comboName = comboVal;
                    }

                    var tellBonus = item.Value<bool?>("tell_bonus") ?? false;
                    var weaknessWindow = item.Value<bool?>("weakness_window") ?? false;

                    parsed.Add(new DialogueOption(
                        stat, text, callbackTurn, comboName, tellBonus, weaknessWindow));
                }

                if (parsed.Count == 0) return null;
                return PadDialogueOptionsToThree(parsed);
            }
            catch
            {
                return null; // Malformed — fall back to text parsing
            }
        }

        /// <summary>Pads parsed options to exactly 3 using default stats not already present.</summary>
        public static DialogueOption[] PadDialogueOptionsToThree(List<DialogueOption> parsed)
        {
            if (parsed.Count >= 3)
            {
                return parsed.GetRange(0, 3).ToArray();
            }

            var usedStats = new HashSet<StatType>();
            foreach (var opt in parsed)
            {
                usedStats.Add(opt.Stat);
            }

            var result = new List<DialogueOption>(parsed);
            foreach (var defaultStat in DefaultPaddingStats)
            {
                if (result.Count >= 3) break;
                if (usedStats.Contains(defaultStat)) continue;
                result.Add(new DialogueOption(defaultStat, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            // If we still need more (e.g., all 3 default stats were used), just pad with Charm
            while (result.Count < 3)
            {
                result.Add(new DialogueOption(StatType.Charm, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            return result.ToArray();
        }
    }
}

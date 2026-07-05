using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Text;

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
            @"OPTION[_\s]\d+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        // Captures the FULL intended text of an option (issue #1117).
        // The previous pattern @"""([^""]+)""" stopped at the first inner
        // double-quote, so an option that quotes the datee back
        // (e.g. the model writes:  said "it's a lot")  got truncated to a
        // leading fragment. Because each per-option section (split out by the
        // OPTION_N header) only ever contains this single quoted block followed
        // by bracketed [CALLBACK]/[COMBO]/[TELL_BONUS] metadata tags — none of
        // which contain double-quotes — a greedy capture to the LAST quote on
        // the line reconstructs the complete text, inner quotes included.
        private static readonly Regex QuotedTextRegex = new Regex(
            @"""(.+)""",
            RegexOptions.Compiled);

        // Minimum length (after meta-prefix stripping/trim) for a parsed option
        // to be treated as a playable dialogue line. Degenerate stubs such as
        // "the" or "..." that slip through are dropped here so they are never
        // surfaced; PadDialogueOptionsToFour then backfills a proper placeholder.
        private const int MinPlayableOptionLength = 4;



        // Default padding stats for ParseDialogueOptions fallback
        private static readonly StatType[] DefaultPaddingStats = new[]
        {
            StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos, StatType.Rizz, StatType.SelfAwareness
        };

        /// <summary>
        /// Parses structured LLM text output into DialogueOption array.
        /// Never throws — returns options padded with defaults if needed.
        /// <para>
        /// NOTE: This lenient parser is best-effort and diagnostics-only.
        /// Gameplay production uses the strict path (ParseDialogueOptionsStrict).
        /// </para>
        /// </summary>
        public static DialogueOption[] ParseDialogueOptionsText(string? llmResponse, StatType[]? availableStats = null)
        {
            var parsed = new List<DialogueOption>();
            int count = availableStats != null ? availableStats.Length : DefaultPaddingStats.Length;

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                try
                {
                    // Split by OPTION_N headers
                    var sections = OptionHeaderRegex.Split(llmResponse);

                    foreach (var section in sections)
                    {
                        if (string.IsNullOrWhiteSpace(section)) continue;
                        if (parsed.Count >= count) break;

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

                        var text = MetaPrefixStripper.Strip(textMatch.Groups[1].Value.Trim());
                        if (string.IsNullOrEmpty(text)) continue;

                        // Issue #1117 sanity guard: reject sub-threshold/degenerate
                        // fragments (e.g. "the", "...") so they are never surfaced as
                        // playable options. PadDialogueOptionsToFour backfills instead.
                        if (text.Length < MinPlayableOptionLength) continue;

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

            return ReconcileAndPadDialogueOptions(parsed, availableStats);
        }

        /// <summary>
        /// Parses dialogue options from a tool_use JSON input.
        /// Returns null if the input is malformed (caller should fall back to text parsing).
        /// <para>
        /// NOTE: This lenient parser is best-effort and diagnostics-only.
        /// Gameplay production uses the strict path (ParseDialogueOptionsStrict).
        /// </para>
        /// </summary>
        public static DialogueOption[]? ParseDialogueOptionsTool(JObject toolInput, StatType[]? availableStats = null)
        {
            try
            {
                var optionsArray = toolInput["options"] as JArray;
                if (optionsArray == null || optionsArray.Count == 0)
                    return null;

                int count = availableStats != null ? availableStats.Length : DefaultPaddingStats.Length;
                var parsed = new List<DialogueOption>();
                foreach (var item in optionsArray)
                {
                    if (parsed.Count >= count) break;

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

                    text = MetaPrefixStripper.Strip(text.Trim());

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
                return ReconcileAndPadDialogueOptions(parsed, availableStats);
            }
            catch
            {
                return null; // Malformed — fall back to text parsing
            }
        }

        /// <summary>
        /// Strictly parses and validates LLM response text for dialogue options.
        /// Throws detailed error codes if output is empty, has an invalid stat, is missing required option counts, or has no valid options.
        /// </summary>
        public static DialogueOption[] ParseDialogueOptionsStrict(
            string? llmResponse,
            StatType[]? availableStats,
            int maxDialogueOptions,
            out string? errorCode,
            out string? errorMessage,
            out int parsedCount,
            out int expectedCount)
        {
            errorCode = null;
            errorMessage = null;
            parsedCount = 0;
            expectedCount = availableStats != null ? Math.Min(availableStats.Length, maxDialogueOptions) : maxDialogueOptions;

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                errorCode = "empty_output";
                errorMessage = "LLM dialogue_options output is empty or whitespace.";
                return Array.Empty<DialogueOption>();
            }

            var parsed = new List<DialogueOption>();
            var sections = OptionHeaderRegex.Split(llmResponse);

            // sections[0] contains anything before OPTION_1 (the preamble / thinking block).
            // sections[1..] contain the actual options.
            for (int i = 1; i < sections.Length; i++)
            {
                var section = sections[i];
                if (string.IsNullOrWhiteSpace(section)) continue;

                // Check for stat tag
                var statMatch = StatRegex.Match(section);
                if (!statMatch.Success)
                {
                    // If we have an OPTION_N block but no STAT tag, it's not a valid complete option
                    continue;
                }

                var statStr = StatNameNormalizer.NormalizeStatName(statMatch.Groups[1].Value.Trim());
                StatType stat;
                try
                {
                    stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                }
                catch (ArgumentException)
                {
                    errorCode = "invalid_stat";
                    errorMessage = "LLM dialogue_options option names an invalid or unknown stat.";
                    return Array.Empty<DialogueOption>();
                }

                // Check for quoted text
                var textMatch = QuotedTextRegex.Match(section);
                if (!textMatch.Success) continue;

                var text = MetaPrefixStripper.Strip(textMatch.Groups[1].Value.Trim());
                if (string.IsNullOrEmpty(text) || text.Length < MinPlayableOptionLength) continue;

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

            var validOptions = new List<DialogueOption>();
            var usedStats = new HashSet<StatType>();
            var allowedStats = availableStats != null ? new HashSet<StatType>(availableStats) : null;

            foreach (var opt in parsed)
            {
                if (allowedStats != null && !allowedStats.Contains(opt.Stat))
                {
                    continue;
                }
                if (usedStats.Contains(opt.Stat))
                {
                    continue;
                }
                usedStats.Add(opt.Stat);
                validOptions.Add(opt);
            }

            parsedCount = validOptions.Count;

            if (validOptions.Count == 0)
            {
                errorCode = "no_valid_options";
                errorMessage = "LLM dialogue_options response contains no valid options (malformed).";
                return Array.Empty<DialogueOption>();
            }

            if (validOptions.Count < expectedCount)
            {
                errorCode = "partial_options";
                errorMessage = $"LLM dialogue_options response has partial options: fewer valid options ({validOptions.Count}) than required ({expectedCount}).";
                return Array.Empty<DialogueOption>();
            }

            if (validOptions.Count > expectedCount)
            {
                return validOptions.GetRange(0, expectedCount).ToArray();
            }
            return validOptions.ToArray();
        }

        public static DialogueOption[] ReconcileAndPadDialogueOptions(List<DialogueOption> parsed, StatType[]? availableStats = null)
        {
            int count = availableStats != null ? availableStats.Length : DefaultPaddingStats.Length;
            var result = new List<DialogueOption>();
            var usedStats = new HashSet<StatType>();
            var remainingAllowed = availableStats != null ? new List<StatType>(availableStats) : new List<StatType>(DefaultPaddingStats);

            var tempOptions = new DialogueOption[parsed.Count];
            for (int i = 0; i < parsed.Count; i++)
            {
                var opt = parsed[i];
                if (remainingAllowed.Contains(opt.Stat))
                {
                    tempOptions[i] = opt;
                    remainingAllowed.Remove(opt.Stat);
                    usedStats.Add(opt.Stat);
                }
            }

            for (int i = 0; i < parsed.Count; i++)
            {
                if (tempOptions[i] == null)
                {
                    var opt = parsed[i];
                    StatType assignedStat;
                    if (remainingAllowed.Count > 0)
                    {
                        assignedStat = remainingAllowed[0];
                        remainingAllowed.RemoveAt(0);
                    }
                    else
                    {
                        assignedStat = StatType.Charm;
                        foreach (StatType s in Enum.GetValues(typeof(StatType)))
                        {
                            if (!usedStats.Contains(s))
                            {
                                assignedStat = s;
                                break;
                            }
                        }
                    }
                    usedStats.Add(assignedStat);
                    
                    tempOptions[i] = new DialogueOption(
                        assignedStat,
                        opt.IntendedText,
                        opt.CallbackTurnNumber,
                        opt.ComboName,
                        opt.HasTellBonus,
                        opt.HasWeaknessWindow
                    );
                }
            }

            result.AddRange(tempOptions);

            while (result.Count < count)
            {
                StatType padStat;
                if (remainingAllowed.Count > 0)
                {
                    padStat = remainingAllowed[0];
                    remainingAllowed.RemoveAt(0);
                }
                else
                {
                    padStat = StatType.Charm;
                    foreach (StatType s in Enum.GetValues(typeof(StatType)))
                    {
                        if (!usedStats.Contains(s))
                        {
                            padStat = s;
                            break;
                        }
                    }
                }
                usedStats.Add(padStat);
                result.Add(new DialogueOption(padStat, "Tell me more about you.",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            if (result.Count > count)
            {
                return result.GetRange(0, count).ToArray();
            }

            return result.ToArray();
        }
    }
}

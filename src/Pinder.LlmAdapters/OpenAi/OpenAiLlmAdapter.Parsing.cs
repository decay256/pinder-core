using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters.OpenAi
{
    public sealed partial class OpenAiLlmAdapter
    {
        // Regex patterns — same as AnthropicLlmAdapter
        private static readonly Regex OptionHeaderRegex = new Regex(
            @"OPTION_\d+", RegexOptions.Compiled);
        private static readonly Regex StatRegex = new Regex(
            @"\[STAT:\s*(\w+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CallbackRegex = new Regex(
            @"\[CALLBACK:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ComboRegex = new Regex(
            @"\[COMBO:\s*([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TellBonusRegex = new Regex(
            @"\[TELL_BONUS:\s*(\w+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex QuotedTextRegex = new Regex(
            @"""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex TellSignalRegex = new Regex(
            @"TELL:\s*(\w+)\s*\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WeaknessSignalRegex = new Regex(
            @"WEAKNESS:\s*(\w+)\s*-(\d+)\s*\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly StatType[] DefaultPaddingStats = new[]
        {
            StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos, StatType.Rizz, StatType.SelfAwareness
        };

        // ── Response parsing (duplicated from AnthropicLlmAdapter) ────────

        /// <summary>
        /// Parses structured LLM output into DialogueOption array.
        /// Never throws — returns 4 options, padding with defaults if needed.
        /// </summary>
        internal static DialogueOption[] ParseDialogueOptions(string? llmResponse)
        {
            var parsed = new List<DialogueOption>();

            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                try
                {
                    var sections = OptionHeaderRegex.Split(llmResponse);

                    foreach (var section in sections)
                    {
                        if (string.IsNullOrWhiteSpace(section)) continue;
                        if (parsed.Count >= 4) break;

                        var statMatch = StatRegex.Match(section);
                        if (!statMatch.Success) continue;

                        var statStr = NormalizeStatName(statMatch.Groups[1].Value.Trim());
                        StatType stat;
                        try
                        {
                            stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                        }
                        catch (ArgumentException)
                        {
                            continue;
                        }

                        var textMatch = QuotedTextRegex.Match(section);
                        if (!textMatch.Success) continue;

                        var text = MetaPrefixStripper.Strip(textMatch.Groups[1].Value.Trim());
                        if (string.IsNullOrEmpty(text)) continue;

                        int? callbackTurn = null;
                        var callbackMatch = CallbackRegex.Match(section);
                        if (callbackMatch.Success)
                        {
                            var cbVal = callbackMatch.Groups[1].Value.Trim();
                            if (!string.Equals(cbVal, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(cbVal, out int turnNum))
                                    callbackTurn = turnNum;
                                else if (cbVal.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) &&
                                         int.TryParse(cbVal.Substring(5), out int turnNum2))
                                    callbackTurn = turnNum2;
                            }
                        }

                        string? comboName = null;
                        var comboMatch = ComboRegex.Match(section);
                        if (comboMatch.Success)
                        {
                            var comboVal = comboMatch.Groups[1].Value.Trim();
                            if (!string.Equals(comboVal, "none", StringComparison.OrdinalIgnoreCase))
                                comboName = comboVal;
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
                    // Swallow — pad below
                }
            }

            return PadToFour(parsed);
        }

        /// <summary>
        /// Parses structured LLM output with optional [SIGNALS] blocks.
        /// Never throws — returns OpponentResponse with null signals on parse failure.
        /// </summary>
        internal static OpponentResponse ParseOpponentResponse(string? llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
                return new OpponentResponse("", null, null);

            var response = llmResponse!;
            string messageText;
            Tell? tell = null;
            WeaknessWindow? weakness = null;

            try
            {
                var signalIdx = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                messageText = signalIdx >= 0
                    ? response.Substring(0, signalIdx).Trim()
                    : response.Trim();

                var responseTagIdx = messageText.IndexOf("[RESPONSE]", StringComparison.OrdinalIgnoreCase);
                if (responseTagIdx >= 0)
                    messageText = messageText.Substring(responseTagIdx + "[RESPONSE]".Length).Trim();

                if (messageText.Length >= 2 && messageText[0] == '"' && messageText[messageText.Length - 1] == '"')
                    messageText = messageText.Substring(1, messageText.Length - 2).Trim();

                var signalsIndex = response.IndexOf("[SIGNALS]", StringComparison.OrdinalIgnoreCase);
                if (signalsIndex >= 0)
                {
                    var signalsBlock = response.Substring(signalsIndex);

                    var tellMatch = TellSignalRegex.Match(signalsBlock);
                    if (tellMatch.Success)
                    {
                        var statStr = NormalizeStatName(tellMatch.Groups[1].Value.Trim());
                        try
                        {
                            var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                            var description = tellMatch.Groups[2].Value.Trim();
                            tell = new Tell(stat, description);
                        }
                        catch (ArgumentException) { }
                    }

                    var weaknessMatch = WeaknessSignalRegex.Match(signalsBlock);
                    if (weaknessMatch.Success)
                    {
                        var statStr = NormalizeStatName(weaknessMatch.Groups[1].Value.Trim());
                        try
                        {
                            var stat = (StatType)Enum.Parse(typeof(StatType), statStr, true);
                            var reduction = int.Parse(weaknessMatch.Groups[2].Value.Trim());
                            if (reduction > 0)
                                weakness = new WeaknessWindow(stat, reduction);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch
            {
                return new OpponentResponse(response.Trim(), null, null);
            }

            return new OpponentResponse(messageText, tell, weakness);
        }

        private static string NormalizeStatName(string raw)
        {
            if (string.Equals(raw, "SELF_AWARENESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "SELFAWARENESS", StringComparison.OrdinalIgnoreCase))
                return "SelfAwareness";
            return raw;
        }

        private static DialogueOption[] PadToFour(List<DialogueOption> parsed)
        {
            if (parsed.Count >= 4)
                return parsed.GetRange(0, 4).ToArray();

            var usedStats = new HashSet<StatType>();
            foreach (var opt in parsed)
                usedStats.Add(opt.Stat);

            var result = new List<DialogueOption>(parsed);
            foreach (var defaultStat in DefaultPaddingStats)
            {
                if (result.Count >= 4) break;
                if (usedStats.Contains(defaultStat)) continue;
                result.Add(new DialogueOption(defaultStat, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            while (result.Count < 4)
            {
                result.Add(new DialogueOption(StatType.Charm, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            return result.ToArray();
        }
    }
}

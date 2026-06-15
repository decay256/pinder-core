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
        internal static DialogueOption[] ParseDialogueOptions(string? llmResponse, StatType[]? availableStats = null)
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

            return PadToFour(parsed, availableStats);
        }

        /// <summary>
        /// Parses structured LLM output with optional [SIGNALS] blocks.
        /// Never throws — returns DateeResponse with null signals on parse failure.
        /// </summary>
        internal static DateeResponse ParseDateeResponse(string? llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
                return new DateeResponse("", null, null);

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
                return new DateeResponse(response.Trim(), null, null);
            }

            return new DateeResponse(messageText, tell, weakness);
        }

        private static string NormalizeStatName(string raw)
        {
            if (string.Equals(raw, "SELF_AWARENESS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "SELFAWARENESS", StringComparison.OrdinalIgnoreCase))
                return "SelfAwareness";
            return raw;
        }

        private static DialogueOption[] PadToFour(List<DialogueOption> parsed, StatType[]? availableStats = null)
        {
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

            var result = new List<DialogueOption>(tempOptions);

            while (result.Count < 4)
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
                result.Add(new DialogueOption(padStat, "...",
                    callbackTurnNumber: null, comboName: null,
                    hasTellBonus: false, hasWeaknessWindow: false));
            }

            if (result.Count > 4)
            {
                return result.GetRange(0, 4).ToArray();
            }

            return result.ToArray();
        }
    }
}

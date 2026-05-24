using System;
using System.Collections.Generic;
using System.Text;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.SessionRunner;

partial class Program
{
    internal static string RiskLabel(int need) =>
        need <= 7  ? "🟢 Safe" :
        need <= 11 ? "🟡 Medium" :
        need <= 15 ? "🟠 Hard" :
        need <= 19 ? "🔴 Bold" : "☠️ Reckless";

    internal static string XpMultiplier(int need) =>
        need <= 5  ? "1x XP" :
        need <= 10 ? "1.5x XP" :
        need <= 15 ? "2x XP" : "3x XP";

    internal static string RewardRange(int need) =>
        need <= 5  ? "+1 to +2" :
        need <= 10 ? "+1 to +2" :
        need <= 15 ? "+2 to +3" : "+3 to +5";

    internal static string InterestBar(int val, int max = 25)
    {
        int filled = (int)Math.Round((double)val / max * 20);
        filled = Math.Max(0, Math.Min(20, filled));
        return new string('█', filled) + new string('░', 20 - filled);
    }

    internal static string StatLabel(StatType s) => s switch {
        StatType.Charm => "CHARM", StatType.Rizz => "RIZZ", StatType.Honesty => "HONESTY",
        StatType.Chaos => "CHAOS", StatType.Wit => "WIT", StatType.SelfAwareness => "SA",
        _ => s.ToString().ToUpperInvariant()
    };

    internal static string FillLine(string content, int width = 58)
    {
        int pad = width - content.Length;
        return "║  " + content + (pad > 0 ? new string(' ', pad) : "") + "║";
    }

    internal static string RenderDiff(TextDiff diff)
    {
        var sb = new StringBuilder();
        foreach (var span in diff.Spans)
        {
            switch (span.Type)
            {
                case DiffSpanType.Keep:   sb.Append(span.Text); break;
                case DiffSpanType.Remove: sb.Append("~~").Append(span.Text.TrimEnd()).Append("~~ "); break;
                case DiffSpanType.Add:    sb.Append("***").Append(span.Text.TrimEnd()).Append("*** "); break;
            }
        }
        return sb.ToString().TrimEnd();
    }

    internal static string FormatDeliveredAdditions(string intended, string delivered, string marker) {
        // Just return the delivered text as-is. The caller already displays
        // intended vs delivered on separate labelled lines, so inline marker
        // highlighting is unnecessary. The previous suffix-only diff was broken
        // for mid-string word substitutions (#705).
        return delivered;
    }

    internal static void PrintQuoted(string? text)
    {
        if (string.IsNullOrEmpty(text)) { Console.WriteLine("> (empty)"); return; }
        // Prefix every line with > so multi-paragraph messages stay in the quote block
        foreach (var line in text.Split('\n'))
        {
            // Blank lines need "> " not just ">" to be a valid blockquote continuation
            Console.WriteLine(string.IsNullOrWhiteSpace(line) ? ">" : $"> {line.TrimEnd()}");
        }
    }

    internal static List<string> WrapText(string text, int maxLen)
    {
        var lines = new List<string>();
        while (text.Length > maxLen) {
            int cut = text.LastIndexOf(' ', maxLen);
            if (cut <= 0) cut = maxLen;
            lines.Add(text.Substring(0, cut));
            text = text.Substring(cut).TrimStart();
        }
        if (text.Length > 0) lines.Add(text);
        return lines.Count > 0 ? lines : new List<string> { "" };
    }

    internal static string GetRollExplanation(RollResult roll)
    {
        if (roll.IsNatOne) return "Nat 1 — Legendary Fail: the die showing 1 overrides all bonuses. Maximum corruption tier.";
        if (roll.IsNatTwenty) return "Nat 20 — Always succeeds regardless of DC. +4 Interest.";
        if (!roll.IsSuccess)
        {
            int miss = roll.DC - roll.FinalTotal;
            if (miss >= 10) return $"Catastrophe (miss by {miss}): −3 Interest + trap activates.";
            if (miss >= 6)  return $"Trope Trap (miss by {miss}): −2 Interest + trap activates.";
            if (miss >= 3)  return $"Misfire (miss by {miss}): −1 Interest.";
            return $"Fumble (miss by {miss}): −1 Interest, slight stumble.";
        }
        else
        {
            int beat = roll.FinalTotal - roll.DC;
            if (beat >= 15) return $"Exceptional (beat by {beat}): best possible delivery. +3 Interest base.";
            if (beat >= 10) return $"Critical success (beat by {beat}): peak delivery. +3 Interest base.";
            if (beat >= 5)  return $"Strong success (beat by {beat}): improved delivery. +2 Interest base.";
            return $"Clean success (beat by {beat}): delivered as intended. +1 Interest base.";
        }
    }

    internal static string GetInterestTierRange(InterestState state) => state switch
    {
        InterestState.Bored => "0-4",
        InterestState.Lukewarm => "5-9",
        InterestState.Interested => "10-15",
        InterestState.VeryIntoIt => "16-20",
        InterestState.AlmostThere => "21-24",
        InterestState.DateSecured => "25",
        InterestState.Unmatched => "≤0",
        _ => "?"
    };

    internal static string GetInterestStateDescription(InterestState state) => state switch
    {
        InterestState.Bored => "Ghost risk: 25% per turn. Opponent may stop responding.",
        InterestState.Lukewarm => "Opponent is present but unconvinced. No ghost risk.",
        InterestState.Interested => "Conversation has traction. Opponent is engaged.",
        InterestState.VeryIntoIt => "Opponent is genuinely interested. +advantage on rolls.",
        InterestState.AlmostThere => "One step from the date. Opponent is deciding.",
        InterestState.DateSecured => "Date secured. Opponent agreed to meet.",
        _ => ""
    };

    internal static string GetPairedStat(ShadowStatType shadow) => shadow switch
    {
        ShadowStatType.Madness => "Charm",
        ShadowStatType.Despair => "Rizz",
        ShadowStatType.Denial => "Honesty",
        ShadowStatType.Fixation => "Chaos",
        ShadowStatType.Dread => "Wit",
        ShadowStatType.Overthinking => "Self-Awareness",
        _ => shadow.ToString()
    };

    internal static string GetProviderBaseUrl(string provider)
    {
        switch (provider.ToLowerInvariant())
        {
            case "groq": return "https://api.groq.com/openai";
            case "together": return "https://api.together.xyz/v1";
            case "openrouter": return "https://openrouter.ai/api";
            case "ollama": return "http://localhost:11434/v1";
            default: return "https://api.openai.com";
        }
    }
}

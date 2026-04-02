// Pinder Session Runner — real GameSession + AnthropicLlmAdapter
// Characters and opponent specified via args or default Sable vs Brick
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters.Anthropic;

class NullTrapRegistry : ITrapRegistry
{
    public TrapDefinition? GetTrap(StatType stat) => null;
    public string? GetLlmInstruction(StatType stat) => null;
}

// Writes to both Console and a StringBuilder simultaneously
class TeeWriter : TextWriter
{
    public readonly TextWriter _console;
    private readonly StringBuilder _buffer;
    public TeeWriter(TextWriter console, StringBuilder buffer) { _console = console; _buffer = buffer; }
    public override Encoding Encoding => _console.Encoding;
    public override void Write(char value) { _console.Write(value); _buffer.Append(value); }
    public override void WriteLine(string? value) { _console.WriteLine(value); _buffer.AppendLine(value); }
    public override void WriteLine() { _console.WriteLine(); _buffer.AppendLine(); }
    protected override void Dispose(bool disposing) { if (disposing) _console.Flush(); base.Dispose(disposing); }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) { Console.Error.WriteLine("ANTHROPIC_API_KEY not set"); return 1; }

        // Redirect stdout to tee — writes to both console and buffer for Obsidian
        var buffer = new StringBuilder();
        var tee = new TeeWriter(Console.Out, buffer);
        Console.SetOut(tee);

        Console.WriteLine("=== PINDER SESSION: Sable vs Brick ===");
        Console.WriteLine("Player: Sable (Level 3) | Opponent: Brick (Level 9)");
        Console.WriteLine($"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm UTC}");
        Console.WriteLine($"Model: claude-sonnet-4-20250514 | Engine: AnthropicLlmAdapter + GameSession");
        Console.WriteLine();

        var sableStats = new StatBlock(
            new Dictionary<StatType, int> {
                { StatType.Charm, 7 }, { StatType.Rizz, 7 }, { StatType.Honesty, 8 },
                { StatType.Chaos, 4 }, { StatType.Wit, 1 }, { StatType.SelfAwareness, 4 }
            },
            new Dictionary<ShadowStatType, int> {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 3 }, { ShadowStatType.Fixation, 2 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
            });

        var brickStats = new StatBlock(
            new Dictionary<StatType, int> {
                { StatType.Charm, 16 }, { StatType.Rizz, 14 }, { StatType.Honesty, 11 },
                { StatType.Chaos, 10 }, { StatType.Wit, 15 }, { StatType.SelfAwareness, 8 }
            },
            new Dictionary<ShadowStatType, int> {
                { ShadowStatType.Madness, 8 }, { ShadowStatType.Horniness, 0 },
                { ShadowStatType.Denial, 4 }, { ShadowStatType.Fixation, 3 },
                { ShadowStatType.Dread, 5 }, { ShadowStatType.Overthinking, 6 }
            });

        string basePath = "/root/.openclaw/agents-extra/pinder/design/examples";
        string sablePrompt = ExtractSystemPrompt(File.ReadAllText($"{basePath}/sable-prompt.md"));
        string brickPrompt = ExtractSystemPrompt(File.ReadAllText($"{basePath}/brick-prompt.md"));

        var timing = new TimingProfile(0, 1.0f, 0.0f, "neutral");
        var sable = new CharacterProfile(sableStats, sablePrompt, "Sable", timing, level: 3);
        var brick  = new CharacterProfile(brickStats, brickPrompt, "Brick", timing, level: 9);

        var llm = new AnthropicLlmAdapter(new AnthropicOptions {
            ApiKey = apiKey, Model = "claude-sonnet-4-20250514", MaxTokens = 1024, Temperature = 0.9
        });

        var session = new GameSession(sable, brick, llm, new SystemRandomDiceRoller(), new NullTrapRegistry());

        int turn = 0;
        GameOutcome? finalOutcome = null;

        while (turn < 15)
        {
            turn++;
            TurnStart turnStart;
            try { turnStart = await session.StartTurnAsync(); }
            catch (GameEndedException ex) { finalOutcome = ex.Outcome; break; }

            var snap = turnStart.State;
            Console.WriteLine($"\n{'─',64}");
            Console.WriteLine($"TURN {turn}  |  Interest: {snap.Interest}/25 ({snap.State})  |  Streak: {snap.MomentumStreak}");
            Console.WriteLine($"{'─',64}");

            if (snap.ActiveTrapNames.Length > 0)
                Console.WriteLine($"⚠️  Active traps: {string.Join(", ", snap.ActiveTrapNames)}");

            // Print options with full text + DC + mechanics
            int interestBefore = snap.Interest;
            Console.WriteLine($"\n┌── DIALOGUE OPTIONS ──────────────────────────────────────┐");
            for (int i = 0; i < turnStart.Options.Length; i++)
            {
                var opt = turnStart.Options[i];
                int mod = sableStats.GetEffective(opt.Stat);
                int dc = brickStats.GetDefenceDC(opt.Stat);
                int need = dc - mod;
                int pct = Math.Max(0, Math.Min(100, (21 - need) * 5));
                string risk = need <= 5 ? "🟢 Safe" : need <= 10 ? "🟡 Medium" : need <= 15 ? "🟠 Hard" : "🔴 Bold";
                string flags = "";
                if (opt.HasTellBonus)           flags += " 📖+2";
                if (opt.ComboName != null)       flags += $" ⭐{opt.ComboName}+1";
                if (opt.CallbackTurnNumber.HasValue) flags += $" 🔗T{opt.CallbackTurnNumber}";
                Console.WriteLine($"│  {i+1}) [{opt.Stat}] mod{mod:+#;-#;+0} vs DC{dc} | Need {need}+ | {pct}% | {risk}{flags}");
                if (!string.IsNullOrEmpty(opt.IntendedText) && opt.IntendedText != "...")
                    Console.WriteLine($"│     \"{opt.IntendedText}\"");
                else
                    Console.WriteLine($"│     [intended text unavailable — see bug #240]");
            }
            Console.WriteLine($"└──────────────────────────────────────────────────────────┘");

            int pick = BestOption(turnStart.Options, sableStats);
            var chosen = turnStart.Options[pick];
            int chosenMod = sableStats.GetEffective(chosen.Stat);
            int chosenDC = brickStats.GetDefenceDC(chosen.Stat);
            Console.WriteLine($"\n► Pick {pick+1}: [{chosen.Stat}] mod{chosenMod:+#;-#;+0} vs DC{chosenDC}");

            TurnResult result;
            try { result = await session.ResolveTurnAsync(pick); }
            catch (GameEndedException ex) { finalOutcome = ex.Outcome; break; }

            var roll = result.Roll;
            string rollLine;
            if (roll.IsNatTwenty)      rollLine = $"NAT 20 ⭐ auto-success";
            else if (roll.IsNatOne)    rollLine = $"NAT 1 💀 auto-fail — Legendary";
            else if (roll.Tier == FailureTier.None) rollLine = $"SUCCESS — beat DC by {chosenDC - roll.FinalTotal * -1}";
            else                       rollLine = $"{roll.Tier} — missed DC by {roll.DC - roll.FinalTotal}";

            Console.WriteLine($"\n🎲 d20({roll.UsedDieRoll}) + mod({roll.StatModifier}) = base {roll.Total}");
            if (roll.ExternalBonus != 0) Console.WriteLine($"   + external bonuses: {roll.ExternalBonus:+#;-#;0} (callback/tell/combo/momentum)");
            Console.WriteLine($"   = FINAL {roll.FinalTotal} vs DC{roll.DC} → {rollLine}");
            Console.WriteLine($"\n   Interest: {interestBefore} → {result.StateAfter.Interest} (Δ{result.InterestDelta:+#;-#;0})");

            // Explain the delta
            var reasons = new System.Collections.Generic.List<string>();
            if (roll.Tier == FailureTier.None) {
                int margin = roll.FinalTotal - roll.DC;
                string baseGain = margin >= 10 ? "+3 (crit)" : margin >= 5 ? "+2 (strong)" : "+1 (clean)";
                reasons.Add(baseGain);
                if (result.RiskTier == RiskTier.Hard) reasons.Add("+1 Hard tier bonus");
                if (result.RiskTier == RiskTier.Bold) reasons.Add("+2 Bold tier bonus");
            } else {
                reasons.Add($"{result.InterestDelta} ({roll.Tier})");
            }
            if (result.ComboTriggered != null) { reasons.Add($"+1 combo ({result.ComboTriggered})"); Console.WriteLine($"   ⭐ COMBO TRIGGERED: {result.ComboTriggered}"); }
            if (result.TellReadBonus > 0)      { reasons.Add($"+{result.TellReadBonus} tell"); Console.WriteLine($"   📖 TELL READ: +{result.TellReadBonus}"); }
            Console.WriteLine($"   Why: {string.Join(", ", reasons)}");

            Console.WriteLine($"\nSABLE sends:");
            Console.WriteLine($"  \"{result.DeliveredMessage}\"");
            Console.WriteLine($"\nBRICK replies:");
            Console.WriteLine($"  \"{result.OpponentMessage}\"");

            if (result.ShadowGrowthEvents?.Count > 0)
            {
                Console.WriteLine($"\n⚠️  SHADOW GROWTH:");
                foreach (var e in result.ShadowGrowthEvents) Console.WriteLine($"   {e}");
            }
            if (result.NarrativeBeat != null) Console.WriteLine($"\n✨ {result.NarrativeBeat}");
            if (result.XpEarned > 0) Console.WriteLine($"   XP +{result.XpEarned} (total: {session.TotalXpEarned})");

            if (result.IsGameOver) { finalOutcome = result.Outcome; break; }
        }

        Console.WriteLine($"\n{'═',64}");
        Console.WriteLine(finalOutcome.HasValue
            ? $"SESSION OVER: {finalOutcome} | Total XP: {session.TotalXpEarned}"
            : $"Session cut at {turn} turns | XP: {session.TotalXpEarned}");
        Console.WriteLine($"{'═',64}");

        llm.Dispose();

        // Restore console and write to Obsidian vault
        Console.SetOut(tee._console);
        WritePlaytestLog(buffer.ToString(), "Sable", "Brick", finalOutcome, session.TotalXpEarned, turn);

        return 0;
    }

    static void WritePlaytestLog(string content, string player, string opponent,
        GameOutcome? outcome, int totalXp, int turns)
    {
        string dir = "/root/.openclaw/agents-extra/pinder/design/playtests";
        if (!Directory.Exists(dir)) { Console.Error.WriteLine("Playtest dir not found"); return; }

        int nextNum = 1;
        foreach (var f in Directory.GetFiles(dir, "session-???.md"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.Length >= 11 && int.TryParse(name.Substring(8, 3), out int n))
                nextNum = Math.Max(nextNum, n + 1);
        }

        string slug = $"session-{nextNum:D3}-{player.ToLower()}-vs-{opponent.ToLower()}.md";
        string path = Path.Combine(dir, slug);

        string outcomeStr = outcome.HasValue ? outcome.Value.ToString() : "Incomplete";
        string header = $"# Playtest Session {nextNum:D3} — {player} × {opponent}\n"
            + $"**Date:** {DateTime.UtcNow:yyyy-MM-dd}\n"
            + $"**Engine:** pinder-core `GameSession` + `AnthropicLlmAdapter`\n"
            + $"**Model:** claude-sonnet-4-20250514\n"
            + $"**Player:** {player} (Level 3) | **Opponent:** {opponent} (Level 9, LLM puppet)\n"
            + $"**Outcome:** ✅ {outcomeStr} | **Turns:** {turns} | **XP:** {totalXp}\n\n"
            + "---\n\n"
            + "```\n" + content + "\n```\n";

        File.WriteAllText(path, header);
        Console.WriteLine($"📝 Playtest written → design/playtests/{slug}");
    }

    static string ExtractSystemPrompt(string md)
    {
        int start = md.IndexOf("```\n", StringComparison.Ordinal) + 4;
        int end   = md.LastIndexOf("\n```", StringComparison.Ordinal);
        if (start < 4 || end < 0) return md;
        return md.Substring(start, end - start).Trim();
    }

    static int BestOption(DialogueOption[] options, StatBlock stats)
    {
        int best = 0, bestMod = int.MinValue;
        for (int i = 0; i < options.Length; i++)
        {
            int mod = stats.GetEffective(options[i].Stat);
            if (mod > bestMod) { bestMod = mod; best = i; }
        }
        return best;
    }
}

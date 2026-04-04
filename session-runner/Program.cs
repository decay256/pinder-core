// Pinder Session Runner — real GameSession + AnthropicLlmAdapter
// Outputs markdown matching the session-001 playtest format
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Data;
using Pinder.LlmAdapters.Anthropic;
using Pinder.SessionRunner;

class NullTrapRegistry : ITrapRegistry
{
    public TrapDefinition? GetTrap(StatType stat) => null;
    public string? GetLlmInstruction(StatType stat) => null;
}

class TeeWriter : TextWriter
{
    public readonly TextWriter _console;
    private readonly StringBuilder _buffer;
    public TeeWriter(TextWriter console, StringBuilder buffer) { _console = console; _buffer = buffer; }
    public override Encoding Encoding => _console.Encoding;
    public override void Write(char value) { _console.Write(value); _buffer.Append(value); }
    public override void WriteLine(string? value) { _console.WriteLine(value); _buffer.AppendLine(value); }
    public override void WriteLine() { _console.WriteLine(); _buffer.AppendLine(); }
    protected override void Dispose(bool d) { if (d) _console.Flush(); base.Dispose(d); }
}

class Program
{
    // ── helpers ─────────────────────────────────────────────────────────────

    static string RiskLabel(int need) =>
        need <= 5  ? "🟢 Safe" :
        need <= 10 ? "🟡 Medium" :
        need <= 15 ? "🟠 Hard" : "🔴 Bold";

    static string XpMultiplier(int need) =>
        need <= 5  ? "1x XP" :
        need <= 10 ? "1.5x XP" :
        need <= 15 ? "2x XP" : "3x XP";

    static string RewardRange(int need) =>
        need <= 5  ? "+1 to +2" :
        need <= 10 ? "+1 to +2" :
        need <= 15 ? "+2 to +3" : "+3 to +5";

    static string InterestBar(int val, int max = 25)
    {
        int filled = (int)Math.Round((double)val / max * 20);
        filled = Math.Max(0, Math.Min(20, filled));
        return new string('█', filled) + new string('░', 20 - filled);
    }

    static string StatLabel(StatType s) => s switch {
        StatType.Charm => "CHARM", StatType.Rizz => "RIZZ", StatType.Honesty => "HONESTY",
        StatType.Chaos => "CHAOS", StatType.Wit => "WIT", StatType.SelfAwareness => "SA",
        _ => s.ToString().ToUpperInvariant()
    };

    static string FillLine(string content, int width = 58)
    {
        int pad = width - content.Length;
        return "║  " + content + (pad > 0 ? new string(' ', pad) : "") + "║";
    }

    static string ExtractSystemPrompt(string md)
    {
        int start = md.IndexOf("```\n", StringComparison.Ordinal) + 4;
        int end   = md.LastIndexOf("\n```", StringComparison.Ordinal);
        if (start < 4 || end < 0) return md;
        return md.Substring(start, end - start).Trim();
    }

    // BestOption removed — replaced by IPlayerAgent (see HighestModAgent)

    // ── main ─────────────────────────────────────────────────────────────────

    static async Task<int> Main(string[] args)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) { Console.Error.WriteLine("ANTHROPIC_API_KEY not set"); return 1; }

        var buffer = new StringBuilder();
        var tee = new TeeWriter(Console.Out, buffer);
        Console.SetOut(tee);

        // ── character definitions ──────────────────────────────────────────
        string player1 = "Sable", player2 = "Brick";
        int p1Level = 3, p2Level = 9;
        int p1LevelBonus = 1, p2LevelBonus = 4;

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
        var sable = new CharacterProfile(sableStats, sablePrompt, player1, timing, level: p1Level);
        var brick  = new CharacterProfile(brickStats, brickPrompt, player2, timing, level: p2Level);

        // ── header ────────────────────────────────────────────────────────
        Console.WriteLine($"# Playtest Session 006 — {player1} × {player2}");
        Console.WriteLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd}");
        Console.WriteLine($"**Engine:** `pinder-core GameSession` + `AnthropicLlmAdapter` → claude-sonnet-4-20250514");
        Console.WriteLine($"**Player:** {player1} (Level {p1Level}, +{p1LevelBonus} level bonus) | **Opponent:** {player2} (Level {p2Level}, +{p2LevelBonus} level bonus, LLM puppet)");
        Console.WriteLine();

        // ── character table ───────────────────────────────────────────────
        Console.WriteLine("## Characters");
        Console.WriteLine();
        Console.WriteLine($"| | **{player1}** | **{player2}** |");
        Console.WriteLine("|---|---|---|");
        Console.WriteLine($"| Level | {p1Level} | {p2Level} |");
        foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness }) {
            int p1 = sableStats.GetEffective(stat), p2 = brickStats.GetEffective(stat);
            Console.WriteLine($"| {StatLabel(stat)} | {p1:+#;-#;0} | {p2:+#;-#;0} |");
        }
        Console.WriteLine();

        // ── DC table ──────────────────────────────────────────────────────
        Console.WriteLine("## DC Reference (Sable attacking, Brick defending)");
        Console.WriteLine();
        Console.WriteLine("| Stat | Sable mod | Brick defends | DC | Need | % | Risk |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness }) {
            int atkMod = sableStats.GetEffective(stat);
            int dc = brickStats.GetDefenceDC(stat);
            int need = dc - atkMod;
            int pct = Math.Max(0, Math.Min(100, (21 - need) * 5));
            Console.WriteLine($"| {StatLabel(stat)} | {atkMod:+#;-#;0} | — | {dc} | {need}+ | {pct}% | {RiskLabel(need)} |");
        }
        Console.WriteLine();
        Console.WriteLine("> DC = 13 + opponent defending stat modifier. Miss by 1–2 = Fumble | 3–5 = Misfire | 6–9 = Trope Trap | 10+ = Catastrophe | Nat 1 = Legendary.");
        Console.WriteLine();

        // ── LLM + session setup ───────────────────────────────────────────
        var llm = new AnthropicLlmAdapter(new AnthropicOptions {
            ApiKey = apiKey, Model = "claude-sonnet-4-20250514", MaxTokens = 1024, Temperature = 0.9
        });

        // Load real trap definitions — fallback to NullTrapRegistry if file missing/corrupt
        ITrapRegistry trapRegistry = TrapRegistryLoader.Load(AppContext.BaseDirectory, Console.Error);

        // Shadow tracking — wrap player's StatBlock so GameSession can track shadow growth
        var sableShadows = new SessionShadowTracker(sableStats);
        var config = new GameSessionConfig(playerShadows: sableShadows);
        var session = new GameSession(sable, brick, llm, new SystemRandomDiceRoller(), trapRegistry, config);

        // Player agent for decision-making — configurable via PLAYER_AGENT env var
        IPlayerAgent agent;
        string agentType = Environment.GetEnvironmentVariable("PLAYER_AGENT") ?? "scoring";
        if (agentType.Equals("llm", StringComparison.OrdinalIgnoreCase))
        {
            var agentOptions = new AnthropicOptions
            {
                ApiKey = apiKey,
                Model = Environment.GetEnvironmentVariable("PLAYER_AGENT_MODEL") ?? "claude-sonnet-4-20250514"
            };
            agent = new LlmPlayerAgent(agentOptions, new ScoringPlayerAgent(),
                playerName: sable.DisplayName, opponentName: brick.DisplayName);
        }
        else
        {
            agent = new ScoringPlayerAgent();
        }

        int interest = 10;
        int momentum = 0;
        Console.WriteLine("## Session State");
        Console.WriteLine();
        Console.WriteLine($"```");
        Console.WriteLine($"Interest: {InterestBar(interest)}  {interest}/25");
        Console.WriteLine($"Active Traps: none");
        Console.WriteLine($"Momentum: —");
        Console.WriteLine($"```");
        Console.WriteLine();
        Console.WriteLine("---");

        int turn = 0;
        GameOutcome? finalOutcome = null;
        string lastOpponentMsg = "";

        while (turn < 15)
        {
            turn++;
            TurnStart turnStart;
            try { turnStart = await session.StartTurnAsync(); }
            catch (GameEndedException ex) { finalOutcome = ex.Outcome; break; }

            var snap = turnStart.State;
            interest = snap.Interest;
            momentum = snap.MomentumStreak;

            Console.WriteLine();
            Console.WriteLine($"---");
            Console.WriteLine();
            Console.WriteLine($"## ═══ TURN {turn} ═══");
            if (!string.IsNullOrEmpty(lastOpponentMsg))
                Console.WriteLine($"**Responding to {player2}'s T{turn-1} reply**");
            else
                Console.WriteLine($"**{player1}'s opener | {player2}'s bio**");
            Console.WriteLine();

            // ── UI panel ──────────────────────────────────────────────────
            Console.WriteLine("```");
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            if (!string.IsNullOrEmpty(lastOpponentMsg)) {
                var lines = WrapText(lastOpponentMsg, 54);
                Console.WriteLine($"║  {player2}: {lines[0].PadRight(54)}║");
                for (int li = 1; li < lines.Count; li++)
                    Console.WriteLine($"║  {lines[li].PadRight(58)}║");
            }
            Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
            string momentumLine = momentum >= 3 ? $"📈 MOMENTUM +{(momentum>=5?3:momentum>=4?2:2)} active" :
                                  momentum > 0  ? $"📈 Momentum: {momentum} win{(momentum>1?"s":"")}" : "";
            if (!string.IsNullOrEmpty(momentumLine))
                Console.WriteLine(FillLine(momentumLine));
            if (snap.ActiveTrapNames.Length > 0)
                Console.WriteLine(FillLine($"⚠️  Traps: {string.Join(", ", snap.ActiveTrapNames)}"));
            Console.WriteLine("║                                                          ║");

            char[] letters = { 'A', 'B', 'C', 'D' };
            for (int i = 0; i < turnStart.Options.Length; i++) {
                var opt = turnStart.Options[i];
                int mod = sableStats.GetEffective(opt.Stat);
                int dc = brickStats.GetDefenceDC(opt.Stat);
                int need = dc - mod;
                int pct = Math.Max(0, Math.Min(100, (21-need)*5));
                string icons = "";
                if (opt.HasTellBonus)              icons += " 📖";
                if (opt.ComboName != null)          icons += " ⭐";
                if (opt.CallbackTurnNumber.HasValue) icons += " 🔗";
                if (opt.HasWeaknessWindow)          icons += " 🔓";

                string header = $"{letters[i]}) 🎲 {StatLabel(opt.Stat)} ({mod:+#;-#;0})";
                string riskStr = $"{RiskLabel(need)}{icons}";
                // right-align risk label
                int headerPad = 40 - header.Length - riskStr.Length;
                Console.WriteLine(FillLine(header + (headerPad > 0 ? new string(' ', headerPad) : "  ") + riskStr));

                if (!string.IsNullOrEmpty(opt.IntendedText) && opt.IntendedText != "...") {
                    var wrapped = WrapText($"\"{opt.IntendedText}\"", 54);
                    foreach (var wl in wrapped) Console.WriteLine(FillLine("   " + wl));
                }
                Console.WriteLine(FillLine($"   DC {dc}  |  Need: {need}+  |  {pct}%"));
                Console.WriteLine(FillLine($"   Reward: {RewardRange(need)}  |  {XpMultiplier(need)}"));
                Console.WriteLine("║                                                          ║");
            }
            Console.WriteLine("║  ICONS: 🟢🟡🟠🔴 Risk | 🔓 Window | 🔗 Callback        ║");
            Console.WriteLine("║         ⭐ Combo | 📈 Momentum | 🔥 Forced | 📖 Tell    ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine("```");
            Console.WriteLine();

            // ── pick + roll ───────────────────────────────────────────────
            // Build current shadow values from tracker for player agent context
            var currentShadowValues = new Dictionary<ShadowStatType, int>();
            foreach (ShadowStatType shadowType in Enum.GetValues(typeof(ShadowStatType)))
                currentShadowValues[shadowType] = sableShadows.GetEffectiveShadow(shadowType);

            var agentContext = new PlayerAgentContext(
                playerStats: sableStats,
                opponentStats: brickStats,
                currentInterest: snap.Interest,
                interestState: snap.State,
                momentumStreak: snap.MomentumStreak,
                activeTrapNames: snap.ActiveTrapNames,
                sessionHorniness: 0,
                shadowValues: currentShadowValues,
                turnNumber: snap.TurnNumber);
            var decision = await agent.DecideAsync(turnStart, agentContext);
            int pick = decision.OptionIndex;
            var chosen = turnStart.Options[pick];
            Console.WriteLine($"**► Player picks: {letters[pick]} ({StatLabel(chosen.Stat)})**");
            Console.WriteLine();
            Console.WriteLine(PlaytestFormatter.FormatReasoningBlock(decision, agent.GetType().Name));
            Console.WriteLine(PlaytestFormatter.FormatScoreTable(decision, turnStart.Options));
            Console.WriteLine();

            TurnResult result;
            try { result = await session.ResolveTurnAsync(pick); }
            catch (GameEndedException ex) { finalOutcome = ex.Outcome; break; }

            var roll = result.Roll;
            string rollMod = $"{roll.StatModifier:+#;-#;0}";
            string rollResult;
            if (roll.IsNatTwenty)     rollResult = "NAT 20 ⭐";
            else if (roll.IsNatOne)   rollResult = "NAT 1 💀";
            else if (roll.Tier == FailureTier.None) rollResult = $"SUCCESS";
            else                      rollResult = roll.Tier.ToString().ToUpperInvariant();

            Console.WriteLine($"**🎲 Roll:** d20({roll.UsedDieRoll}) + {StatLabel(chosen.Stat)}({rollMod}) = **{roll.FinalTotal}** vs DC {roll.DC} → **Miss: {(roll.FinalTotal>=roll.DC ? $"−{roll.FinalTotal-roll.DC}" : $"+{roll.DC-roll.FinalTotal}")} → {rollResult}**");
            Console.WriteLine();

            if (result.ComboTriggered != null) Console.WriteLine($"> *⭐ {result.ComboTriggered} combo fires!*");
            if (result.TellReadBonus > 0)      Console.WriteLine($"> *📖 Tell read! +{result.TellReadBonus}*");
            Console.WriteLine();

            Console.WriteLine($"**📨 {player1} sends:**");
            Console.WriteLine($"> \"{result.DeliveredMessage}\"");
            Console.WriteLine();

            lastOpponentMsg = result.OpponentMessage ?? "";
            Console.WriteLine($"**📩 {player2} replies:**");
            Console.WriteLine($"> \"{result.OpponentMessage}\"");
            Console.WriteLine();

            int newInterest = result.StateAfter.Interest;
            int delta = result.InterestDelta;
            string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();
            Console.WriteLine("---");
            Console.WriteLine();
            Console.WriteLine("```");
            Console.WriteLine($"Interest: {InterestBar(newInterest)}  {newInterest}/25  ({deltaStr})");
            if (result.ShadowGrowthEvents?.Count > 0)
            {
                foreach (var shadowEvent in result.ShadowGrowthEvents)
                    Console.WriteLine($"\u26a0\ufe0f SHADOW GROWTH: {shadowEvent}");
            }
            string trapLine = result.StateAfter.ActiveTrapNames.Length > 0
                ? string.Join(", ", result.StateAfter.ActiveTrapNames) : "none";
            Console.WriteLine($"Active Traps: {trapLine}  |  Momentum: {result.StateAfter.MomentumStreak} win{(result.StateAfter.MomentumStreak!=1?"s":"")}");
            Console.WriteLine("```");

            interest = newInterest;
            momentum = result.StateAfter.MomentumStreak;

            if (result.NarrativeBeat != null) { Console.WriteLine(); Console.WriteLine($"✨ {result.NarrativeBeat}"); }
            if (result.IsGameOver) { finalOutcome = result.Outcome; break; }
        }

        // ── session summary ───────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("---");
        Console.WriteLine();
        Console.WriteLine("## Session Summary");
        Console.WriteLine();
        string outcomeIcon = finalOutcome == GameOutcome.DateSecured ? "✅" :
                             finalOutcome == GameOutcome.Unmatched  ? "💀" : "👻";
        Console.WriteLine($"**{outcomeIcon} {finalOutcome?.ToString() ?? "Incomplete"} | Turns: {turn} | Total XP: {session.TotalXpEarned}**");
        Console.WriteLine();

        // ── shadow delta table ────────────────────────────────────────────
        Console.WriteLine("## Shadow Changes This Session");
        Console.WriteLine("| Shadow | Start | End | Delta |");
        Console.WriteLine("|---|---|---|---|");
        foreach (ShadowStatType shadowType in Enum.GetValues(typeof(ShadowStatType)))
        {
            int start = sableStats.GetShadow(shadowType);
            int end = sableShadows.GetEffectiveShadow(shadowType);
            int shadowDelta = sableShadows.GetDelta(shadowType);
            string deltaFmt = shadowDelta > 0 ? $"+{shadowDelta}" : shadowDelta.ToString();
            Console.WriteLine($"| {shadowType} | {start} | {end} | {deltaFmt} |");
        }
        Console.WriteLine();

        llm.Dispose();

        Console.SetOut(tee._console);
        WritePlaytestLog(buffer.ToString(), player1, player2, finalOutcome, session.TotalXpEarned, turn);
        return 0;
    }

    static List<string> WrapText(string text, int maxLen)
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

    static void WritePlaytestLog(string content, string p1, string p2, GameOutcome? outcome, int xp, int turns)
    {
        string dir = "/root/.openclaw/agents-extra/pinder/design/playtests";
        if (!Directory.Exists(dir)) { Console.Error.WriteLine("Playtest dir not found"); return; }
        int nextNum = SessionFileCounter.GetNextSessionNumber(dir);
        string slug = $"session-{nextNum:D3}-{p1.ToLower()}-vs-{p2.ToLower()}.md";
        File.WriteAllText(Path.Combine(dir, slug), content);
        Console.WriteLine($"\n📝 Written → design/playtests/{slug}");
    }
}

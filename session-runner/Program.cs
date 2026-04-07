// Pinder Session Runner — real GameSession + AnthropicLlmAdapter
// Outputs markdown matching the session-001 playtest format
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Data;
using Pinder.LlmAdapters;
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

    // ── character loading ─────────────────────────────────────────────────────

    /// <summary>
    /// Load a character from either a definition file or a prompt file.
    /// Priority: --player-def path > --player name (try assembler first, then prompt file fallback).
    /// </summary>
    static CharacterProfile LoadCharacter(
        string? defPath,
        string? name,
        string promptDir,
        ref IItemRepository? itemRepo,
        ref IAnatomyRepository? anatomyRepo)
    {
        // Explicit --player-def / --opponent-def takes priority
        if (defPath != null)
        {
            EnsureReposLoaded(ref itemRepo, ref anatomyRepo);
            return CharacterDefinitionLoader.Load(defPath, itemRepo!, anatomyRepo!);
        }

        // --player / --opponent name: try assembler pipeline first
        if (name != null)
        {
            string? charDefPath = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "characters", $"{name.ToLowerInvariant()}.json"));

            if (charDefPath != null)
            {
                try
                {
                    EnsureReposLoaded(ref itemRepo, ref anatomyRepo);
                    return CharacterDefinitionLoader.Load(charDefPath, itemRepo!, anatomyRepo!);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WARN] Failed to load {name} via assembler: {ex.Message} — falling back to prompt file");
                }
            }

            // Fallback to prompt file loading
            return CharacterLoader.Load(name, promptDir);
        }

        throw new InvalidOperationException("Neither definition path nor name provided");
    }

    /// <summary>
    /// Lazily load item and anatomy repositories from data files.
    /// </summary>
    static void EnsureReposLoaded(ref IItemRepository? itemRepo, ref IAnatomyRepository? anatomyRepo)
    {
        if (itemRepo != null && anatomyRepo != null)
            return;

        string baseDir = AppContext.BaseDirectory;

        string? itemsPath = DataFileLocator.FindDataFile(baseDir, Path.Combine("data", "items", "starter-items.json"));
        if (itemsPath == null)
            throw new FileNotFoundException("Could not find data/items/starter-items.json — ensure data files are present in the repo");

        string? anatomyPath = DataFileLocator.FindDataFile(baseDir, Path.Combine("data", "anatomy", "anatomy-parameters.json"));
        if (anatomyPath == null)
            throw new FileNotFoundException("Could not find data/anatomy/anatomy-parameters.json — ensure data files are present in the repo");

        itemRepo = new JsonItemRepository(File.ReadAllText(itemsPath));
        anatomyRepo = new JsonAnatomyRepository(File.ReadAllText(anatomyPath));
    }

    // ── main ─────────────────────────────────────────────────────────────────

    // ── CLI arg parsing ─────────────────────────────────────────────────

    static int ParseMaxTurns(string[] args, int defaultValue = 20)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--max-turns" && int.TryParse(args[i + 1], out int val) && val > 0)
                return val;
        }
        return defaultValue;
    }

    static string ParseAgentArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--agent")
                return args[i + 1];
        }
        return "scoring";
    }

    static string? ParseArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }

    static string ResolvePromptDirectory(string baseDir)
    {
        // 1. Environment variable override
        string? envPath = Environment.GetEnvironmentVariable("PINDER_PROMPTS_PATH");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            return Path.GetFullPath(envPath!);

        // 2. Walk up from baseDir looking for design/examples/
        string? dir = baseDir;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "design", "examples");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }

        // 3. Hardcoded fallback
        const string fallback = "/root/.openclaw/agents-extra/pinder/design/examples";
        if (Directory.Exists(fallback))
            return fallback;

        return Path.Combine(baseDir, "design", "examples");
    }

    static void PrintUsage(string promptDir)
    {
        Console.Error.WriteLine("Usage: dotnet run --project session-runner -- --player <name> --opponent <name> [--max-turns <n>] [--agent <scoring|llm>]");
        Console.Error.WriteLine("       dotnet run --project session-runner -- --player-def <path> --opponent-def <path> [--max-turns <n>] [--agent <scoring|llm>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --player <name>       Player character name (required, or use --player-def)");
        Console.Error.WriteLine("  --opponent <name>      Opponent character name (required, or use --opponent-def)");
        Console.Error.WriteLine("  --player-def <path>   Player character definition JSON file");
        Console.Error.WriteLine("  --opponent-def <path>  Opponent character definition JSON file");
        Console.Error.WriteLine("  --max-turns <n>       Maximum turns (default: 20)");
        Console.Error.WriteLine("  --agent <type>        Player agent: scoring or llm (default: scoring)");
        Console.Error.WriteLine();
        string available = CharacterLoader.ListAvailable(promptDir);
        Console.Error.WriteLine($"Available characters: {available}");
    }

    static async Task<int> Main(string[] args)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) { Console.Error.WriteLine("ANTHROPIC_API_KEY not set"); return 1; }

        int maxTurns = ParseMaxTurns(args);
        string agentType = ParseAgentArg(args);
        bool isDebug = args.Contains("--debug");

        string promptDir = ResolvePromptDirectory(AppContext.BaseDirectory);

        // Parse character name / definition args
        string? playerArg = ParseArg(args, "--player");
        string? opponentArg = ParseArg(args, "--opponent");
        string? playerDefArg = ParseArg(args, "--player-def");
        string? opponentDefArg = ParseArg(args, "--opponent-def");

        // Must have at least one identifier per side
        if ((playerArg == null && playerDefArg == null) ||
            (opponentArg == null && opponentDefArg == null))
        {
            PrintUsage(promptDir);
            return 1;
        }

        // Preload assembler repos (lazy — only if needed)
        IItemRepository? itemRepo = null;
        IAnatomyRepository? anatomyRepo = null;

        CharacterProfile sable, brick;
        try
        {
            sable = LoadCharacter(playerDefArg, playerArg, promptDir, ref itemRepo, ref anatomyRepo);
            brick = LoadCharacter(opponentDefArg, opponentArg, promptDir, ref itemRepo, ref anatomyRepo);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }

        var buffer = new StringBuilder();
        var tee = new TeeWriter(Console.Out, buffer);
        Console.SetOut(tee);

        // ── character definitions (loaded from prompt files) ───────────────
        string player1 = sable.DisplayName, player2 = brick.DisplayName;
        int p1Level = sable.Level, p2Level = brick.Level;
        int p1LevelBonus = Pinder.Core.Progression.LevelTable.GetBonus(p1Level);
        int p2LevelBonus = Pinder.Core.Progression.LevelTable.GetBonus(p2Level);
        var sableStats = sable.Stats;
        var brickStats = brick.Stats;

        // ── resolve session number early so header matches filename ──────
        string? playtestDir = SessionFileCounter.ResolvePlaytestDirectory(AppContext.BaseDirectory);
        int sessionNumber = playtestDir != null ? SessionFileCounter.GetNextSessionNumber(playtestDir) : 1;

        // ── header ────────────────────────────────────────────────────────
        Console.WriteLine($"# Playtest Session {sessionNumber:D3} — {player1} × {player2}");
        Console.WriteLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd}");
        Console.WriteLine($"**Engine:** `pinder-core GameSession` + `AnthropicLlmAdapter` → claude-sonnet-4-20250514");
        string p1Archetype = sable.ActiveArchetype != null ? $" | Archetype: {sable.ActiveArchetype.Name} ({sable.ActiveArchetype.InterferenceLevel})" : "";
        string p2Archetype = brick.ActiveArchetype != null ? $" | Archetype: {brick.ActiveArchetype.Name} ({brick.ActiveArchetype.InterferenceLevel})" : "";
        Console.WriteLine($"**Player:** {player1} (Level {p1Level}, +{p1LevelBonus} level bonus{p1Archetype}) | **Opponent:** {player2} (Level {p2Level}, +{p2LevelBonus} level bonus, LLM puppet{p2Archetype})");
        Console.WriteLine();

        // ── character table ───────────────────────────────────────────────
        Console.WriteLine("## Characters");
        Console.WriteLine();
        Console.WriteLine($"***{player1} bio:*** *\"{sable.Bio}\"*");
        Console.WriteLine($"***{player2} bio:*** *\"{brick.Bio}\"*");
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
        Console.WriteLine($"## DC Reference ({player1} attacking, {player2} defending)");
        Console.WriteLine();
        Console.WriteLine($"| Stat | {player1} mod | {player2} defends | DC | Need | % | Risk |");
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
        // Load game-definition.yaml if present
        string? gameDefPath = DataFileLocator.FindDataFile(AppContext.BaseDirectory, Path.Combine("data", "game-definition.yaml"));
        GameDefinition? gameDef = null;
        if (gameDefPath != null)
        {
            try
            {
                gameDef = GameDefinition.LoadFrom(File.ReadAllText(gameDefPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to load game-definition.yaml: {ex.Message}");
            }
        }

        string? debugFile = null;
        if (isDebug && playtestDir != null)
        {
            debugFile = Path.Combine(playtestDir, $"session-{sessionNumber:D3}-debug.md");
        }

        var llm = new AnthropicLlmAdapter(new AnthropicOptions {
            ApiKey = apiKey, Model = "claude-sonnet-4-20250514", MaxTokens = 1024, Temperature = 0.9,
            GameDefinition = gameDef,
            DebugDirectory = debugFile
        });

        // Load real trap definitions — fallback to NullTrapRegistry if file missing/corrupt
        ITrapRegistry trapRegistry = TrapRegistryLoader.Load(AppContext.BaseDirectory, Console.Error);

        // Shadow tracking — wrap player's StatBlock so GameSession can track shadow growth
        var sableShadows = new SessionShadowTracker(sableStats);
        var config = new GameSessionConfig(playerShadows: sableShadows);
        var session = new GameSession(sable, brick, llm, new SystemRandomDiceRoller(), trapRegistry, config);

        // Player agent for decision-making — configurable via --agent arg or PLAYER_AGENT env var
        IPlayerAgent agent;
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
        // ── Matchup Analysis ──────────────────────────────────────────────
        Console.Error.WriteLine("Generating matchup analysis...");
        var analysisOptions = new AnthropicOptions {
            ApiKey = apiKey,
            Model = Environment.GetEnvironmentVariable("PLAYER_AGENT_MODEL") ?? "claude-sonnet-4-20250514"
        };
        var analysis = await MatchupAnalyzer.AnalyzeMatchupAsync(analysisOptions, sable, brick);
        if (!string.IsNullOrWhiteSpace(analysis))
        {
            Console.WriteLine(analysis);
            Console.WriteLine();
        }

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
        StatType? lastStatUsed = null;
        StatType? secondLastStatUsed = null;
        var conversationHistory = new List<(string Sender, string Text)>();

        while (turn < maxTurns)
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

            // // -- Option display: one block per option --
            if (!string.IsNullOrEmpty(lastOpponentMsg)) {
                Console.WriteLine($"**{player2}:** *\"{lastOpponentMsg}\"*");
                Console.WriteLine();
            }
            var statusParts = new System.Collections.Generic.List<string>();
            if (snap.ActiveTrapNames.Length > 0)
                statusParts.Add($"Traps: {string.Join(", ", snap.ActiveTrapNames)}");
            if (momentum >= 3)
                statusParts.Add($"Momentum +{(momentum>=5?3:momentum>=4?2:2)} ACTIVE");
            else if (momentum > 0)
                statusParts.Add($"Momentum: {momentum} win streak");
            if (statusParts.Count > 0) { Console.WriteLine(string.Join(" | ", statusParts)); Console.WriteLine(); }
            char[] letters = { 'A', 'B', 'C', 'D' };
            for (int i = 0; i < turnStart.Options.Length; i++) {
                var opt = turnStart.Options[i];
                int mod = sableStats.GetEffective(opt.Stat);
                int dc = brickStats.GetDefenceDC(opt.Stat);
                int need = dc - mod;
                int pct = Math.Max(0, Math.Min(100, (21-need)*5));
                string riskColor = RiskLabel(need);
                var badges = new System.Collections.Generic.List<string>();
                if (opt.HasTellBonus)               badges.Add("Tell +2");
                if (opt.ComboName != null)           badges.Add($"⭐ Combo: {opt.ComboName} ({PlaytestFormatter.GetComboRewardSummary(opt.ComboName)})");
                if (opt.CallbackTurnNumber.HasValue) badges.Add("Callback");
                if (opt.HasWeaknessWindow)           badges.Add("Window");
                // Shadow growth warnings (#644)
                bool honestyAvailable = Array.Exists(turnStart.Options, o => o.Stat == StatType.Honesty);
                if (opt.Stat != StatType.Honesty && honestyAvailable)
                    badges.Add("⚠️ Denial +1");
                if (opt.Stat == StatType.Honesty && honestyAvailable && interest >= 15)
                    badges.Add("✨ Denial -1");
                if (lastStatUsed.HasValue && secondLastStatUsed.HasValue
                    && lastStatUsed.Value == secondLastStatUsed.Value
                    && opt.Stat == lastStatUsed.Value)
                    badges.Add("⚠️ Fixation +1");
                if (lastStatUsed.HasValue && secondLastStatUsed.HasValue
                    && lastStatUsed.Value == secondLastStatUsed.Value
                    && opt.Stat != lastStatUsed.Value)
                    badges.Add("✨ breaks streak");
                string badgeStr = badges.Count > 0 ? " | " + string.Join(", ", badges) : "";
                Console.WriteLine($"**{letters[i]})** {StatLabel(opt.Stat)} {mod:+#;-#;0} | {pct}% {riskColor}{badgeStr}");
                
                if (opt.ComboName != null)
                {
                    Console.WriteLine($"> *{opt.ComboName}: {PlaytestFormatter.GetComboSequenceDescription(opt.ComboName)}*");
                }

                if (!string.IsNullOrEmpty(opt.IntendedText) && opt.IntendedText != "...")
                    Console.WriteLine($"> \"{opt.IntendedText}\"");
                Console.WriteLine();
            }

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
                turnNumber: snap.TurnNumber,
                playerSystemPrompt: sable.AssembledSystemPrompt,
                playerName: player1,
                opponentName: player2,
                recentHistory: conversationHistory.Count > 0 ? conversationHistory.AsReadOnly() : null);
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

            string marginText;
            if (roll.FinalTotal >= roll.DC)
            {
                if (roll.IsNatOne)
                {
                    marginText = $"Total beat DC by {roll.FinalTotal - roll.DC} — but NAT 1 💀 overrides";
                    rollResult = ""; // embedded in marginText
                }
                else
                {
                    marginText = $"Beat by {roll.FinalTotal - roll.DC}";
                }
            }
            else
            {
                marginText = $"Miss by {roll.DC - roll.FinalTotal}";
            }

            string arrowResult = string.IsNullOrEmpty(rollResult) ? "" : $" → {rollResult}";
            Console.WriteLine($"**🎲 Roll:** d20({roll.UsedDieRoll}) + {StatLabel(chosen.Stat)}({rollMod}) = **{roll.FinalTotal}** vs DC {roll.DC} → **{marginText}{arrowResult}**");

            // #484/#698: Inline rule explanation after roll
            string rollExplanation = GetRollExplanation(roll);
            if (!string.IsNullOrEmpty(rollExplanation))
                Console.WriteLine($"> 📋 *{rollExplanation}*");
            Console.WriteLine();

            if (result.ComboTriggered != null)
            {
                Console.WriteLine($"> *⭐ {result.ComboTriggered} combo fires!*");
                Console.WriteLine($"> *{PlaytestFormatter.GetComboSequenceDescription(result.ComboTriggered)}*");
                Console.WriteLine($"> *{PlaytestFormatter.GetComboRewardSummary(result.ComboTriggered)}*");
            }
            if (result.TellReadBonus > 0)      Console.WriteLine($"> *📖 Tell read! +{result.TellReadBonus}*");
            Console.WriteLine();

            Console.WriteLine($"**📨 {player1} sends:**");
            bool isSuccess = roll.Tier == FailureTier.None || roll.IsNatTwenty;
            bool isStrongSuccess = isSuccess && (roll.FinalTotal - roll.DC >= 5 || roll.IsNatTwenty);
            bool isFail = roll.Tier != FailureTier.None || roll.IsNatOne;
            
            if (isStrongSuccess || isFail)
            {
                string intended = chosen.IntendedText ?? "";
                string label = isStrongSuccess ? "Strong success" : 
                               roll.IsNatOne ? "Nat 1" : 
                               roll.Tier.ToString();
                if (roll.IsNatTwenty) label = "Nat 20";
                
                PrintQuoted("**Intended:** " + (string.IsNullOrWhiteSpace(intended) || intended == "..." ? "..." : $"\"{intended}\""));
                
                string marker = isStrongSuccess ? "*" : "~~";
                string formattedDelivered = FormatDeliveredAdditions(intended, result.DeliveredMessage ?? "", marker);
                PrintQuoted($"**Delivered ({label}):** \"{formattedDelivered}\"");
            }
            else
            {
                PrintQuoted(result.DeliveredMessage);
            }
            Console.WriteLine();

            // Track conversation history for LLM agent context (#492)
            if (!string.IsNullOrEmpty(result.DeliveredMessage))
                conversationHistory.Add((player1, result.DeliveredMessage));
            lastOpponentMsg = result.OpponentMessage ?? "";
            if (!string.IsNullOrEmpty(lastOpponentMsg))
                conversationHistory.Add((player2, lastOpponentMsg));
            Console.WriteLine($"**📩 {player2} replies:**");
            PrintQuoted(result.OpponentMessage);
            Console.WriteLine();

            int newInterest = result.StateAfter.Interest;
            int delta = result.InterestDelta;
            string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();
            Console.WriteLine("---");
            Console.WriteLine();
            Console.WriteLine("```");
            Console.WriteLine($"Interest: {InterestBar(newInterest)}  {newInterest}/25  ({deltaStr})");

            // #699: Interest delta breakdown
            if (delta != 0 && result.Roll != null)
            {
                var parts = new List<string>();
                if (result.Roll.IsNatOne) parts.Add("Nat 1: -4");
                else if (result.Roll.IsNatTwenty) parts.Add("Nat 20: +4 base");
                else if (result.Roll.IsSuccess)
                {
                    int beat = result.Roll.FinalTotal - result.Roll.DC;
                    if (beat >= 10) parts.Add("crit: +3 base");
                    else if (beat >= 5) parts.Add("strong: +2 base");
                    else parts.Add("clean: +1 base");
                }
                else parts.Add($"fail: {result.InterestDelta}");
                if (result.ComboTriggered != null) parts.Add("combo bonus");
                if (result.TellReadBonus > 0) parts.Add($"tell +{result.TellReadBonus}");
                if (result.CallbackBonusApplied > 0) parts.Add($"callback +{result.CallbackBonusApplied}");
                if (parts.Count > 0)
                    Console.WriteLine($"  *({string.Join(", ", parts)})*");
            }
            if (result.ShadowGrowthEvents?.Count > 0)
            {
                foreach (var shadowEvent in result.ShadowGrowthEvents)
                {
                    Console.WriteLine($"\u26a0\ufe0f SHADOW GROWTH: {shadowEvent}");
                    // #484: Shadow threshold warnings
                    foreach (ShadowStatType sType in Enum.GetValues(typeof(ShadowStatType)))
                    {
                        if (shadowEvent.Contains(sType.ToString()))
                        {
                            int sv = sableShadows.GetEffectiveShadow(sType);
                            string paired = GetPairedStat(sType);
                            if (sv == 6) Console.WriteLine($"> ⚠️ *Threshold 6: {sType} now taints {paired} dialogue.*");
                            else if (sv == 12) Console.WriteLine($"> ⚠️ *Threshold 12: {sType} now penalizes {paired} rolls.*");
                            else if (sv == 18) Console.WriteLine($"> ⚠️ *Threshold 18: {sType} may override your {paired} choices.*");
                        }
                    }
                }
            }
            string trapLine = result.StateAfter.ActiveTrapNames.Length > 0
                ? string.Join(", ", result.StateAfter.ActiveTrapNames) : "none";
            Console.WriteLine($"Active Traps: {trapLine}  |  Momentum: {result.StateAfter.MomentumStreak} win{(result.StateAfter.MomentumStreak!=1?"s":"")}");
            Console.WriteLine("```");

            // #481/#700: Momentum tooltip
            if (result.StateAfter.MomentumStreak >= 3)
            {
                int mBonus = result.StateAfter.MomentumStreak >= 5 ? 3 : 2;
                Console.WriteLine($"> *Momentum: +{mBonus} added to your next roll total.*");
            }

            // #700: Interest state change explanation
            {
                InterestState stateBefore = snap.State;
                InterestState stateAfter = result.StateAfter.State;
                if (stateBefore != stateAfter)
                {
                    Console.WriteLine($"*** Interest state changed to {stateAfter} ***");
                    string stateDesc = GetInterestStateDescription(stateAfter);
                    if (!string.IsNullOrEmpty(stateDesc))
                        Console.WriteLine($"> *{stateDesc}*");
                }
            }

            interest = newInterest;
            momentum = result.StateAfter.MomentumStreak;

            // Track stat usage for shadow warnings (#644)
            secondLastStatUsed = lastStatUsed;
            lastStatUsed = chosen.Stat;

            if (result.NarrativeBeat != null) { Console.WriteLine(); Console.WriteLine($"{result.NarrativeBeat}"); }
            if (result.IsGameOver) { finalOutcome = result.Outcome; break; }
        }

        // ── session summary ───────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("---");
        Console.WriteLine();
        Console.WriteLine("## Session Summary");
        Console.WriteLine();
        bool isCutoff = finalOutcome == null;
        string outcomeIcon = finalOutcome == GameOutcome.DateSecured ? "✅" :
                             finalOutcome == GameOutcome.Unmatched  ? "💀" :
                             isCutoff ? "⏸️" : "👻";
        string outcomeLabel = isCutoff ? $"Incomplete ({turn}/{maxTurns} turns)" : finalOutcome.ToString()!;
        Console.WriteLine($"**{outcomeIcon} {outcomeLabel} | Interest: {interest}/25 | Total XP: {session.TotalXpEarned}**");

        if (isCutoff)
        {
            // Compute interest state from current interest value
            InterestState currentState = interest <= 0  ? InterestState.Unmatched :
                                         interest <= 4  ? InterestState.Bored :
                                         interest <= 9  ? InterestState.Lukewarm :
                                         interest <= 15 ? InterestState.Interested :
                                         interest <= 20 ? InterestState.VeryIntoIt :
                                         interest <= 24 ? InterestState.AlmostThere :
                                                          InterestState.DateSecured;
            string projection = OutcomeProjector.Project(
                interest, momentum, turn, maxTurns, currentState);
            Console.WriteLine();
            Console.WriteLine($"Projected: {projection}");
        }
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
        WritePlaytestLog(buffer.ToString(), player1, player2, playtestDir, sessionNumber);
        return 0;
    }

    internal static string FormatDeliveredAdditions(string intended, string delivered, string marker) {
        if (string.IsNullOrWhiteSpace(intended) || intended == "...") return $"{marker}{delivered}{marker}";
        
        int exactIdx = delivered.IndexOf(intended, StringComparison.OrdinalIgnoreCase);
        if (exactIdx >= 0) return WrapAdditions(intended, delivered, exactIdx, marker);
        
        string normalized = Regex.Replace(intended, @"\s+", " ");
        string pattern = Regex.Escape(normalized).Replace(@"\ ", @"\s+");
        var match = Regex.Match(delivered, pattern, RegexOptions.IgnoreCase);
        if (match.Success) {
            return WrapAdditions(match.Value, delivered, match.Index, marker);
        }
        
        return $"{marker}{delivered}{marker}";
    }
    
    internal static string WrapAdditions(string matchStr, string delivered, int exactIdx, string marker) {
        string before = delivered.Substring(0, exactIdx);
        string after = delivered.Substring(exactIdx + matchStr.Length);
        
        string afterSpace = "";
        while (after.Length > 0 && (char.IsWhiteSpace(after[0]) || char.IsPunctuation(after[0]))) {
            afterSpace += after[0];
            after = after.Substring(1);
        }
        
        string beforeSpace = "";
        while (before.Length > 0 && (char.IsWhiteSpace(before[before.Length - 1]) || char.IsPunctuation(before[before.Length - 1]))) {
            beforeSpace = before[before.Length - 1] + beforeSpace;
            before = before.Substring(0, before.Length - 1);
        }
        
        string res = "";
        if (before.Length > 0) res += marker + before + marker + beforeSpace;
        else res += beforeSpace;
        
        res += matchStr;
        
        if (after.Length > 0) res += afterSpace + marker + after + marker;
        else res += afterSpace;
        
        return res;
    }

    static void PrintQuoted(string? text)
    {
        if (string.IsNullOrEmpty(text)) { Console.WriteLine("> (empty)"); return; }
        // Prefix every line with > so multi-paragraph messages stay in the quote block
        foreach (var line in text.Split('\n'))
        {
            // Blank lines need "> " not just ">" to be a valid blockquote continuation
            Console.WriteLine(string.IsNullOrWhiteSpace(line) ? ">" : $"> {line.TrimEnd()}");
        }
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

    // #484/#698: Roll explanation helper
    static string GetRollExplanation(RollResult roll)
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

    // #700: Interest state description helper
    static string GetInterestStateDescription(InterestState state) => state switch
    {
        InterestState.Bored => "Ghost risk: 25% per turn. Opponent may stop responding.",
        InterestState.Lukewarm => "Opponent is present but unconvinced. No ghost risk.",
        InterestState.Interested => "Conversation has traction. Opponent is engaged.",
        InterestState.VeryIntoIt => "Opponent is genuinely interested. +advantage on rolls.",
        InterestState.AlmostThere => "One step from the date. Opponent is deciding.",
        InterestState.DateSecured => "Date secured. Opponent agreed to meet.",
        _ => ""
    };

    // #484: Shadow-to-stat pairing helper
    static string GetPairedStat(ShadowStatType shadow) => shadow switch
    {
        ShadowStatType.Madness => "Charm",
        ShadowStatType.Horniness => "Rizz",
        ShadowStatType.Denial => "Honesty",
        ShadowStatType.Fixation => "Chaos",
        ShadowStatType.Dread => "Wit",
        ShadowStatType.Overthinking => "Self-Awareness",
        _ => shadow.ToString()
    };

    static void WritePlaytestLog(string content, string p1, string p2, string? dir, int sessionNumber)
    {
        if (dir == null) { Console.Error.WriteLine("Playtest dir not found — set PINDER_PLAYTESTS_PATH or ensure design/playtests/ exists"); return; }
        string slug = $"session-{sessionNumber:D3}-{p1.ToLower()}-vs-{p2.ToLower()}.md";
        File.WriteAllText(Path.Combine(dir, slug), content);
        Console.WriteLine($"\n📝 Written → {dir}/{slug}");
    }
}

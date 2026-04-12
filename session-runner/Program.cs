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
using Pinder.LlmAdapters.OpenAi;
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
        need <= 7  ? "🟢 Safe" :
        need <= 11 ? "🟡 Medium" :
        need <= 15 ? "🟠 Hard" :
        need <= 19 ? "🔴 Bold" : "☠️ Reckless";

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

    static int ParseMaxTurns(string[] args, int defaultValue = 30)
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
        return "llm";
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
        Console.Error.WriteLine("  --agent <type>        Player agent: scoring or llm (default: llm)");
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

        // --difficulty <pct>: reduce success chance by N% (e.g. --difficulty 20 = 20% harder).
        // Implemented as a DC bias: dcBias = (int)Math.Round(20.0 * pct / 100.0)
        int difficultyBias = 0;
        string? difficultyArg = ParseArg(args, "--difficulty");
        if (difficultyArg != null && double.TryParse(difficultyArg, out double difficultyPct) && difficultyPct != 0)
        {
            difficultyBias = (int)Math.Round(20.0 * difficultyPct / 100.0);
            Console.Error.WriteLine($"Difficulty bias: {(difficultyPct > 0 ? "+" : "")}{difficultyPct}% → dcBias={difficultyBias}");
        }

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
        Console.WriteLine();
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
        Console.WriteLine($"| Stat | {player1} mod | {player2} defends with | DC | Need | % | Risk |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var stat in new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness }) {
            int atkMod = sableStats.GetEffective(stat);
            StatType defStat = Pinder.Core.Stats.StatBlock.DefenceTable[stat];
            int defMod = brickStats.GetEffective(defStat);
            int dc = brickStats.GetDefenceDC(stat);
            int need = dc - (atkMod + p1LevelBonus); // include level bonus
            // need ≥20 = Reckless (only Nat 20 succeeds = 5%); else standard formula
            int pct = need >= 20 ? 5 : Math.Max(0, Math.Min(100, (21 - need) * 5));
            Console.WriteLine($"| {StatLabel(stat)} | {atkMod:+#;-#;0} | {StatLabel(defStat)} {defMod:+#;-#;0} | {dc} | {need}+ | {pct}% | {RiskLabel(need)} |");
        }
        Console.WriteLine();
        Console.WriteLine("> DC = 16 + opponent defending stat modifier. Miss by 1–2 = Fumble | 3–5 = Misfire | 6–9 = Trope Trap | 10+ = Catastrophe | Nat 1 = Legendary.");
        Console.WriteLine();

        // ── steering roll explanation ─────────────────────────────────────
        int steeringMod = (sableStats.GetEffective(StatType.Charm) + sableStats.GetEffective(StatType.Wit) + sableStats.GetEffective(StatType.SelfAwareness)) / 3;
        int steeringDC = 16 + (brickStats.GetEffective(StatType.SelfAwareness) + brickStats.GetEffective(StatType.Rizz) + brickStats.GetEffective(StatType.Honesty)) / 3;
        Console.WriteLine($"> 🧭 **Steering**: After each delivery, {player1} may append a follow-up sentence.");
        Console.WriteLine($"> Roll: d20 + (CHARM+WIT+SA)/3 = +{steeringMod} vs DC = 16 + (opponent SA+RIZZ+HONESTY)/3 = {steeringDC}");
        Console.WriteLine("> On success: adds a steering question. No interest effect — purely narrative.");
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

        // Load delivery-instructions.yaml if present
        string? deliveryInstructionsPath = DataFileLocator.FindDataFile(AppContext.BaseDirectory, Path.Combine("data", "delivery-instructions.yaml"));
        StatDeliveryInstructions? statDeliveryInstructions = null;
        if (deliveryInstructionsPath != null)
        {
            try
            {
                statDeliveryInstructions = StatDeliveryInstructions.LoadFrom(File.ReadAllText(deliveryInstructionsPath));
                Console.Error.WriteLine("Loaded delivery-instructions.yaml");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to load delivery-instructions.yaml: {ex.Message}");
            }
        }

        string? debugFile = null;
        if (isDebug && playtestDir != null)
        {
            debugFile = Path.Combine(playtestDir, $"session-{sessionNumber:D3}-debug.md");
        }

        string modelSpec = ParseArg(args, "--model") ?? "";
        IStatefulLlmAdapter llm;
        if (modelSpec.StartsWith("groq/") || modelSpec.StartsWith("together/") ||
            modelSpec.StartsWith("openrouter/") || modelSpec.StartsWith("ollama/"))
        {
            string[] providerParts = modelSpec.Split(new[] { '/' }, 2);
            string provider = providerParts[0];
            string model = providerParts.Length > 1 ? providerParts[1] : modelSpec;
            string baseUrl = GetProviderBaseUrl(provider);
            string envKey = provider.ToUpperInvariant() + "_API_KEY";
            string openAiKey = Environment.GetEnvironmentVariable(envKey) ?? apiKey;
            llm = new OpenAiLlmAdapter(new OpenAiOptions
            {
                ApiKey = openAiKey,
                BaseUrl = baseUrl,
                Model = model,
                MaxTokens = 1024,
                Temperature = 0.9,
                GameDefinition = gameDef,
                DebugDirectory = debugFile,
                StatDeliveryInstructions = statDeliveryInstructions
            });
        }
        else
        {
            llm = new AnthropicLlmAdapter(new AnthropicOptions
            {
                ApiKey = apiKey, Model = "claude-sonnet-4-20250514", MaxTokens = 1024, Temperature = 0.9,
                GameDefinition = gameDef,
                DebugDirectory = debugFile,
                StatDeliveryInstructions = statDeliveryInstructions
            });
        }

        // Load real trap definitions — fallback to NullTrapRegistry if file missing/corrupt
        ITrapRegistry trapRegistry = TrapRegistryLoader.Load(AppContext.BaseDirectory, Console.Error);

        // Shadow tracking — wrap player's StatBlock so GameSession can track shadow growth
        var sableShadows = new SessionShadowTracker(sableStats);

        // Create real wall clock with time-of-day horniness modifiers from game definition
        var now = DateTimeOffset.Now;
        Pinder.Core.Conversation.GameClock clock;
        if (gameDef != null)
        {
            var mods = gameDef.HorninessTimeModifiers;
            var horninessModifiers = new Pinder.Core.Conversation.HorninessModifiers(
                mods.Morning, mods.Afternoon, mods.Evening, mods.Overnight);
            clock = new Pinder.Core.Conversation.GameClock(now, horninessModifiers);
        }
        else
        {
            // Fallback: zero modifiers when game definition is unavailable
            var zeroModifiers = new Pinder.Core.Conversation.HorninessModifiers(0, 0, 0, 0);
            clock = new Pinder.Core.Conversation.GameClock(now, zeroModifiers);
        }

        // Display time-of-day info in session header
        {
            int hour = now.Hour;
            int min = now.Minute;
            var band = clock.GetTimeOfDay();
            int modifier = clock.GetHorninessModifier();
            string modDisplay = modifier >= 0 ? $"+{modifier}" : modifier.ToString();
            Console.WriteLine($"🕐 Time: {hour:00}:{min:00} UTC — {band} | Horniness modifier: {modDisplay}");
            Console.WriteLine();
        }

        int yamlDcBias = gameDef.GlobalDcBias;
        int totalDcBias = difficultyBias + yamlDcBias;
        var config = new GameSessionConfig(clock: clock, playerShadows: sableShadows, globalDcBias: totalDcBias, statDeliveryInstructions: statDeliveryInstructions);
        var session = new GameSession(sable, brick, llm, new SystemRandomDiceRoller(), trapRegistry, config);

        // Display session horniness in header (#709)
        {
            int sh = session.SessionHorniness;
            int horninessDC = 20 - sh;
            Console.WriteLine($"🌶️ Session Horniness: {sh}  (per-turn DC {horninessDC} — miss = message deformation)");
            Console.WriteLine($"   → Fumble/Misfire/TropeTrap/Catastrophe tier on miss (same as roll failure tiers)");
        }
        Console.WriteLine();

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
        else if (agentType.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            agent = new HumanPlayerAgent();
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

        // ── Psychological Stakes ──────────────────────────────────────────
        Console.Error.WriteLine("Generating psychological stakes...");
        string p1Stake = await GeneratePsychologicalStakeAsync(apiKey, sable.AssembledSystemPrompt, player1).ConfigureAwait(false);
        string p2Stake = await GeneratePsychologicalStakeAsync(apiKey, brick.AssembledSystemPrompt, player2).ConfigureAwait(false);
        sable.PsychologicalStake = p1Stake;
        brick.PsychologicalStake = p2Stake;

        if (!string.IsNullOrWhiteSpace(sable.PsychologicalStake))
        {
            Console.WriteLine();
            Console.WriteLine($"### {player1} — Psychological Stake");
            Console.WriteLine();
            Console.WriteLine(sable.PsychologicalStake);
            Console.WriteLine();
        }
        if (!string.IsNullOrWhiteSpace(brick.PsychologicalStake))
        {
            Console.WriteLine();
            Console.WriteLine($"### {player2} — Psychological Stake");
            Console.WriteLine();
            Console.WriteLine(brick.PsychologicalStake);
            Console.WriteLine();
        }

        // Freeze base prompts before appending stakes — BaseSystemPrompt stays clean
        // for opponent profile injection (player must not see opponent's stake as prior knowledge)
        sable.FreezeBasePrompt();
        brick.FreezeBasePrompt();

        // Inject stakes into assembled system prompts
        if (!string.IsNullOrWhiteSpace(sable.PsychologicalStake))
            sable.AppendToSystemPrompt("\n\n== PSYCHOLOGICAL STAKE ==\n\n" + sable.PsychologicalStake);
        if (!string.IsNullOrWhiteSpace(brick.PsychologicalStake))
            brick.AppendToSystemPrompt("\n\n== PSYCHOLOGICAL STAKE ==\n\n" + brick.PsychologicalStake);

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

        // Shadow hint tracking state (#644)
        var statsUsedHistory = new List<StatType>();
        var highestPctHistory = new List<bool>();
        int charmUsageCount = 0;
        bool charmMadnessTriggered = false;
        int saUsageCount = 0;
        bool saOverthinkingTriggered = false;
        int rizzCumulativeFailureCount = 0;

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
            Console.WriteLine($"## ═══ TURN {turn} ═══  [{DateTime.UtcNow.ToString("HH:mm:ss")} UTC]");
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
                int need = dc - (mod + p1LevelBonus); // include level bonus
                // Compute display bonuses (tell, Triple combo, callback)
                int displayBonus = 0;
                var bonusAnnotations = new System.Collections.Generic.List<string>();
                if (opt.HasTellBonus) { displayBonus += 2; bonusAnnotations.Add("+2 tell"); }
                if (snap.TripleBonusActive) { displayBonus += 1; bonusAnnotations.Add("+1 Triple"); }
                if (opt.CallbackTurnNumber.HasValue)
                {
                    int cbBonus = CallbackBonus.Compute(snap.TurnNumber, opt.CallbackTurnNumber.Value);
                    if (cbBonus > 0) { displayBonus += cbBonus; bonusAnnotations.Add($"+{cbBonus} callback"); }
                }
                // need ≥20 = Reckless (only Nat 20 succeeds = 5%)
                int effectiveNeed = need - displayBonus;
                int pct = effectiveNeed >= 20 ? 5 : Math.Max(0, Math.Min(100, (21-effectiveNeed)*5));
                string pctAnnotation = bonusAnnotations.Count > 0 ? $" ({string.Join(", ", bonusAnnotations)})" : "";
                string riskColor = RiskLabel(need);
                int riskBonus = need <= 7 ? 1 : need <= 11 ? 2 : need <= 15 ? 3 : need <= 19 ? 5 : 10;
                string riskBonusTag = $" [+{riskBonus}i★]"; // always show — Reckless shows +10
                var badges = new System.Collections.Generic.List<string>();
                if (opt.HasTellBonus)               badges.Add("📖 Tell (+2 bonus)");
                if (opt.ComboName != null)           badges.Add($"⭐ Combo: {opt.ComboName} ({PlaytestFormatter.GetComboRewardSummary(opt.ComboName)})");
                if (opt.CallbackTurnNumber.HasValue) badges.Add($"🔄 Callback (references turn {opt.CallbackTurnNumber.Value})");
                if (opt.HasWeaknessWindow)           badges.Add("🎯 Window (+DC reduction)");
                // Shadow growth warnings and reduction hints (#644)
                var shadowCtx = new ShadowHintContext
                {
                    StatsUsedHistory = statsUsedHistory,
                    HighestPctHistory = highestPctHistory,
                    CurrentInterest = interest,
                    CharmUsageCount = charmUsageCount,
                    CharmMadnessTriggered = charmMadnessTriggered,
                    SaUsageCount = saUsageCount,
                    SaOverthinkingTriggered = saOverthinkingTriggered,
                    RizzCumulativeFailureCount = rizzCumulativeFailureCount,
                    CurrentOptions = turnStart.Options,
                    PlayerStats = sableStats,
                    OpponentStats = brickStats,
                    PlayerLevelBonus = p1LevelBonus,
                    HonestyAvailable = Array.Exists(turnStart.Options, o => o.Stat == StatType.Honesty)
                };
                var shadowHints = ShadowHintComputer.ComputeShadowHints(opt, shadowCtx);
                badges.AddRange(shadowHints);
                // Streak-breaking hint (not shadow-specific)
                if (lastStatUsed.HasValue && secondLastStatUsed.HasValue
                    && lastStatUsed.Value == secondLastStatUsed.Value
                    && opt.Stat != lastStatUsed.Value)
                    badges.Add("✨ breaks streak");
                string badgeStr = badges.Count > 0 ? " | " + string.Join(", ", badges) : "";
                Console.WriteLine($"**{letters[i]})** {StatLabel(opt.Stat)} {mod:+#;-#;0} | {pct}%{pctAnnotation} {riskColor}{riskBonusTag}{badgeStr}");
                
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
                sessionHorniness: session.SessionHorniness,
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
            string lvPart = roll.LevelBonus > 0 ? $"+Lv({roll.LevelBonus:+#;-#;0})" : "";
            // #732: append external bonus components to roll formula
            var _extParts = new System.Text.StringBuilder();
            if (result.TripleBonusApplied > 0)    _extParts.Append($"+Triple(+{result.TripleBonusApplied})");
            if (result.TellReadBonus > 0)          _extParts.Append($"+Tell(+{result.TellReadBonus})");
            if (result.CallbackBonusApplied > 0)   _extParts.Append($"+Callback(+{result.CallbackBonusApplied})");
            string extBonusPart = _extParts.ToString();
            string rollResult;
            if (roll.IsNatTwenty)     rollResult = "NAT 20 ⭐ — always succeeds";
            else if (roll.IsNatOne)   rollResult = "NAT 1 💀 — always fails";
            else if (roll.Tier == FailureTier.None) rollResult = $"SUCCESS";
            else                      rollResult = roll.Tier.ToString().ToUpperInvariant();

            string marginText;
            if (roll.FinalTotal >= roll.DC)
            {
                if (roll.IsNatOne)
                {
                    marginText = $"Total beat DC by {roll.FinalTotal - roll.DC} — but NAT 1 💀 always fails";
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
            Console.WriteLine($"**🎲 Roll:** d20({roll.UsedDieRoll})+{StatLabel(chosen.Stat)}({rollMod}){lvPart}{extBonusPart} = **{roll.FinalTotal}** vs DC {roll.DC} → **{marginText}{arrowResult}**");

            // #484/#698: Inline rule explanation after roll
            string rollExplanation = GetRollExplanation(roll);
            if (!string.IsNullOrEmpty(rollExplanation))
                Console.WriteLine($"> 📋 *{rollExplanation}*");

            // #727: Show activated trap name, effect, and duration
            if (roll.ActivatedTrap != null)
            {
                var trap = roll.ActivatedTrap;
                string effectDesc = trap.Effect switch
                {
                    TrapEffect.Disadvantage        => $"disadvantage on {trap.Stat} rolls",
                    TrapEffect.StatPenalty         => $"-{trap.EffectValue} to {trap.Stat} rolls",
                    TrapEffect.OpponentDCIncrease  => $"opponent DC +{trap.EffectValue}",
                    _                              => trap.Effect.ToString()
                };
                int dur = trap.DurationTurns;
                string turnWord = dur == 1 ? "turn" : "turns";
                Console.WriteLine($"> 🪤 *Trap activated: {trap.Id} [{trap.Stat}] — {effectDesc} for {dur} {turnWord} (clear with {trap.ClearMethod})*");
            }
            Console.WriteLine();

            if (result.TripleBonusApplied > 0)
                Console.WriteLine($"> *⚡ Combo: The Triple — +{result.TripleBonusApplied} to this roll*");
            if (result.ComboTriggered != null)
            {
                Console.WriteLine($"> *⭐ {result.ComboTriggered} combo fires!*");
                Console.WriteLine($"> *{PlaytestFormatter.GetComboSequenceDescription(result.ComboTriggered)}*");
                Console.WriteLine($"> *{PlaytestFormatter.GetComboRewardSummary(result.ComboTriggered)}*");
            }
            if (result.TellReadBonus > 0)      Console.WriteLine($"> *📖 Tell read! +{result.TellReadBonus}*");
            Console.WriteLine();

            // Steering roll display (#734 — moved before message delivery block)
            if (result.Steering != null && result.Steering.SteeringAttempted)
            {
                int steeringTotal = result.Steering.SteeringRoll + result.Steering.SteeringMod;
                if (result.Steering.SteeringSucceeded)
                {
                    Console.WriteLine($"> 🧭 Steering roll: d20({result.Steering.SteeringRoll}) + {result.Steering.SteeringMod} = {steeringTotal} vs DC {result.Steering.SteeringDC} → SUCCESS");
                    Console.WriteLine($"> *{player1} adds:* \"{result.Steering.SteeringQuestion}\"");
                }
                else
                {
                    Console.WriteLine($"> 🧭 Steering roll: d20({result.Steering.SteeringRoll}) + {result.Steering.SteeringMod} = {steeringTotal} vs DC {result.Steering.SteeringDC} → MISS");
                }
                Console.WriteLine();
            }

            // Per-turn horniness check display (#709, moved after steering roll per #742)
            if (result.HorninessCheck != null && result.HorninessCheck.DC > 0)
            {
                var hc = result.HorninessCheck;
                string hcResult = hc.IsMiss
                    ? $"MISS ({hc.Tier}){(hc.OverlayApplied ? " — overlay applied" : "")}"
                    : "OK";
                Console.WriteLine($"> 🌶️ Horniness check: d20({hc.Roll}) + {hc.Modifier} = {hc.Total} vs DC {hc.DC} → {hcResult}");
                Console.WriteLine();
            }

            Console.WriteLine($"**📨 {player1} sends:**");
            bool isSuccess = roll.Tier == FailureTier.None || roll.IsNatTwenty;
            bool isStrongSuccess = isSuccess && (roll.FinalTotal - roll.DC >= 5 || roll.IsNatTwenty);
            bool isFail = roll.Tier != FailureTier.None || roll.IsNatOne;
            
            // #736: Layered display — tier modifier and horniness overlay shown as separate layers
            bool tierFired = isStrongSuccess || isFail;
            bool horninessFired = result.HorninessCheck != null && result.HorninessCheck.OverlayApplied;

            if (!tierFired && !horninessFired)
            {
                // Clean success, no horniness — just show the message
                PrintQuoted(result.DeliveredMessage);
            }
            else
            {
                string intended = chosen.IntendedText ?? "";
                string intendedDisplay = string.IsNullOrWhiteSpace(intended) || intended == "..." ? "..." : $"\"{intended}\"";
                PrintQuoted("**Intended:** " + intendedDisplay);
                Console.WriteLine();

                if (tierFired)
                {
                    string label = isStrongSuccess ? "Strong success" :
                                   roll.IsNatOne ? "Nat 1" :
                                   roll.Tier.ToString();
                    if (roll.IsNatTwenty) label = "Nat 20";
                    string marker = isStrongSuccess ? "__" : "*";
                    // When horniness also fired, use the delivered message
                    string tierText = result.DeliveredMessage ?? "";
                    string formattedDelivered = FormatDeliveredAdditions(intended, tierText, marker);
                    PrintQuoted($"**Delivered ({label}):** \"{formattedDelivered}\"");
                    Console.WriteLine();
                }

                if (horninessFired)
                {
                    var hc = result.HorninessCheck!;
                    string horninessLabel = hc.Tier != Pinder.Core.Rolls.FailureTier.None ? hc.Tier.ToString() : "Overlay";
                    PrintQuoted($"**After horniness ({horninessLabel}):** \"{result.DeliveredMessage}\"");
                    Console.WriteLine();
                }
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

                // Base delta (success scale or failure scale)
                if (result.Roll.IsSuccess)
                {
                    string baseSign = result.BaseInterestDelta >= 0 ? "+" : "";
                    parts.Add($"Roll success {baseSign}{result.BaseInterestDelta}");
                }
                else
                {
                    string tierName = result.Roll.Tier.ToString();
                    parts.Add($"{tierName} miss {result.BaseInterestDelta}");
                }

                // Risk tier bonus (success only)
                if (result.RiskBonusDelta != 0)
                {
                    string riskSign = result.RiskBonusDelta >= 0 ? "+" : "";
                    parts.Add($"Risk bonus ({result.RiskTier}) {riskSign}{result.RiskBonusDelta}");
                }

                // Combo bonus
                if (result.ComboBonusDelta != 0 && result.ComboTriggered != null)
                {
                    string comboSign = result.ComboBonusDelta >= 0 ? "+" : "";
                    string comboExpl = PlaytestFormatter.GetComboBreakdownExplanation(result.ComboTriggered);
                    parts.Add($"Combo: {result.ComboTriggered} {comboSign}{result.ComboBonusDelta} ({comboExpl})");
                }

                if (parts.Count > 0)
                    Console.WriteLine($"  ↳ {string.Join(" | ", parts)}");
            }
            // #743: Horniness penalty display
            if (result.HorninessInterestPenalty != 0)
            {
                int penaltyAfter = result.HorninessInterestBefore + result.HorninessInterestPenalty;
                Console.WriteLine($"  ↳ Horniness penalty: interest halved ({result.HorninessInterestBefore} → {penaltyAfter})");
            }
            if (result.ShadowGrowthEvents?.Count > 0)
            {
                var enrichedShadow = new List<string>();
                foreach (var se in result.ShadowGrowthEvents)
                    enrichedShadow.Add(PlaytestFormatter.EnrichShadowEvent(se));
                Console.WriteLine($"📊 Shadow: {string.Join(" | ", enrichedShadow)}");
                // #484: Shadow threshold warnings
                foreach (var shadowEvent in result.ShadowGrowthEvents)
                {
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
            // #700: Active trap details
            if (result.StateAfter.ActiveTrapDetails.Length > 0)
            {
                foreach (var trap in result.StateAfter.ActiveTrapDetails)
                {
                    Console.WriteLine($"🪤 Trap: {trap.Name} [{trap.Stat}] — {trap.TurnsRemaining} turn{(trap.TurnsRemaining != 1 ? "s" : "")} remaining — {trap.PenaltyDescription} (activated by {trap.Stat} check failure)");
                }
            }
            else
            {
                Console.WriteLine("Active Traps: none");
                // #728: 1-turn traps expire before state snapshot — show acknowledgement
                var activatedTrap = result.Roll?.ActivatedTrap;
                if (activatedTrap != null && activatedTrap.DurationTurns <= 1)
                {
                    Console.WriteLine($"  ↳ ({activatedTrap.Id} [{activatedTrap.Stat}] was active this turn — expired after 1 turn)");
                }
            }
            // #700: Momentum state
            if (result.StateAfter.MomentumStreak >= 3)
            {
                int mBonus = result.StateAfter.MomentumStreak >= 5 ? 3 : 2;
                string momExpl = PlaytestFormatter.GetMomentumExplanation(result.StateAfter.MomentumStreak);
                Console.WriteLine($"⚡ Momentum: {result.StateAfter.MomentumStreak}-streak → +{mBonus} bonus ({momExpl})");
            }
            else
            {
                Console.WriteLine($"Momentum: {result.StateAfter.MomentumStreak} win{(result.StateAfter.MomentumStreak != 1 ? "s" : "")}");
            }
            Console.WriteLine("```");

            // #700: Interest range explanation
            {
                InterestState stateBefore = snap.State;
                InterestState stateAfter = result.StateAfter.State;
                int newI = result.StateAfter.Interest;
                string tierRange = GetInterestTierRange(stateAfter);
                string stateDesc = GetInterestStateDescription(stateAfter);
                if (stateBefore != stateAfter)
                {
                    Console.WriteLine($"💡 Interest: {newI} — **{stateAfter}** ({tierRange}: {stateDesc})");
                }
                else if (turn == 1)
                {
                    Console.WriteLine($"💡 Interest: {newI} — {stateAfter} ({tierRange}: {stateDesc})");
                }
            }

            interest = newInterest;
            momentum = result.StateAfter.MomentumStreak;

            // Track stat usage for shadow warnings (#644)
            secondLastStatUsed = lastStatUsed;
            lastStatUsed = chosen.Stat;

            // Update shadow hint tracking state
            // Highest-% detection for this turn
            {
                int chosenMargin = sableStats.GetEffective(chosen.Stat) + p1LevelBonus
                                   - brickStats.GetDefenceDC(chosen.Stat);
                bool isHighest = true;
                for (int oi = 0; oi < turnStart.Options.Length; oi++)
                {
                    int margin = sableStats.GetEffective(turnStart.Options[oi].Stat) + p1LevelBonus
                                 - brickStats.GetDefenceDC(turnStart.Options[oi].Stat);
                    if (margin > chosenMargin) { isHighest = false; break; }
                }
                highestPctHistory.Add(isHighest);
            }
            statsUsedHistory.Add(chosen.Stat);
            if (chosen.Stat == StatType.Charm)
            {
                charmUsageCount++;
                if (charmUsageCount == 3 && !charmMadnessTriggered)
                    charmMadnessTriggered = true;
            }
            if (chosen.Stat == StatType.SelfAwareness)
            {
                saUsageCount++;
                if (saUsageCount == 3 && !saOverthinkingTriggered)
                    saOverthinkingTriggered = true;
            }
            if (chosen.Stat == StatType.Rizz && result.Roll != null && !result.Roll.IsSuccess)
                rizzCumulativeFailureCount++;

            if (result.NarrativeBeat != null) { Console.WriteLine(); Console.WriteLine($"{result.NarrativeBeat}"); }
            if (result.IsGameOver) { finalOutcome = result.Outcome; break; }
        }

        // ── session summary ───────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("---");
        Console.WriteLine();
        Console.WriteLine($"## Session Summary  [{DateTime.UtcNow.ToString("HH:mm:ss")} UTC]");
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

        // ── token audit table ─────────────────────────────────────────────
        {
            var allStats = new List<CallSummaryStat>();
            if (llm is AnthropicLlmAdapter anthropicLlm)
            {
                allStats.AddRange(anthropicLlm.GetCallStats());
            }
            if (agent is LlmPlayerAgent llmAgent)
            {
                allStats.AddRange(llmAgent.GetTokenStats());
            }
            if (allStats.Count > 0)
            {
                // Sort by turn, then adapter calls before player pick
                allStats.Sort((a, b) =>
                {
                    int tc = a.Turn.CompareTo(b.Turn);
                    if (tc != 0) return tc;
                    // llm-player-pick sorts after adapter calls within same turn
                    bool aIsPlayer = a.Type == "llm-player-pick";
                    bool bIsPlayer = b.Type == "llm-player-pick";
                    if (aIsPlayer == bIsPlayer) return 0;
                    return aIsPlayer ? 1 : -1;
                });

                Console.WriteLine();
                Console.WriteLine("## Token Audit");
                Console.WriteLine("| Turn | Call | Input | Output | Cache Read | Cache Write |");
                Console.WriteLine("|------|------|-------|--------|------------|-------------|" );
                foreach (var stat in allStats)
                    Console.WriteLine($"| {stat.Turn} | {stat.Type} | {stat.InputTokens} | {stat.OutputTokens} | {stat.CacheReadInputTokens} | {stat.CacheCreationInputTokens} |");

                int totalInput      = allStats.Sum(s => s.InputTokens);
                int totalOutput     = allStats.Sum(s => s.OutputTokens);
                int totalCacheRead  = allStats.Sum(s => s.CacheReadInputTokens);
                int totalCacheWrite = allStats.Sum(s => s.CacheCreationInputTokens);
                Console.WriteLine($"| **Total** | | **{totalInput}** | **{totalOutput}** | **{totalCacheRead}** | **{totalCacheWrite}** |");
                Console.WriteLine();
            }
        }

        (llm as IDisposable)?.Dispose();

        Console.SetOut(tee._console);
        WritePlaytestLog(buffer.ToString(), player1, player2, playtestDir, sessionNumber);
        return 0;
    }

    internal static string FormatDeliveredAdditions(string intended, string delivered, string marker) {
        // Just return the delivered text as-is. The caller already displays
        // intended vs delivered on separate labelled lines, so inline marker
        // highlighting is unnecessary. The previous suffix-only diff was broken
        // for mid-string word substitutions (#705).
        return delivered;
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

    // #700: Interest tier range helper
    static string GetInterestTierRange(InterestState state) => state switch
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
        ShadowStatType.Despair => "Rizz",
        ShadowStatType.Denial => "Honesty",
        ShadowStatType.Fixation => "Chaos",
        ShadowStatType.Dread => "Wit",
        ShadowStatType.Overthinking => "Self-Awareness",
        _ => shadow.ToString()
    };

    static string GetProviderBaseUrl(string provider)
    {
        switch (provider.ToLowerInvariant())
        {
            case "groq": return "https://api.groq.com/openai";
            case "together": return "https://api.together.xyz/v1";
            case "openrouter": return "https://openrouter.ai/api/v1";
            case "ollama": return "http://localhost:11434/v1";
            default: return "https://api.openai.com";
        }
    }

    static async Task<string> GeneratePsychologicalStakeAsync(string apiKey, string assembledSystemPrompt, string characterName)
    {
        try
        {
            var client = new Pinder.LlmAdapters.Anthropic.AnthropicClient(apiKey);
            var request = new Pinder.LlmAdapters.Anthropic.Dto.MessagesRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 800,
                Temperature = 0.9,
                Messages = new Pinder.LlmAdapters.Anthropic.Dto.Message[]
                {
                    new Pinder.LlmAdapters.Anthropic.Dto.Message
                    {
                        Role = "user",
                        Content = $@"Based on this character's assembled fragments, write a psychological portrait that a novelist would use to write their dialogue. Be creative and specific — fill in gaps based on what the fragments imply, don't summarize them.

TONAL INSTRUCTION: This is a comedy game about the absurdity of online dating. The psychological stakes should be over the top and slightly ridiculous — but the character treats them as completely, genuinely real. The comedy lives in the gap between how absurd the reason sounds stated plainly and how seriously the character feels it. A character who joined because they had a spiritual crisis in an IKEA and now believes their soulmate is someone who understands the existential weight of flat-pack furniture is funnier than a character who is simply 'looking for connection' — and no less emotionally true. Lean into specific absurdity. Make the precipitating events specific and a little unhinged. The character should never know they're funny.

Cover six things, each in 2-3 paragraphs:
1. Why they are on this app right now. Not a general 'looking for connection' — a specific, absurd, over-the-top emotional context. What ridiculous but emotionally real thing happened recently? Name the specific humiliating, strange, or unhinged moment that preceded this. Make it funny but play it straight.
2. What they actually want from a match. Their real underlying need — and it should be slightly deranged in its specificity. What exact bizarre thing would having it feel like? What would they do the morning after?
3. What they are secretly afraid of. The belief about themselves they are protecting — make it specific and a little ridiculous. What absurd thing would it confirm about them if they failed here?
4. What winning this conversation would mean emotionally — not 'getting the date' but what specific, slightly unhinged thing it proves or heals or demonstrates.
5. What losing would mean emotionally — not 'getting unmatched' but the specific catastrophic conclusion they would draw about themselves.
6. Their biographical backstory: 3-5 specific, concrete, slightly unhinged events from the last 2-3 years of their life. These should be specific enough to be revealed in conversation and funny enough to belong in a comedy — not themes but events. A named relationship and the specific absurd way it ended. A job decision and what they did the week after (something strange). A specific moment of realisation in an unlikely location. A place they went alone and what they did there. These are the facts the character can share when the conversation gets real. Write them as vivid, specific narrative fragments. The more specific and slightly absurd, the better.

Write 2-3 paragraphs per point. This is a novelist's character bible for a comedy. Do not use headers or bullet points — write flowing prose. The character is real, their feelings are genuine, their reasons are ridiculous.

CHARACTER PROFILE:
{assembledSystemPrompt.Substring(0, System.Math.Min(4000, assembledSystemPrompt.Length))}"
                    }
                }
            };
            var response = await client.SendMessagesAsync(request).ConfigureAwait(false);
            return response.GetText().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    static void WritePlaytestLog(string content, string p1, string p2, string? dir, int sessionNumber)
    {
        if (dir == null) { Console.Error.WriteLine("Playtest dir not found — set PINDER_PLAYTESTS_PATH or ensure design/playtests/ exists"); return; }
        string slug = $"session-{sessionNumber:D3}-{p1.ToLower()}-vs-{p2.ToLower()}.md";
        File.WriteAllText(Path.Combine(dir, slug), content);
        Console.WriteLine($"\n📝 Written → {dir}/{slug}");
    }
}

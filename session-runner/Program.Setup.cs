using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Data;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.OpenAi;
using Pinder.SessionRunner;
using Pinder.SessionRunner.Snapshot;
using Pinder.Core.Rolls;
using Pinder.SessionSetup;

partial class Program
{
    internal static async Task<GameSetupResult> SetupSessionAsync(string[] args)
    {
        var result = new GameSetupResult();

        result.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(result.ApiKey))
        {
            Console.Error.WriteLine("ANTHROPIC_API_KEY not set");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return result;
        }

        int maxTurnsArg = ParseMaxTurns(args, defaultValue: -1); // -1 = not specified
        string agentType = ParseAgentArg(args);
        bool isDebug = args.Contains("--debug");
        bool disableTraps = args.Contains("--disable-traps");

        // ── Resimulation flags ────────────────────────────────────────────
        result.ResimulateSlug = ParseArg(args, "--resimulate");
        result.IsResimulation = result.ResimulateSlug != null;
        int fromTurnArg = -1; // -1 = auto-detect last saved turn
        {
            string? fromTurnStr = ParseArg(args, "--from-turn");
            if (fromTurnStr != null && int.TryParse(fromTurnStr, out int ft) && ft > 0)
                fromTurnArg = ft;
        }

        // --difficulty <pct>: reduce success chance by N% (e.g. --difficulty 20 = 20% harder).
        // Implemented as a DC bias: dcBias = (int)Math.Round(20.0 * pct / 100.0)
        int difficultyBias = 0;
        string? difficultyArg = ParseArg(args, "--difficulty");
        if (difficultyArg != null && double.TryParse(difficultyArg, out double difficultyPct) && difficultyPct != 0)
        {
            difficultyBias = (int)Math.Round(20.0 * difficultyPct / 100.0);
            Console.Error.WriteLine($"Difficulty bias: {(difficultyPct > 0 ? "+" : "")}{difficultyPct}% → dcBias={difficultyBias}");
        }

        // Parse character name / definition args
        string? playerArg = ParseArg(args, "--player");
        string? dateeArg = ParseArg(args, "--datee");
        string? playerDefArg = ParseArg(args, "--player-def");
        string? dateeDefArg = ParseArg(args, "--datee-def");

        // Must have at least one identifier per side (skipped for --resimulate)
        if (!result.IsResimulation &&
            ((playerArg == null && playerDefArg == null) ||
             (dateeArg == null && dateeDefArg == null)))
        {
            PrintUsage();
            result.ShouldExit = true;
            result.ExitCode = 1;
            return result;
        }

        // Phase 5 of #871: wire prompt yaml infrastructure before any
        // character profile is built. After this point there are no
        // const fallbacks — the catalog must be present.
        PromptWiring.Wire(
            Path.Combine(AppContext.BaseDirectory, "data", "prompts"),
            Console.Error);

        // Load required game-definition.yaml. Hoisted above character
        // loading (#1179) so result.GameDef.ArchetypesEnabled is known before
        // characters are assembled — the assembler gates archetype injection on
        // this flag. The load depends only on AppContext.BaseDirectory +
        // DataFileLocator and has no dependency on character data.
        string? gameDefPath = Pinder.SessionSetup.DataFileLocator.FindDataFile(AppContext.BaseDirectory, Path.Combine("data", "game-definition.yaml"));
        if (gameDefPath == null)
        {
            Console.Error.WriteLine("[ERROR] Required data/game-definition.yaml was not found.");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return result;
        }

        result.GameDef = LoadGameDefinitionOrExit(gameDefPath, result, Console.Error);
        var gameDefinition = result.GameDef;
        if (result.ShouldExit || gameDefinition == null)
            return result;

        DefaultRuleResolver.Instance = gameDefinition;

        if (result.IsResimulation)
        {
            result.PlaytestDir = SessionFileCounter.ResolvePlaytestDirectory(AppContext.BaseDirectory);
            if (result.PlaytestDir == null)
            {
                Console.Error.WriteLine("[ERROR] Cannot find playtest directory for resimulation snapshots. " +
                    "Set PINDER_PLAYTESTS_PATH or ensure design/playtests/ exists.");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }

            ConfigureResimulationSnapshotData(result, args, fromTurnArg);
            if (result.ShouldExit)
                return result;
        }
        else
        {
            // Preload assembler repos (lazy — only if needed)
            IItemRepository? itemRepo = null;
            IAnatomyRepository? anatomyRepo = null;
            ITimingRepository? timingRepo = null;

            try
            {
                bool archetypesEnabled = gameDefinition.ArchetypesEnabled;
                result.Sable = LoadCharacter(playerDefArg, playerArg, ref itemRepo, ref anatomyRepo, ref timingRepo, archetypesEnabled);
                result.Brick = LoadCharacter(dateeDefArg, dateeArg, ref itemRepo, ref anatomyRepo, ref timingRepo, archetypesEnabled);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.Message}");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }
        }

        result.Tee = new TeeWriter(Console.Out, result.Buffer);
        Console.SetOut(result.Tee);

        // ── character definitions (loaded from prompt files) ───────────────
        result.Player1 = result.Sable.DisplayName;
        result.Player2 = result.Brick.DisplayName;
        result.P1Level = result.Sable.Level;
        result.P2Level = result.Brick.Level;
        result.P1LevelBonus = Pinder.Core.Progression.LevelTable.GetBonus(result.P1Level, gameDefinition);
        result.P2LevelBonus = Pinder.Core.Progression.LevelTable.GetBonus(result.P2Level, gameDefinition);
        result.SableStats = result.Sable.Stats;
        result.BrickStats = result.Brick.Stats;

        // ── resolve session number early so header matches filename ──────
        result.PlaytestDir = SessionFileCounter.ResolvePlaytestDirectory(AppContext.BaseDirectory);
        result.SessionNumber = result.PlaytestDir != null ? SessionFileCounter.ClaimNextSessionNumber(result.PlaytestDir) : 1;

        // ── header ────────────────────────────────────────────────────────
        Console.WriteLine($"# Playtest Session {result.SessionNumber:D3} — {result.Player1} × {result.Player2}");
        Console.WriteLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd}");
        string engineLabel = "PinderLlmAdapter + AnthropicTransport → claude-sonnet-4-20250514"; // updated after adapter selection
        // Engine line printed below after adapter is resolved
        string p1Archetype = result.Sable.ActiveArchetype != null ? $" | Archetype: {result.Sable.ActiveArchetype.Name} ({result.Sable.ActiveArchetype.InterferenceLevel})" : "";
        string p2Archetype = result.Brick.ActiveArchetype != null ? $" | Archetype: {result.Brick.ActiveArchetype.Name} ({result.Brick.ActiveArchetype.InterferenceLevel})" : "";
        Console.WriteLine($"**Player:** {result.Player1} (Level {result.P1Level}, +{result.P1LevelBonus} level bonus{p1Archetype}) | **Datee:** {result.Player2} (Level {result.P2Level}, +{result.P2LevelBonus} level bonus, LLM puppet{p2Archetype})");
        Console.WriteLine();

        // Retrieve the deserialized turn snapshot if we are in resimulation
        TurnSnapshot? resimTurnSnapFromSetup = null;
        if (result.IsResimulation)
        {
            var snapOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            string turnSnapPath = Path.Combine(result.PlaytestDir!, $"{result.ResimulateSlug}.turn-{result.FromTurn:D2}.snap.json");
            resimTurnSnapFromSetup = JsonSerializer.Deserialize<TurnSnapshot>(File.ReadAllText(turnSnapPath), snapOpts);
            resimTurnSnapFromSetup = ValidateAndPatchTurnSnapshot(resimTurnSnapFromSetup!, new List<string>());
        }

        // ── Resimulation banner (printed immediately after header) ────────
        if (result.IsResimulation && resimTurnSnapFromSetup != null)
        {
            string resimSessionLabel = result.ResimOriginalSessionNum > 0
                ? $"session-{result.ResimOriginalSessionNum:D3}"
                : result.ResimulateSlug!;
            Console.WriteLine($"**\u23ea RESIMULATED from {resimSessionLabel}, turn {result.FromTurn}**");
            Console.WriteLine($"**Original session:** {result.ResimulateSlug}");
            Console.WriteLine($"**Continuing from:** Interest {resimTurnSnapFromSetup.Interest}/25 | Momentum {resimTurnSnapFromSetup.MomentumStreak} | Turn {result.FromTurn}");
            Console.WriteLine();
        }

        // ── print session details ─────────────────────────────────────────
        PrintSetupDetails(result);

        // ── LLM + session setup ───────────────────────────────────────────
        // Resolve maxTurns: CLI arg overrides YAML, YAML overrides default (30)
        result.MaxTurns = maxTurnsArg > 0 ? maxTurnsArg : gameDefinition.MaxTurns;

        StatDeliveryInstructions? statDeliveryInstructions;
        ConfigureLlmAdapterAndEngine(result, args, ref engineLabel, out statDeliveryInstructions);
        if (result.ShouldExit)
            return result;

        result.DebugFile = null;
        if (isDebug && result.PlaytestDir != null)
        {
            result.DebugFile = Path.Combine(result.PlaytestDir, $"session-{result.SessionNumber:D3}-debug.md");
        }

        Console.WriteLine($"**Engine:** `pinder-core GameSession` + `{engineLabel}`");

        // Load real trap definitions. Trap data is a core gameplay contract:
        // a missing or corrupt traps.json fails setup rather than silently
        // running with all trap mechanics disabled. The only sanctioned
        // no-traps mode is the explicit --disable-traps flag, which is
        // called out clearly in the session header below.
        result.TrapsDisabled = disableTraps;
        if (disableTraps)
        {
            Console.WriteLine("**Traps:** DISABLED (--disable-traps — deliberate no-traps mode)");
        }
        try
        {
            result.TrapRegistry = TrapRegistryLoader.Resolve(disableTraps, AppContext.BaseDirectory, Console.Error);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to load traps.json: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine("[ERROR] Pass --disable-traps to intentionally run without trap data.");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return result;
        }

        // Issue #474: load the i18n catalog so per-turn snapshots can
        // embed deterministic interpretation strings on each event.
        result.SnapshotI18nCatalog = null;
        try
        {
            string? repoRoot = Pinder.SessionSetup.DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);
            if (repoRoot != null)
            {
                string i18nDir = Path.Combine(repoRoot, "data", "i18n");
                if (Directory.Exists(i18nDir))
                {
                    result.SnapshotI18nCatalog = Pinder.LlmAdapters.I18nCatalog.LoadFromDirectory(i18nDir, "en");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Failed to load i18n catalog for event interpretations: {ex.Message}");
            result.SnapshotI18nCatalog = null;
        }

        // Shadow tracking — wrap player's StatBlock so GameSession can track shadow growth
        result.SableShadows = new SessionShadowTracker(result.SableStats);

        // Create real wall clock with time-of-day horniness modifiers from game definition
        var now = DateTimeOffset.Now;
        Pinder.Core.Conversation.GameClock clock;
        var mods = gameDefinition.HorninessTimeModifiers;
        var horninessModifiers = new Pinder.Core.Conversation.HorninessModifiers(
            mods.Morning, mods.Afternoon, mods.Evening, mods.Overnight);
        clock = new Pinder.Core.Conversation.GameClock(now, horninessModifiers);
        result.Clock = clock;

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

        int yamlDcBias = gameDefinition.GlobalDcBias;
        int yamlShadowDcBias = gameDefinition.ShadowDcBias;
        int yamlHorninessDcBias = gameDefinition.HorninessDcBias;
        int totalDcBias = difficultyBias + yamlDcBias;
        Pinder.LlmAdapters.ConsequenceCatalog? consequenceCatalog = null;
        if (result.SnapshotI18nCatalog != null)
            consequenceCatalog = new Pinder.LlmAdapters.ConsequenceCatalog(result.SnapshotI18nCatalog);

        var config = new GameSessionConfig(
            clock: clock,
            playerShadows: result.SableShadows,
            globalDcBias: totalDcBias,
            shadowDcBias: yamlShadowDcBias,
            horninessDcBias: yamlHorninessDcBias,
            statDeliveryInstructions: statDeliveryInstructions,
            consequenceCatalog: consequenceCatalog,
            archetypesEnabled: gameDefinition.ArchetypesEnabled,
            rules: gameDefinition);
        int? diceSeed = null;
        { if (ParseArg(args, "--seed") is string s2 && int.TryParse(s2, out int s3)) diceSeed = s3; }
        result.Session = new GameSession(result.Sable, result.Brick, result.Llm, new SystemRandomDiceRoller(diceSeed), result.TrapRegistry, config);

        // ── Resimulation: restore session state from snapshot ───────────
        if (result.IsResimulation && resimTurnSnapFromSetup != null)
        {
            var resimData = BuildResimulateData(resimTurnSnapFromSetup);
            result.Session.RestoreState(resimData, result.TrapRegistry);
        }

        // Display session horniness in header (#709, #750)
        {
            int sh = result.Session.SessionHorniness;
            int horninessDC = RollEngine.ApplyDcBias(sh, yamlHorninessDcBias);
            int hRoll = result.Session.HorninessRoll;
            int hMod = result.Session.HorninessTimeModifier;
            string timeBand = clock.GetTimeOfDay().ToString().ToLower();
            string hModDisplay = hMod >= 0 ? $"+{hMod}" : hMod.ToString();
            Console.WriteLine($"🌶️ Session Horniness: {sh}  (1d10[{hRoll}] {timeBand} {hModDisplay} = {sh} → DC {horninessDC} per turn)");
            Console.WriteLine($"   → Fumble/Misfire/TropeTrap/Catastrophe tier on miss (same as roll failure tiers)");
        }
        Console.WriteLine();

        result.PlayerAgentModelSpec = ParsePlayerAgentModelArg(args);

        // Player agent for decision-making — configurable via --agent arg or PLAYER_AGENT env var
        if (agentType.Equals("llm", StringComparison.OrdinalIgnoreCase))
        {
            var agentOptions = new AnthropicOptions
            {
                ApiKey = result.ApiKey,
                Model = result.PlayerAgentModelSpec
            };
            result.Agent = new LlmPlayerAgent(agentOptions, new ScoringPlayerAgent(),
                playerName: result.Sable.DisplayName, dateeName: result.Brick.DisplayName,
                ruleResolver: gameDefinition);
        }
        else if (agentType.Equals("human", StringComparison.OrdinalIgnoreCase))
        {
            result.Agent = new HumanPlayerAgent();
        }
        else
        {
            result.Agent = new ScoringPlayerAgent();
        }

        result.Interest = result.IsResimulation && resimTurnSnapFromSetup != null ? resimTurnSnapFromSetup.Interest : 10;

        await GenerateStakesAndFreeze(result);

        result.Momentum = result.IsResimulation && resimTurnSnapFromSetup != null ? resimTurnSnapFromSetup.MomentumStreak : 0;
        Console.WriteLine("## Session State");
        Console.WriteLine();
        Console.WriteLine($"```");
        Console.WriteLine($"Interest: {InterestBar(result.Interest)}  {result.Interest}/25");
        if (result.IsResimulation && resimTurnSnapFromSetup != null)
        {
            string trapStr = resimTurnSnapFromSetup.ActiveTraps.Count > 0
                ? string.Join(", ", resimTurnSnapFromSetup.ActiveTraps.Select(t => $"{t.Id} [{t.Stat}]"))
                : "none";
            Console.WriteLine($"Active Traps: {trapStr}");
            Console.WriteLine($"Momentum: {result.Momentum}");
        }
        else
        {
            Console.WriteLine($"Active Traps: none");
            Console.WriteLine($"Momentum: —");
        }
        Console.WriteLine($"```");
        Console.WriteLine();
        Console.WriteLine("---");

        result.SessionSlug = result.PlaytestDir != null
            ? $"session-{result.SessionNumber:D3}-{result.Player1.ToLower()}-vs-{result.Player2.ToLower()}"
            : $"session-{result.SessionNumber:D3}-unknown";

        // ── Write initial snapshot before turn 1 ───────────────────
        if (!result.IsResimulation && result.PlaytestDir != null)
        {
            var initialSnap = BuildInitialSnapshot(
                result.Sable, result.Brick, result.P1LevelBonus, result.P2LevelBonus,
                result.Session, result.Interest, result.MaxTurns, result.ModelSpec,
                gameDefinition.GlobalDcBias, gameDefinition.MaxDialogueOptions);
            string initialSnapPath = Path.Combine(result.PlaytestDir, $"{result.SessionSlug}.initial.snap.json");
            File.WriteAllText(initialSnapPath, JsonSerializer.Serialize(initialSnap, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine($"📸 Initial snapshot → {initialSnapPath}");
        }

        return result;
    }
}

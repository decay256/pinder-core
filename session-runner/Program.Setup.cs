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
        string? opponentArg = ParseArg(args, "--opponent");
        string? playerDefArg = ParseArg(args, "--player-def");
        string? opponentDefArg = ParseArg(args, "--opponent-def");

        // Must have at least one identifier per side (skipped for --resimulate)
        if (!result.IsResimulation &&
            ((playerArg == null && playerDefArg == null) ||
             (opponentArg == null && opponentDefArg == null)))
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

            var snapOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            string initialSnapPath = Path.Combine(result.PlaytestDir, $"{result.ResimulateSlug}.initial.snap.json");
            if (!File.Exists(initialSnapPath))
            {
                Console.Error.WriteLine($"[ERROR] Initial snapshot not found: {initialSnapPath}");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }
            var resimInitialSnap = JsonSerializer.Deserialize<InitialSessionSnapshot>(
                File.ReadAllText(initialSnapPath), snapOpts);
            if (resimInitialSnap == null)
            {
                Console.Error.WriteLine($"[ERROR] Failed to deserialize initial snapshot: {initialSnapPath}");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }

            // Determine which turn to resume from
            result.FromTurn = fromTurnArg >= 1 ? fromTurnArg : FindLastTurnSnapshot(result.PlaytestDir, result.ResimulateSlug!);
            if (result.FromTurn <= 0)
            {
                Console.Error.WriteLine($"[ERROR] No turn snapshots found for slug: {result.ResimulateSlug}");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }

            string turnSnapPath = Path.Combine(result.PlaytestDir, $"{result.ResimulateSlug}.turn-{result.FromTurn:D2}.snap.json");
            if (!File.Exists(turnSnapPath))
            {
                Console.Error.WriteLine($"[ERROR] Turn snapshot not found: {turnSnapPath}");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }
            var resimTurnSnap = JsonSerializer.Deserialize<TurnSnapshot>(
                File.ReadAllText(turnSnapPath), snapOpts);
            if (resimTurnSnap == null)
            {
                Console.Error.WriteLine($"[ERROR] Failed to deserialize turn snapshot: {turnSnapPath}");
                result.ShouldExit = true;
                result.ExitCode = 1;
                return result;
            }

            // Validate + log assumptions for missing fields
            resimTurnSnap = ValidateAndPatchTurnSnapshot(resimTurnSnap, result.AssumptionLog);

            // Reconstruct CharacterProfile objects from frozen snapshot data
            result.Sable = BuildProfileFromSnapshot(resimInitialSnap.Player);
            result.Brick = BuildProfileFromSnapshot(resimInitialSnap.Opponent);

            // Restore psychological stakes from snapshot (no API calls needed)
            result.Sable.PsychologicalStake = resimInitialSnap.PlayerPsychologicalStake;
            result.Brick.PsychologicalStake = resimInitialSnap.OpponentPsychologicalStake;

            // Parse original session number from slug (format: session-NNN-...)
            result.ResimOriginalSessionNum = ParseSessionNumberFromSlug(result.ResimulateSlug!);

            Console.Error.WriteLine($"⏪ Resimulation: {result.ResimulateSlug} from turn {result.FromTurn}");
        }
        else
        {
            // Preload assembler repos (lazy — only if needed)
            IItemRepository? itemRepo = null;
            IAnatomyRepository? anatomyRepo = null;

            try
            {
                result.Sable = LoadCharacter(playerDefArg, playerArg, ref itemRepo, ref anatomyRepo);
                result.Brick = LoadCharacter(opponentDefArg, opponentArg, ref itemRepo, ref anatomyRepo);
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
        result.P1LevelBonus = Pinder.Core.Progression.LevelTable.GetBonus(result.P1Level);
        result.P2LevelBonus = Pinder.Core.Progression.LevelTable.GetBonus(result.P2Level);
        result.SableStats = result.Sable.Stats;
        result.BrickStats = result.Brick.Stats;

        // ── resolve session number early so header matches filename ──────
        result.PlaytestDir = SessionFileCounter.ResolvePlaytestDirectory(AppContext.BaseDirectory);
        result.SessionNumber = result.PlaytestDir != null ? SessionFileCounter.ClaimNextSessionNumber(result.PlaytestDir) : 1;

        // ── header ────────────────────────────────────────────────────────
        Console.WriteLine($"# Playtest Session {result.SessionNumber:D3} — {result.Player1} × {result.Player2}");
        Console.WriteLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd}");
        string engineLabel = "AnthropicLlmAdapter → claude-sonnet-4-20250514"; // updated after adapter selection
        // Engine line printed below after adapter is resolved
        string p1Archetype = result.Sable.ActiveArchetype != null ? $" | Archetype: {result.Sable.ActiveArchetype.Name} ({result.Sable.ActiveArchetype.InterferenceLevel})" : "";
        string p2Archetype = result.Brick.ActiveArchetype != null ? $" | Archetype: {result.Brick.ActiveArchetype.Name} ({result.Brick.ActiveArchetype.InterferenceLevel})" : "";
        Console.WriteLine($"**Player:** {result.Player1} (Level {result.P1Level}, +{result.P1LevelBonus} level bonus{p1Archetype}) | **Opponent:** {result.Player2} (Level {result.P2Level}, +{result.P2LevelBonus} level bonus, LLM puppet{p2Archetype})");
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
        // Load game-definition.yaml if present
        string? gameDefPath = DataFileLocator.FindDataFile(AppContext.BaseDirectory, Path.Combine("data", "game-definition.yaml"));
        result.GameDef = null;
        if (gameDefPath != null)
        {
            try
            {
                result.GameDef = GameDefinition.LoadFrom(File.ReadAllText(gameDefPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Failed to load game-definition.yaml: {ex.Message}");
            }
        }

        // Resolve maxTurns: CLI arg overrides YAML, YAML overrides default (30)
        result.MaxTurns = maxTurnsArg > 0 ? maxTurnsArg : (result.GameDef?.MaxTurns ?? 30);

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

        result.DebugFile = null;
        if (isDebug && result.PlaytestDir != null)
        {
            result.DebugFile = Path.Combine(result.PlaytestDir, $"session-{result.SessionNumber:D3}-debug.md");
        }

        result.ModelSpec = ParseArg(args, "--model") ?? "";
        result.OverlayModel = ParseArg(args, "--overlay-model");

        var adapterOptions = new PinderLlmAdapterOptions
        {
            GameDefinition = result.GameDef,
            StatDeliveryInstructions = statDeliveryInstructions,
            MaxTokens = 1024,
            Temperature = 0.9,
        };

        if (result.ModelSpec.StartsWith("groq/") || result.ModelSpec.StartsWith("together/") ||
            result.ModelSpec.StartsWith("openrouter/") || result.ModelSpec.StartsWith("ollama/"))
        {
            string[] providerParts = result.ModelSpec.Split(new[] { '/' }, 2);
            string provider = providerParts[0];
            string model = providerParts.Length > 1 ? providerParts[1] : result.ModelSpec;
            string baseUrl = GetProviderBaseUrl(provider);
            string envKey = provider.ToUpperInvariant() + "_API_KEY";
            string openAiKey = Environment.GetEnvironmentVariable(envKey) ?? result.ApiKey;
            var transport = new OpenAiTransport(openAiKey, baseUrl, model);
            result.Llm = new PinderLlmAdapter(transport, adapterOptions);
            engineLabel = $"PinderLlmAdapter + OpenAiTransport ({provider}) → {model}";
        }
        else
        {
            string? groqApiKey = !string.IsNullOrWhiteSpace(result.OverlayModel)
                ? Environment.GetEnvironmentVariable("GROQ_API_KEY")
                : null;
            if (!string.IsNullOrWhiteSpace(result.OverlayModel))
            {
                Console.Error.WriteLine($"Overlay model: {result.OverlayModel} (Groq)");
                if (string.IsNullOrWhiteSpace(groqApiKey))
                    Console.Error.WriteLine("[WARN] GROQ_API_KEY not set — overlay calls will fall back to primary transport");
                adapterOptions.OverlayGroqModel = result.OverlayModel;
                adapterOptions.OverlayGroqApiKey = groqApiKey;
            }
            string anthropicModel = "claude-sonnet-4-20250514";
            var transport = new AnthropicTransport(result.ApiKey, anthropicModel);
            result.Llm = new PinderLlmAdapter(transport, adapterOptions);
            engineLabel = string.IsNullOrWhiteSpace(result.OverlayModel)
                ? $"PinderLlmAdapter + AnthropicTransport → {anthropicModel}"
                : $"PinderLlmAdapter + AnthropicTransport → {anthropicModel} (overlay: {result.OverlayModel} via Groq)";
        }

        Console.WriteLine($"**Engine:** `pinder-core GameSession` + `{engineLabel}`");

        // Load real trap definitions — fallback to NullTrapRegistry if file missing/corrupt
        result.TrapRegistry = TrapRegistryLoader.Load(AppContext.BaseDirectory, Console.Error);

        // Issue #474: load the i18n catalog so per-turn snapshots can
        // embed deterministic interpretation strings on each event.
        result.SnapshotI18nCatalog = null;
        try
        {
            string? repoRoot = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);
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
        if (result.GameDef != null)
        {
            var mods = result.GameDef.HorninessTimeModifiers;
            var horninessModifiers = new Pinder.Core.Conversation.HorninessModifiers(
                mods.Morning, mods.Afternoon, mods.Evening, mods.Overnight);
            clock = new Pinder.Core.Conversation.GameClock(now, horninessModifiers);
        }
        else
        {
            var zeroModifiers = new Pinder.Core.Conversation.HorninessModifiers(0, 0, 0, 0);
            clock = new Pinder.Core.Conversation.GameClock(now, zeroModifiers);
        }
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

        int yamlDcBias = result.GameDef?.GlobalDcBias ?? 0;
        int totalDcBias = difficultyBias + yamlDcBias;
        Pinder.LlmAdapters.ConsequenceCatalog? consequenceCatalog = null;
        if (result.SnapshotI18nCatalog != null)
            consequenceCatalog = new Pinder.LlmAdapters.ConsequenceCatalog(result.SnapshotI18nCatalog);

        var config = new GameSessionConfig(clock: clock, playerShadows: result.SableShadows, globalDcBias: totalDcBias, statDeliveryInstructions: statDeliveryInstructions, consequenceCatalog: consequenceCatalog);
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
            int horninessDC = 20 - sh;
            int hRoll = result.Session.HorninessRoll;
            int hMod = result.Session.HorninessTimeModifier;
            string timeBand = clock.GetTimeOfDay().ToString().ToLower();
            string hModDisplay = hMod >= 0 ? $"+{hMod}" : hMod.ToString();
            Console.WriteLine($"🌶️ Session Horniness: {sh}  (1d10[{hRoll}] {timeBand} {hModDisplay} = {sh} → DC {horninessDC} per turn)");
            Console.WriteLine($"   → Fumble/Misfire/TropeTrap/Catastrophe tier on miss (same as roll failure tiers)");
        }
        Console.WriteLine();

        // Player agent for decision-making — configurable via --agent arg or PLAYER_AGENT env var
        if (agentType.Equals("llm", StringComparison.OrdinalIgnoreCase))
        {
            var agentOptions = new AnthropicOptions
            {
                ApiKey = result.ApiKey,
                Model = Environment.GetEnvironmentVariable("PLAYER_AGENT_MODEL") ?? "claude-sonnet-4-20250514"
            };
            result.Agent = new LlmPlayerAgent(agentOptions, new ScoringPlayerAgent(),
                playerName: result.Sable.DisplayName, opponentName: result.Brick.DisplayName);
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

        if (!result.IsResimulation)
        {
            string setupModel = Environment.GetEnvironmentVariable("PLAYER_AGENT_MODEL") ?? "claude-sonnet-4-20250514";
            using var setupRawTransport = new Pinder.LlmAdapters.Anthropic.AnthropicTransport(result.ApiKey, setupModel);
            var setupTransport = new Pinder.LlmAdapters.ThinkingStrippingLlmTransport(setupRawTransport);

            // ── Psychological Stakes ──────────────────────────────────
            Console.Error.WriteLine("Generating psychological stakes...");
            var stakeGenerator = new Pinder.SessionSetup.LlmStakeGenerator(setupTransport);
            string p1Stake = await stakeGenerator.GenerateAsync(result.Player1, result.Sable.AssembledSystemPrompt).ConfigureAwait(false);
            string p2Stake = await stakeGenerator.GenerateAsync(result.Player2, result.Brick.AssembledSystemPrompt).ConfigureAwait(false);
            result.Sable.PsychologicalStake = p1Stake;
            result.Brick.PsychologicalStake = p2Stake;

            if (!string.IsNullOrWhiteSpace(result.Sable.PsychologicalStake))
            {
                Console.WriteLine();
                Console.WriteLine($"### {result.Player1} — Psychological Stake");
                Console.WriteLine();
                Console.WriteLine(result.Sable.PsychologicalStake);
                Console.WriteLine();
            }
            if (!string.IsNullOrWhiteSpace(result.Brick.PsychologicalStake))
            {
                Console.WriteLine();
                Console.WriteLine($"### {result.Player2} — Psychological Stake");
                Console.WriteLine();
                Console.WriteLine(result.Brick.PsychologicalStake);
                Console.WriteLine();
            }

            result.Sable.FreezeBasePrompt();
            result.Brick.FreezeBasePrompt();

            if (!string.IsNullOrWhiteSpace(result.Sable.PsychologicalStake))
                result.Sable.AppendToSystemPrompt("\n\n== PSYCHOLOGICAL STAKE ==\n\n" + result.Sable.PsychologicalStake);
            if (!string.IsNullOrWhiteSpace(result.Brick.PsychologicalStake))
                result.Brick.AppendToSystemPrompt("\n\n== PSYCHOLOGICAL STAKE ==\n\n" + result.Brick.PsychologicalStake);
        }
        else
        {
            result.Sable.FreezeBasePrompt();
            result.Brick.FreezeBasePrompt();
        }

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
                result.GameDef?.GlobalDcBias ?? 0, result.GameDef?.MaxDialogueOptions ?? 3);
            string initialSnapPath = Path.Combine(result.PlaytestDir, $"{result.SessionSlug}.initial.snap.json");
            File.WriteAllText(initialSnapPath, JsonSerializer.Serialize(initialSnap, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine($"📸 Initial snapshot → {initialSnapPath}");
        }

        return result;
    }
}

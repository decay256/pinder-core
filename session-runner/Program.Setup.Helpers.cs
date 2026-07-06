using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static void ConfigureResimulationSnapshotData(GameSetupResult result, string[] args, int fromTurnArg)
    {
        var snapOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        string initialSnapPath = Path.Combine(result.PlaytestDir!, $"{result.ResimulateSlug}.initial.snap.json");
        if (!File.Exists(initialSnapPath))
        {
            Console.Error.WriteLine($"[ERROR] Initial snapshot not found: {initialSnapPath}");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return;
        }
        var resimInitialSnap = JsonSerializer.Deserialize<InitialSessionSnapshot>(
            File.ReadAllText(initialSnapPath), snapOpts);
        if (resimInitialSnap == null)
        {
            Console.Error.WriteLine($"[ERROR] Failed to deserialize initial snapshot: {initialSnapPath}");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return;
        }

        // Determine which turn to resume from
        result.FromTurn = fromTurnArg >= 1 ? fromTurnArg : FindLastTurnSnapshot(result.PlaytestDir!, result.ResimulateSlug!);
        if (result.FromTurn <= 0)
        {
            Console.Error.WriteLine($"[ERROR] No turn snapshots found for slug: {result.ResimulateSlug}");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return;
        }

        string turnSnapPath = Path.Combine(result.PlaytestDir!, $"{result.ResimulateSlug}.turn-{result.FromTurn:D2}.snap.json");
        if (!File.Exists(turnSnapPath))
        {
            Console.Error.WriteLine($"[ERROR] Turn snapshot not found: {turnSnapPath}");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return;
        }
        var resimTurnSnap = JsonSerializer.Deserialize<TurnSnapshot>(
            File.ReadAllText(turnSnapPath), snapOpts);
        if (resimTurnSnap == null)
        {
            Console.Error.WriteLine($"[ERROR] Failed to deserialize turn snapshot: {turnSnapPath}");
            result.ShouldExit = true;
            result.ExitCode = 1;
            return;
        }

        // Validate + log assumptions for missing fields
        resimTurnSnap = ValidateAndPatchTurnSnapshot(resimTurnSnap, result.AssumptionLog);

        // Reconstruct CharacterProfile objects from frozen snapshot data
        result.Sable = BuildProfileFromSnapshot(resimInitialSnap.Player);
        result.Brick = BuildProfileFromSnapshot(resimInitialSnap.Datee);

        // Restore psychological stakes from snapshot (no API calls needed)
        result.Sable.PsychologicalStake = resimInitialSnap.PlayerPsychologicalStake;
        result.Brick.PsychologicalStake = resimInitialSnap.DateePsychologicalStake;

        // Parse original session number from slug (format: session-NNN-...)
        result.ResimOriginalSessionNum = ParseSessionNumberFromSlug(result.ResimulateSlug!);

        Console.Error.WriteLine($"⏪ Resimulation: {result.ResimulateSlug} from turn {result.FromTurn}");
    }

    private static void ConfigureLlmAdapterAndEngine(GameSetupResult result, string[] args, ref string engineLabel, out StatDeliveryInstructions? statDeliveryInstructions)
    {
        result.ModelSpec = ParseArg(args, "--model") ?? "";
        result.OverlayModel = ParseArg(args, "--overlay-model");

        // Load delivery-instructions.yaml if present
        string? deliveryInstructionsPath = DataFileLocator.FindDataFile(AppContext.BaseDirectory, Path.Combine("data", "delivery-instructions.yaml"));
        statDeliveryInstructions = null;
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
            if (!string.IsNullOrWhiteSpace(result.OverlayModel))
            {
                Console.Error.WriteLine($"[WARN] Overlay model '{result.OverlayModel}' requested but overlay routing via option fields was removed (#1293); overlay calls will use the primary transport. Wire a dedicated overlay ILlmTransport to route overlays.");
            }
            string anthropicModel = string.IsNullOrWhiteSpace(result.ModelSpec)
                ? AnthropicModelIds.DefaultModel
                : result.ModelSpec;
            var transport = new AnthropicTransport(result.ApiKey, anthropicModel);
            result.Llm = new PinderLlmAdapter(transport, adapterOptions);
            engineLabel = string.IsNullOrWhiteSpace(result.OverlayModel)
                ? $"PinderLlmAdapter + AnthropicTransport → {anthropicModel}"
                : $"PinderLlmAdapter + AnthropicTransport → {anthropicModel} (overlay: unrouted)";
        }
    }

    private static async Task GenerateStakesAndFreeze(GameSetupResult result)
    {
        if (!result.IsResimulation)
        {
            string setupModel = Environment.GetEnvironmentVariable("PLAYER_AGENT_MODEL") ?? AnthropicModelIds.DefaultModel;
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
    }
}

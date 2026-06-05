using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Rules-free narrative testbed (issue #843).
    ///
    /// A turn here is ONLY: build the system prompt with the real
    /// SessionSystemPromptBuilder (arc slot populated) -&gt;
    /// transport.SendAsync(...) -&gt; record raw output. No Roll/Shadow/Horniness/
    /// Weakness/GameSessionRules/interest-delta/misfire paths are touched. The
    /// project does not even reference Pinder.Rules.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            HarnessOptions opts;
            try { opts = HarnessOptions.Parse(args); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] " + ex.Message);
                HarnessOptions.PrintUsage();
                return 2;
            }

            string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("[ERROR] ANTHROPIC_API_KEY not set.");
                return 1;
            }

            // ── Load real character (production path) ─────────────────────
            LoadedCharacter character;
            try { character = HarnessCharacterLoader.Load(opts.CharacterSlug); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] Character load failed: " + ex.Message);
                return 1;
            }

            GameDefinition baseDef = HarnessCharacterLoader.LoadBaseGameDefinition();

            // ── Build the reusable confession menu ────────────────────────
            var menu = ConfessionMenu.Build(
                character.Name, character.PsychologicalStake, character.BackgroundStory);

            if (menu.Entries.Count == 0)
            {
                Console.Error.WriteLine(
                    $"[ERROR] Character '{opts.CharacterSlug}' has no parseable psychological_stake confessions. "
                    + "The ingestion harness needs a populated stake. Pick a character with a 15-line stake (e.g. brick, velvet).");
                return 1;
            }

            // ── Select arc strategy (strategy interface, not hardcoded) ───
            IArcStrategy strategy = opts.ArcShape == "romcom"
                ? new RomComArcStrategy()
                : new IngestionArcStrategy(menu);

            // ── Real transport (dashed model id; ctor maps to API id) ─────
            using var transport = new AnthropicTransport(apiKey, "claude-opus-4-8");

            // ── Scripted pursuer lines (optional) ─────────────────────────
            List<string>? scriptedLines = null;
            if (opts.PlayerScriptPath != null)
            {
                if (!File.Exists(opts.PlayerScriptPath))
                {
                    Console.Error.WriteLine("[ERROR] --player-script file not found: " + opts.PlayerScriptPath);
                    return 1;
                }
                scriptedLines = File.ReadAllLines(opts.PlayerScriptPath)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .ToList();
            }

            var runner = new HarnessRunner(transport, character, menu, baseDef, strategy, opts, scriptedLines);
            string transcript = await runner.RunAsync();

            // ── Write out ─────────────────────────────────────────────────
            File.WriteAllText(opts.OutPath, transcript);
            Console.Error.WriteLine($"[ok] Transcript written → {opts.OutPath}");
            return 0;
        }
    }
}

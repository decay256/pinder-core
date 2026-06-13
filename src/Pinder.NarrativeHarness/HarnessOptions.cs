using System;
using System.Linq;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>Parsed CLI options for the narrative harness.</summary>
    public sealed class HarnessOptions
    {
        public string CharacterSlug { get; private set; } = "brick";
        public string? PursuerCharacterSlug { get; private set; }     // --pursuer-character (opt; real 2nd char)
        public int Turns { get; private set; } = 14;                 // resolved from <n|range>
        public string? PlayerScriptPath { get; private set; }
        public int? Seed { get; private set; }
        public string OutPath { get; private set; } = "narrative-harness-out.md";

        public static void PrintUsage()
        {
            Console.Error.WriteLine(
@"NarrativeHarness — rules-free narrative testbed (#843)

Usage:
  --character <slug>        Datee character to load (e.g. brick, velvet). Default: brick
  --pursuer-character <slug> OPTIONAL second real character driven as the pursuer
                            through the SAME production prompt path (BuildDatee).
                            When set, the pursuer stays in character for the whole
                            transcript (REACTIVE — no arc injection). When omitted,
                            the pursuer falls back to --player-script (if given) or
                            the generic lightweight LLM persona.
  --turns <n|range>         Turn count, e.g. 14 or 10-20 (range picks the high end,
                            seeded if --seed given). Default: 14
  --player-script <file>    Scripted pursuer lines (one per line; # = comment).
                            Alternative to the LLM pursuer.
  --seed <int>              Seed for range resolution / reproducibility.
  --out <file>              Output markdown path. Default: narrative-harness-out.md");
        }

        public static HarnessOptions Parse(string[] args)
        {
            var o = new HarnessOptions();
            string? Get(string name)
            {
                int i = Array.IndexOf(args, name);
                if (i >= 0 && i + 1 < args.Length) return args[i + 1];
                return null;
            }

            if (Get("--character") is string c) o.CharacterSlug = c;
            if (Get("--pursuer-character") is string pc) o.PursuerCharacterSlug = pc;

            if (Get("--seed") is string seedStr && int.TryParse(seedStr, out int seed))
                o.Seed = seed;

            if (Get("--turns") is string turnsStr)
                o.Turns = ResolveTurns(turnsStr, o.Seed);

            if (Get("--player-script") is string ps) o.PlayerScriptPath = ps;
            if (Get("--out") is string outp) o.OutPath = outp;

            if (o.Turns < 1) throw new ArgumentException("--turns must be >= 1.");
            return o;
        }

        /// <summary>
        /// Resolve "&lt;n&gt;" to n, or "lo-hi" to a value in [lo,hi] (seeded if a seed
        /// is present, else the high end so a 10-20 request runs a full arc).
        /// </summary>
        public static int ResolveTurns(string spec, int? seed)
        {
            spec = spec.Trim();
            int dash = spec.IndexOf('-');
            if (dash > 0)
            {
                string loS = spec.Substring(0, dash).Trim();
                string hiS = spec.Substring(dash + 1).Trim();
                if (int.TryParse(loS, out int lo) && int.TryParse(hiS, out int hi) && lo <= hi)
                {
                    if (seed.HasValue)
                        return new Random(seed.Value).Next(lo, hi + 1);
                    return hi;
                }
                throw new ArgumentException($"Invalid --turns range: '{spec}'. Use e.g. 10-20.");
            }
            if (int.TryParse(spec, out int n)) return n;
            throw new ArgumentException($"Invalid --turns: '{spec}'. Use a number or a range like 10-20.");
        }
    }
}

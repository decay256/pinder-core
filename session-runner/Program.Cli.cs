using System;
using System.Linq;

partial class Program
{
    internal static string ExtractSystemPrompt(string md)
    {
        int start = md.IndexOf("```\n", StringComparison.Ordinal) + 4;
        int end   = md.LastIndexOf("\n```", StringComparison.Ordinal);
        if (start < 4 || end < 0) return md;
        return md.Substring(start, end - start).Trim();
    }

    internal static int ParseMaxTurns(string[] args, int defaultValue = 30)
    {
        string? val = ParseArg(args, "--turns");
        if (val != null && int.TryParse(val, out int t) && t > 0)
            return t;
        return defaultValue;
    }

    internal static string ParseAgentArg(string[] args)
    {
        string? agent = ParseArg(args, "--agent");
        if (agent != null) return agent;
        return Environment.GetEnvironmentVariable("PLAYER_AGENT") ?? "score";
    }

    internal static string? ParseArg(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx < args.Length - 1)
            return args[idx + 1];
        return null;
    }

    internal static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  session-runner --player <name> --opponent <name> [options]");
        Console.Error.WriteLine("  session-runner --player-def <path> --opponent-def <path> [options]");
        Console.Error.WriteLine("  session-runner --resimulate <slug> [--from-turn <N>] [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --turns <N>        Override maximum session turns (default: 30)");
        Console.Error.WriteLine("  --agent <type>      Select decision agent: score, llm, human (default: score)");
        Console.Error.WriteLine("  --model <spec>      LLM adapter target (e.g. groq/llama3-8b, ollama/mistral, defaults to Claude 3.5 Sonnet)");
        Console.Error.WriteLine("  --overlay-model <m> Run an overlay/refinement model on top of primary adapter output (via Groq)");
        Console.Error.WriteLine("  --difficulty <pct> Reduce check success probability by N% (e.g. --difficulty 15 = 15% harder)");
        Console.Error.WriteLine("  --seed <int>       Seed value for deterministic dice checks");
        Console.Error.WriteLine("  --debug            Write an accompanying session-XXX-debug.md log containing full raw API transcripts");
        Console.Error.WriteLine();
        string available = ListAvailableCharacters();
        Console.Error.WriteLine($"Available characters: {available}");
    }
}

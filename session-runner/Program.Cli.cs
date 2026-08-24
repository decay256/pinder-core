using System;
using System.Linq;
using Pinder.LlmAdapters.Anthropic;

partial class Program
{
    internal const string SessionSetupModelEnvVar = "SESSION_SETUP_MODEL";
    internal const string PlayerAgentModelEnvVar = "PLAYER_AGENT_MODEL";
    internal const string ModelMaxOutputTokensEnvVar = "PINDER_MODEL_MAX_OUTPUT_TOKENS";
    internal const string SetupModelMaxOutputTokensEnvVar = "PINDER_SETUP_MODEL_MAX_OUTPUT_TOKENS";

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

    internal static string ParseGameModelArg(string[] args)
    {
        return ParseArg(args, "--model") ?? AnthropicModelIds.DefaultModel;
    }

    internal static string ParseSetupModelArg(string[] args, string gameModel)
    {
        return ParseArg(args, "--setup-model")
            ?? Environment.GetEnvironmentVariable(SessionSetupModelEnvVar)
            ?? gameModel;
    }

    internal static string ParsePlayerAgentModelArg(string[] args)
    {
        return ParseArg(args, "--player-agent-model")
            ?? Environment.GetEnvironmentVariable(PlayerAgentModelEnvVar)
            ?? AnthropicModelIds.DefaultModel;
    }

    internal static int? ParseModelMaxOutputTokens(string[] args, string? environmentValue = null)
        => ParsePositiveOptionalInt(
            ParseArg(args, "--model-max-output-tokens")
                ?? environmentValue,
            $"--model-max-output-tokens / {ModelMaxOutputTokensEnvVar}");

    internal static int? ParseSetupModelMaxOutputTokens(
        string[] args,
        int? gameModelMaximum,
        bool setupUsesGameModel,
        string? environmentValue = null)
        => ParsePositiveOptionalInt(
            ParseArg(args, "--setup-model-max-output-tokens")
                ?? environmentValue,
            $"--setup-model-max-output-tokens / {SetupModelMaxOutputTokensEnvVar}")
            ?? (setupUsesGameModel ? gameModelMaximum : null);

    internal static (int? GameModel, int? SetupModel) ResolveModelMaxOutputTokens(
        string[] args,
        string gameModel,
        string setupModel,
        string? gameEnvironmentValue,
        string? setupEnvironmentValue)
    {
        int? gameMaximum = ParseModelMaxOutputTokens(args, gameEnvironmentValue);
        int? setupMaximum = ParseSetupModelMaxOutputTokens(
            args,
            gameMaximum,
            string.Equals(setupModel, gameModel, StringComparison.OrdinalIgnoreCase),
            setupEnvironmentValue);
        return (gameMaximum, setupMaximum);
    }

    private static int? ParsePositiveOptionalInt(string? configured, string settingName)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        if (int.TryParse(configured, out int value) && value > 0)
            return value;

        throw new ArgumentException(
            $"{settingName} must be a positive integer.");
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
        Console.Error.WriteLine("  session-runner --player <name> --datee <name> [options]");
        Console.Error.WriteLine("  session-runner --player-def <path> --datee-def <path> [options]");
        Console.Error.WriteLine("  session-runner --resimulate <slug> [--from-turn <N>] [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --turns <N>        Override maximum session turns (default: 30)");
        Console.Error.WriteLine("  --agent <type>      Select decision agent: score, llm, human (default: score)");
        Console.Error.WriteLine($"  --model <spec>      Game-turn LLM target (default: {AnthropicModelIds.DefaultModel})");
        Console.Error.WriteLine($"  --setup-model <m>   Setup generator model (default: same as --model; env: {SessionSetupModelEnvVar})");
        Console.Error.WriteLine($"  --player-agent-model <m> LLM player decision model (default: {AnthropicModelIds.DefaultModel}; env: {PlayerAgentModelEnvVar})");
        Console.Error.WriteLine($"  --model-max-output-tokens <N> Provider model output capability (env: {ModelMaxOutputTokensEnvVar})");
        Console.Error.WriteLine($"  --setup-model-max-output-tokens <N> Setup-model output capability (env: {SetupModelMaxOutputTokensEnvVar})");
        Console.Error.WriteLine("  --overlay-model <m> Run an overlay/refinement model on top of primary adapter output (via Groq)");
        Console.Error.WriteLine("  --difficulty <pct> Reduce check success probability by N% (e.g. --difficulty 15 = 15% harder)");
        Console.Error.WriteLine("  --seed <int>       Seed value for deterministic dice checks");
        Console.Error.WriteLine("  --debug            Write an accompanying session-XXX-debug.md log containing full raw API transcripts");
        Console.Error.WriteLine("  --disable-traps    Deliberately run without trap data (skips traps.json load; session header notes this explicitly)");
        Console.Error.WriteLine();
        string available = ListAvailableCharacters();
        Console.Error.WriteLine($"Available characters: {available}");
    }
}

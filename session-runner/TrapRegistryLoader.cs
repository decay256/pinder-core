using System;
using System.IO;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

/// <summary>
/// Loads an ITrapRegistry from traps.json, with graceful fallback to NullTrapRegistry.
/// Extracted from Program.cs for testability (issue #353).
/// </summary>
internal static class TrapRegistryLoader
{
    /// <summary>
    /// Environment variable that overrides default trap file search paths.
    /// </summary>
    internal const string EnvVarName = "PINDER_TRAPS_PATH";

    /// <summary>
    /// Attempts to load a JsonTrapRepository from traps.json.
    /// Search order:
    ///   1. PINDER_TRAPS_PATH env var (if set)
    ///   2. Relative path: {baseDir}/data/traps/traps.json
    ///   3. Repo root path (walking up from baseDir)
    /// Falls back to NullTrapRegistry on any failure.
    /// </summary>
    /// <param name="baseDir">Base directory to search relative to (typically AppContext.BaseDirectory).</param>
    /// <param name="warningWriter">TextWriter for warnings (typically Console.Error).</param>
    /// <returns>A loaded ITrapRegistry — either JsonTrapRepository or NullTrapRegistry fallback.</returns>
    internal static ITrapRegistry Load(string baseDir, TextWriter warningWriter)
    {
        // 1. Check environment variable override
        string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envPath))
        {
            return TryLoadFromPath(envPath!, warningWriter);
        }

        // 2. Relative path from base directory
        string relativePath = Path.Combine(baseDir, "data", "traps", "traps.json");
        if (File.Exists(relativePath))
        {
            return TryLoadFromPath(relativePath, warningWriter);
        }

        // 3. Walk up from base directory looking for repo root with data/traps/traps.json
        string? resolvedPath = FindTrapsJsonUpward(baseDir);
        if (resolvedPath != null)
        {
            return TryLoadFromPath(resolvedPath, warningWriter);
        }

        warningWriter.WriteLine("[WARN] traps.json not found — traps disabled");
        return new NullTrapRegistry();
    }

    /// <summary>
    /// Loads a JsonTrapRepository from a specific file path.
    /// Falls back to NullTrapRegistry if the file is missing, unreadable, or corrupt.
    /// </summary>
    private static ITrapRegistry TryLoadFromPath(string path, TextWriter warningWriter)
    {
        try
        {
            string json = File.ReadAllText(path);
            var repo = new JsonTrapRepository(json);
            warningWriter.WriteLine($"[INFO] Loaded traps from {path}");
            return repo;
        }
        catch (Exception ex)
        {
            warningWriter.WriteLine($"[WARN] Failed to load traps from {path}: {ex.Message} — traps disabled");
            return new NullTrapRegistry();
        }
    }

    /// <summary>
    /// Walks up from the given directory looking for data/traps/traps.json.
    /// </summary>
    private static string? FindTrapsJsonUpward(string startDir)
    {
        string? dir = startDir;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "data", "traps", "traps.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}

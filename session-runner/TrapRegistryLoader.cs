using System;
using System.IO;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

/// <summary>
/// Loads an ITrapRegistry from traps.json.
/// Extracted from Program.cs for testability (issue #353).
///
/// Trap data is a core gameplay contract, not a cosmetic optional asset: a
/// missing or corrupt traps.json now fails session-runner setup (throws)
/// instead of silently falling back to <see cref="NullTrapRegistry"/>. A
/// deliberate no-traps mode is available via the session-runner
/// <c>--disable-traps</c> flag, which constructs <see cref="NullTrapRegistry"/>
/// directly and never calls into this loader — see Program.Setup.cs.
/// </summary>
internal static class TrapRegistryLoader
{
    /// <summary>
    /// Environment variable that overrides default trap file search paths.
    /// </summary>
    internal const string EnvVarName = "PINDER_TRAPS_PATH";

    /// <summary>
    /// Loads a JsonTrapRepository from traps.json.
    /// Search order:
    ///   1. PINDER_TRAPS_PATH env var (if set)
    ///   2. Relative path: {baseDir}/data/traps/traps.json
    ///   3. Repo root path (walking up from baseDir)
    /// Throws if traps.json cannot be found, read, or parsed — trap data is
    /// required for session-runner setup. Callers that need to run without
    /// traps must opt in explicitly (e.g. session-runner's --disable-traps
    /// flag) rather than relying on this method to fail open.
    /// </summary>
    /// <param name="baseDir">Base directory to search relative to (typically AppContext.BaseDirectory).</param>
    /// <param name="infoWriter">TextWriter for informational messages (typically Console.Error).</param>
    /// <returns>A loaded JsonTrapRepository-backed ITrapRegistry.</returns>
    /// <exception cref="FileNotFoundException">traps.json could not be found or read.</exception>
    /// <exception cref="InvalidDataException">traps.json content could not be parsed.</exception>
    internal static ITrapRegistry Load(string baseDir, TextWriter infoWriter)
    {
        // 1. Check environment variable override
        string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envPath))
        {
            return LoadFromPath(envPath!, infoWriter);
        }

        // 2. Relative path from base directory
        string relativePath = Path.Combine(baseDir, "data", "traps", "traps.json");
        if (File.Exists(relativePath))
        {
            return LoadFromPath(relativePath, infoWriter);
        }

        // 3. Walk up from base directory looking for repo root with data/traps/traps.json
        string? resolvedPath = FindTrapsJsonUpward(baseDir);
        if (resolvedPath != null)
        {
            return LoadFromPath(resolvedPath, infoWriter);
        }

        throw new FileNotFoundException(
            $"traps.json not found. Searched {EnvVarName} env var (not set), " +
            $"'{relativePath}', and upward from '{baseDir}'. Trap data is required for " +
            "session-runner setup; pass --disable-traps to intentionally run without traps.");
    }

    /// <summary>
    /// Loads a JsonTrapRepository from a specific file path.
    /// Throws if the file is missing, unreadable, or corrupt.
    /// </summary>
    private static ITrapRegistry LoadFromPath(string path, TextWriter infoWriter)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            throw new FileNotFoundException($"Failed to read traps.json at {path}: {ex.Message}", path, ex);
        }

        JsonTrapRepository repo;
        try
        {
            repo = new JsonTrapRepository(json);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to parse traps.json at {path}: {ex.Message}", ex);
        }

        infoWriter.WriteLine($"[INFO] Loaded traps from {path}");
        return repo;
    }

    /// <summary>
    /// Resolves the trap registry for a session-runner run.
    /// When <paramref name="disableTraps"/> is true (the explicit
    /// <c>--disable-traps</c> CLI opt-out), returns <see cref="NullTrapRegistry"/>
    /// directly without touching traps.json. Otherwise delegates to
    /// <see cref="Load"/>, which throws if trap data is missing or corrupt.
    /// </summary>
    internal static ITrapRegistry Resolve(bool disableTraps, string baseDir, TextWriter infoWriter)
    {
        if (disableTraps)
        {
            infoWriter.WriteLine("[INFO] Traps disabled via --disable-traps (deliberate no-traps mode)");
            return new NullTrapRegistry();
        }

        return Load(baseDir, infoWriter);
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

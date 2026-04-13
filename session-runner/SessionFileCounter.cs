using System;
using System.IO;

/// <summary>
/// Extracts the next session number from a directory of session markdown files.
/// Files are expected to follow the naming convention: session-NNN-name-vs-name.md
/// Also resolves the playtest directory path via environment variable or directory walking.
/// </summary>
internal static class SessionFileCounter
{
    /// <summary>
    /// Environment variable that overrides default playtest directory search paths.
    /// </summary>
    internal const string EnvVarName = "PINDER_PLAYTESTS_PATH";

    /// <summary>
    /// Scans the given directory for session-*.md files and returns the next
    /// available session number (highest existing + 1, or 1 if none exist).
    /// </summary>
    /// <param name="directory">Absolute path to the directory containing session files.</param>
    /// <returns>The next session number to use (>= 1).</returns>
    public static int GetNextSessionNumber(string directory)
    {
        int nextNum = 1;
        foreach (var f in Directory.GetFiles(directory, "session-*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            // name = "session-005-sable-vs-brick" → Split('-') = ["session","005","sable","vs","brick"]
            var parts = name.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int num))
                nextNum = Math.Max(nextNum, num + 1);
        }
        return nextNum;
    }

    /// <summary>
    /// Claims the next session number atomically by creating a placeholder .lock file.
    /// Concurrent processes will get different numbers. Caller is responsible for
    /// deleting the .lock file after writing the real session file.
    /// Returns the claimed session number.
    /// </summary>
    public static int ClaimNextSessionNumber(string directory)
    {
        // Clean up stale .lock files that have no corresponding .md file
        // (left by crashed processes). Never deletes .md files.
        foreach (var lockFile in Directory.GetFiles(directory, "session-*.lock"))
        {
            string mdPath = lockFile.Replace(".lock", ".md");
            // Only remove lock if the corresponding session file doesn't exist
            // and the lock is older than 60 seconds (not actively being written)
            if (!File.Exists(mdPath))
            {
                try
                {
                    var lockAge = DateTime.UtcNow - File.GetCreationTimeUtc(lockFile);
                    if (lockAge.TotalSeconds > 60)
                        File.Delete(lockFile);
                }
                catch { }
            }
        }

        for (int attempt = 0; attempt < 100; attempt++)
        {
            int candidate = GetNextSessionNumber(directory);
            string lockPath = Path.Combine(directory, $"session-{candidate:D3}.lock");
            try
            {
                // FileMode.CreateNew fails atomically if the file already exists
                using var fs = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return candidate;
            }
            catch (IOException)
            {
                // Another process claimed this number — retry
            }
        }
        throw new InvalidOperationException("Could not claim a session number after 100 attempts");
    }

    /// <summary>Removes the .lock placeholder after the real session file is written.</summary>
    public static void ReleaseLock(string directory, int sessionNumber)
    {
        string lockPath = Path.Combine(directory, $"session-{sessionNumber:D3}.lock");
        try { File.Delete(lockPath); } catch { }
    }

    /// <summary>
    /// Resolves the playtest output directory.
    /// Search order:
    ///   1. PINDER_PLAYTESTS_PATH env var (if set and directory exists)
    ///   2. Walk up from baseDir looking for design/playtests/
    ///   3. Hardcoded fallback path
    /// </summary>
    /// <param name="baseDir">Base directory to search relative to (typically AppContext.BaseDirectory).</param>
    /// <returns>Absolute path to the playtests directory, or null if not found.</returns>
    public static string? ResolvePlaytestDirectory(string baseDir)
    {
        // 1. Check environment variable override
        string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            return Path.GetFullPath(envPath!);
        }

        // 2. Walk up from base directory looking for design/playtests/
        string? dir = baseDir;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "design", "playtests");
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        // 3. Hardcoded fallback
        const string fallback = "/root/.openclaw/agents-extra/pinder/design/playtests";
        if (Directory.Exists(fallback))
        {
            return fallback;
        }

        return null;
    }
}

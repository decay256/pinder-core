using System;
using System.IO;

/// <summary>
/// Extracts the next session number from a directory of session markdown files.
/// Files are expected to follow the naming convention: session-NNN-name-vs-name.md
/// </summary>
internal static class SessionFileCounter
{
    /// <summary>
    /// Scans the given directory for session-*.md files and returns the next
    /// available session number (highest existing + 1, or 1 if none exist).
    /// </summary>
    /// <param name="directory">Directory to scan for session files.</param>
    /// <returns>The next session number to use.</returns>
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
}

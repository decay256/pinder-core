using System;
using System.IO;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Self-contained data-file resolver (the production DataFileLocator lives
    /// in the session-runner project, which this tool does not reference). Walks
    /// up from a base dir, honoring PINDER_DATA_PATH first — same contract.
    /// </summary>
    internal static class HarnessDataLocator
    {
        private const string EnvVarName = "PINDER_DATA_PATH";

        public static string? FindDataFile(string baseDir, string relativePath)
        {
            string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrEmpty(envPath))
            {
                string envCandidate = Path.Combine(envPath!, relativePath);
                if (File.Exists(envCandidate) || Directory.Exists(envCandidate))
                    return Path.GetFullPath(envCandidate);
            }

            string? dir = baseDir;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }
    }
}

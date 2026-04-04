using System;
using System.IO;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Resolves paths to data files by walking up from a base directory.
    /// Follows the same pattern as TrapRegistryLoader.
    /// </summary>
    public static class DataFileLocator
    {
        /// <summary>
        /// Environment variable that overrides default data file search paths.
        /// </summary>
        internal const string EnvVarName = "PINDER_DATA_PATH";

        /// <summary>
        /// Find a data file by walking up from baseDir.
        /// Checks PINDER_DATA_PATH env var first, then walks up directories.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <param name="relativePath">Relative path to the data file (e.g. "data/items/starter-items.json").</param>
        /// <returns>Absolute path to the file, or null if not found.</returns>
        public static string? FindDataFile(string baseDir, string relativePath)
        {
            // 1. Check environment variable override
            string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrEmpty(envPath))
            {
                string envCandidate = Path.Combine(envPath!, relativePath);
                if (File.Exists(envCandidate))
                    return Path.GetFullPath(envCandidate);
            }

            // 2. Walk up from baseDir
            string? dir = baseDir;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        /// <summary>
        /// Find the repo root by walking up from baseDir, looking for a directory
        /// that contains both "data" and "src" subdirectories.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <returns>Absolute path to the repo root, or null if not found.</returns>
        public static string? FindRepoRoot(string baseDir)
        {
            string? dir = baseDir;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "data")) &&
                    Directory.Exists(Path.Combine(dir, "src")))
                {
                    return Path.GetFullPath(dir);
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }
    }
}

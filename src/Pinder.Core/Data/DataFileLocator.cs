using System;
using System.IO;

namespace Pinder.Core.Data
{
    /// <summary>
    /// Resolves Pinder data files across runtime and repository layouts.
    /// </summary>
    public static class DataFileLocator
    {
        /// <summary>
        /// Environment variable that overrides default data-file search paths.
        /// </summary>
        public const string EnvVarName = "PINDER_DATA_PATH";

        /// <summary>
        /// Finds a data file by checking <see cref="EnvVarName"/> first, then
        /// walking up from <paramref name="baseDir"/>.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <param name="relativePath">
        /// Relative path to the data file. The path is tried verbatim and with
        /// the first path segment case-flipped so runtime <c>Data/</c> and
        /// repository <c>data/</c> layouts both resolve.
        /// </param>
        /// <returns>Absolute path to the file, or null if not found.</returns>
        public static string? FindDataFile(string baseDir, string relativePath)
        {
            string? envPath = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrEmpty(envPath))
            {
                string? found = TryResolveInDirectory(envPath!, relativePath);
                if (found != null)
                    return found;
            }

            string? dir = baseDir;
            while (dir != null)
            {
                string? found = TryResolveInDirectory(dir, relativePath);
                if (found != null)
                    return found;

                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        /// <summary>
        /// Finds the repo root by walking up from <paramref name="baseDir"/>,
        /// looking for a directory that contains both "data" and "src".
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

        private static string? TryResolveInDirectory(string directory, string relativePath)
        {
            string candidate = Path.Combine(directory, relativePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            string? flipped = FlipFirstSegmentCase(relativePath);
            if (flipped == null)
                return null;

            string flippedCandidate = Path.Combine(directory, flipped);
            return File.Exists(flippedCandidate)
                ? Path.GetFullPath(flippedCandidate)
                : null;
        }

        private static string? FlipFirstSegmentCase(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            string normalized = relativePath.Replace('\\', '/');
            int slash = normalized.IndexOf('/');
            if (slash <= 0)
                return null;

            string head = normalized.Substring(0, slash);
            string tail = normalized.Substring(slash + 1);
            string flippedHead;

            if (head == head.ToLowerInvariant())
                flippedHead = char.ToUpperInvariant(head[0]) + head.Substring(1);
            else if (head == head.ToUpperInvariant() || char.IsUpper(head[0]))
                flippedHead = char.ToLowerInvariant(head[0]) + head.Substring(1);
            else
                return null;

            if (flippedHead == head)
                return null;

            return Path.Combine(flippedHead, tail.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}

using SharedDataFileLocator = Pinder.Core.Data.DataFileLocator;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Session-runner compatibility wrapper for the shared Pinder data-file locator.
    /// </summary>
    public static class DataFileLocator
    {
        /// <summary>
        /// Environment variable that overrides default data file search paths.
        /// </summary>
        internal const string EnvVarName = SharedDataFileLocator.EnvVarName;

        /// <summary>
        /// Find a data file by walking up from baseDir.
        /// Checks PINDER_DATA_PATH env var first, then walks up directories.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <param name="relativePath">Relative path to the data file (e.g. "data/items/starter-items.json").</param>
        /// <returns>Absolute path to the file, or null if not found.</returns>
        public static string? FindDataFile(string baseDir, string relativePath)
            => SharedDataFileLocator.FindDataFile(baseDir, relativePath);

        /// <summary>
        /// Find the repo root by walking up from baseDir, looking for a directory
        /// that contains both "data" and "src" subdirectories.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <returns>Absolute path to the repo root, or null if not found.</returns>
        public static string? FindRepoRoot(string baseDir)
            => SharedDataFileLocator.FindRepoRoot(baseDir);
    }
}

using System;
using System.IO;
using Pinder.SessionRunner;

namespace Pinder.Core.TestCommon
{
    public static class TestRepoLocator
    {
        public static string RepoRoot =>
            DataFileLocator.FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);

        public static string FindRepoSubdir(string subdir)
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, subdir);
                if (Directory.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate {subdir} in any ancestor of the test binary.");
        }

        public static string FindRepoSubdir(params string[] pathSegments)
            => FindRepoSubdir(Path.Combine(pathSegments));
    }
}

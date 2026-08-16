using System.IO;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Materialization
{
    internal static class MaterializationFixtureFiles
    {
        public static string RepositoryRoot
        {
            get
            {
                string current = Directory.GetCurrentDirectory();
                while (!File.Exists(Path.Combine(current, "Pinder.Core.sln")))
                {
                    string? parent = Directory.GetParent(current)?.FullName;
                    if (parent == null)
                    {
                        return Directory.GetCurrentDirectory();
                    }
                    current = parent;
                }
                return current;
            }
        }

        public static string ReadSnapshot(string fileName)
            => File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "tests",
                "Pinder.LlmAdapters.Tests",
                "AgentJournals",
                "Materialization",
                "Fixtures",
                "snapshots",
                fileName)).Trim();

        public static string ReadNormalized(string fileName)
            => File.ReadAllText(Path.Combine(RepositoryRoot, "tests", "Pinder.LlmAdapters.Tests", "AgentJournals", "Materialization", "Fixtures", "normalized", fileName)).Trim();
    }
}

using System;
using System.IO;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "SessionRunner")]
    public class DataFileLocatorTests : IDisposable
    {
        private readonly string? _originalDataPath;

        public DataFileLocatorTests()
        {
            _originalDataPath = Environment.GetEnvironmentVariable(DataFileLocator.EnvVarName);
            Environment.SetEnvironmentVariable(DataFileLocator.EnvVarName, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(DataFileLocator.EnvVarName, _originalDataPath);
        }

        [Fact]
        public void FindDataFile_FromRepoSubdirectory_FindsFile()
        {
            // Start from deep inside repo and find data/traps/traps.json
            string? repoRoot = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);
            Assert.NotNull(repoRoot);

            string? found = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "items", "starter-items.json"));

            Assert.NotNull(found);
            Assert.True(File.Exists(found));
        }

        [Fact]
        public void FindDataFile_NonexistentFile_ReturnsNull()
        {
            string? found = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "nonexistent", "file.json"));

            Assert.Null(found);
        }

        [Fact]
        public void FindDataFile_EnvironmentOverrideTakesPrecedenceOverBaseDirectoryWalk()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string envRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "env")).FullName;
                string runtimeRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "runtime")).FullName;
                string runtimeBase = Directory.CreateDirectory(Path.Combine(runtimeRoot, "bin", "Debug", "net8.0")).FullName;

                string envFile = WriteDataFile(envRoot, Path.Combine("data", "items"), "starter-items.json", "env");
                WriteDataFile(runtimeRoot, Path.Combine("data", "items"), "starter-items.json", "runtime");
                Environment.SetEnvironmentVariable(DataFileLocator.EnvVarName, envRoot);

                string? found = DataFileLocator.FindDataFile(
                    runtimeBase,
                    Path.Combine("data", "items", "starter-items.json"));

                Assert.Equal(Path.GetFullPath(envFile), found);
            }
            finally
            {
                DeleteTempDirectory(tempRoot);
            }
        }

        [Fact]
        public void FindDataFile_SupportsCaseFlippedFirstSegment()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string repoRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "repo")).FullName;
                string runtimeBase = Directory.CreateDirectory(Path.Combine(repoRoot, "src", "Pinder.GameApi", "bin", "Debug", "net8.0")).FullName;
                WriteDataFile(repoRoot, "data", "delivery-instructions.yaml", "repo-root");

                string? found = DataFileLocator.FindDataFile(
                    runtimeBase,
                    Path.Combine("Data", "delivery-instructions.yaml"));

                Assert.NotNull(found);
                Assert.Equal("repo-root", File.ReadAllText(found!));
            }
            finally
            {
                DeleteTempDirectory(tempRoot);
            }
        }

        [Fact]
        public void FindRepoRoot_FromTestBinDirectory_FindsRoot()
        {
            string? root = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);
            Assert.NotNull(root);
            Assert.True(Directory.Exists(Path.Combine(root!, "data")));
            Assert.True(Directory.Exists(Path.Combine(root!, "src")));
        }

        [Fact]
        public void FindRepoRoot_FromRootDir_ReturnsNull()
        {
            // Root directory has no data+src pair
            string? root = DataFileLocator.FindRepoRoot("/");
            Assert.Null(root);
        }

        [Fact]
        public void FindDataFile_CharacterDefinitions_Found()
        {
            var names = new[] { "gerald", "velvet", "sable", "brick", "zyx" };
            foreach (var name in names)
            {
                string? path = DataFileLocator.FindDataFile(
                    AppContext.BaseDirectory,
                    Path.Combine("data", "characters", $"{name}.json"));

                Assert.NotNull(path);
                Assert.True(File.Exists(path), $"Character definition for {name} should exist");
            }
        }

        [Fact]
        public void FindDataFile_AnatomyParameters_Found()
        {
            string? path = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "anatomy", "anatomy-parameters.json"));

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void SessionRunnerDataFileLocator_UsesSharedLocator()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string repoRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "repo")).FullName;
                string runtimeBase = Directory.CreateDirectory(Path.Combine(repoRoot, "session-runner", "bin", "Debug", "net8.0")).FullName;
                WriteDataFile(repoRoot, "data", "delivery-instructions.yaml", "repo-root");

                string? found = Pinder.SessionRunner.DataFileLocator.FindDataFile(
                    runtimeBase,
                    Path.Combine("Data", "delivery-instructions.yaml"));

                Assert.NotNull(found);
                Assert.Equal("repo-root", File.ReadAllText(found!));
            }
            finally
            {
                DeleteTempDirectory(tempRoot);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "pinder-data-file-locator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempDirectory(string path)
        {
            try { Directory.Delete(path, recursive: true); } catch { }
        }

        private static string WriteDataFile(string root, string dataDirectory, string fileName, string contents)
        {
            string dataRoot = Directory.CreateDirectory(Path.Combine(root, dataDirectory)).FullName;
            string path = Path.Combine(dataRoot, fileName);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}

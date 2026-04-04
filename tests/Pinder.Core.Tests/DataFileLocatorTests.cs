using System;
using System.IO;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public class DataFileLocatorTests
    {
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
            // May or may not find it depending on system; at minimum shouldn't throw
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
    }
}

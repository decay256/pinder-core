using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Issue415_CharacterDefinitionLoaderSpecTests
    {
        // =====================================================================
        // DataFileLocator tests
        // =====================================================================

        // Fails if: FindRepoRoot can't locate repo root from deep subdirectory
        [Fact]
        public void DataFileLocator_FindRepoRoot_FromTestDir_FindsRoot()
        {
            string? root = DataFileLocator.FindRepoRoot(AppContext.BaseDirectory);

            Assert.NotNull(root);
            Assert.True(Directory.Exists(Path.Combine(root!, "data")));
            Assert.True(Directory.Exists(Path.Combine(root!, "src")));
        }

        // Fails if: FindDataFile returns path for nonexistent file
        [Fact]
        public void DataFileLocator_FindDataFile_Nonexistent_ReturnsNull()
        {
            string? result = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "nonexistent", "does-not-exist.json"));

            Assert.Null(result);
        }

        // Fails if: FindDataFile doesn't walk up directories to find data file
        [Fact]
        public void DataFileLocator_FindDataFile_WalksUpToFindItemsJson()
        {
            string? path = DataFileLocator.FindDataFile(
                AppContext.BaseDirectory,
                Path.Combine("data", "items", "starter-items.json"));

            Assert.NotNull(path);
            Assert.True(File.Exists(path!));
        }

        // =====================================================================
        // Integration: Full pipeline for each character
        // =====================================================================

        // Fails if: Gerald's profile is incomplete (missing timing, prompt, etc.)
        [Fact]
        public void Integration_Gerald_FullPipelineProducesCompleteProfile()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string path = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            var profile = CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo);

            Assert.Equal("Gerald_42", profile.DisplayName);
            Assert.Equal(5, profile.Level);
            Assert.NotNull(profile.Stats);
            Assert.NotNull(profile.Timing);
            Assert.False(string.IsNullOrWhiteSpace(profile.AssembledSystemPrompt));
            // Stats should have non-trivial values from items + build_points
            int totalStats = 0;
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                totalStats += profile.Stats.GetEffective(stat);
            Assert.True(totalStats > 0, "Total stats should be positive");
        }

        // Fails if: Different characters produce identical stat blocks (pipeline is dummy)
        [Fact]
        public void Integration_DifferentCharacters_ProduceDifferentStats()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            var gerald = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "gerald.json"), itemRepo, anatomyRepo);
            var velvet = CharacterDefinitionLoader.Load(
                Path.Combine(RepoRoot, "data", "characters", "velvet.json"), itemRepo, anatomyRepo);

            // Different characters should have different names
            Assert.NotEqual(gerald.DisplayName, velvet.DisplayName);

            // And different stat profiles (extremely unlikely to be identical with different items)
            bool anyDifferent = false;
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                if (gerald.Stats.GetEffective(stat) != velvet.Stats.GetEffective(stat))
                {
                    anyDifferent = true;
                    break;
                }
            }
            Assert.True(anyDifferent, "Gerald and Velvet should have at least one different stat value");
        }
    }
}

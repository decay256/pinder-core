using System;
using System.IO;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.SessionSetup;
using Xunit;
using Xunit.Abstractions;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #1179: prove the archetypes_enabled ON path is wired end-to-end
    /// through the production load entry point
    /// (<see cref="CharacterDefinitionLoader.Load"/>). #1174 added the flag and
    /// the suppression (OFF) path; this verifies that passing
    /// archetypesEnabled:true genuinely injects archetype content into the
    /// assembled system prompt of a REAL character (zyx) with REAL data files,
    /// and that the default / false path suppresses it.
    ///
    /// The PromptCatalog framing strings ("ACTIVE ARCHETYPE") are wired by the
    /// test assembly's module initializer (CoreTestWiring) before any [Fact]
    /// runs, so the production prompt assembly has the catalog available.
    /// </summary>
    [Collection("StaticWiring")]
    public class Issue1179_ArchetypesEnabledOnPathTests
    {
        // Resolved by running the real production load against zyx with the real
        // archetype catalog (see Research Log in the PR). Asserting on the
        // concrete name proves the injection is non-vacuous.
        private const string ZyxArchetypeName = "The Wall of Text";

        private readonly ITestOutputHelper _output;

        public Issue1179_ArchetypesEnabledOnPathTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        private static IItemRepository LoadItemRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "items", "starter-items.json"));
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository LoadAnatomyRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            return new JsonAnatomyRepository(json);
        }

        private static string ZyxPath =>
            Path.Combine(RepoRoot, "data", "characters", "zyx.json");

        [Fact]
        public void Load_Zyx_ArchetypesEnabled_InjectsArchetypeContent()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            var profile = CharacterDefinitionLoader.Load(
                ZyxPath, itemRepo, anatomyRepo, archetypesEnabled: true);

            _output.WriteLine($"Resolved ActiveArchetype: {profile.ActiveArchetype?.Name ?? "(null)"}");

            Assert.NotNull(profile.ActiveArchetype);
            Assert.Equal(ZyxArchetypeName, profile.ActiveArchetype!.Name);

            Assert.Contains("ACTIVE ARCHETYPE", profile.AssembledSystemPrompt, StringComparison.Ordinal);
            // Non-vacuous: the concrete resolved archetype name must appear.
            Assert.Contains(ZyxArchetypeName, profile.AssembledSystemPrompt, StringComparison.Ordinal);
        }

        [Fact]
        public void Load_Zyx_ArchetypesDisabled_SuppressesArchetypeContent()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();

            // Explicit false.
            var profileFalse = CharacterDefinitionLoader.Load(
                ZyxPath, itemRepo, anatomyRepo, archetypesEnabled: false);

            Assert.Null(profileFalse.ActiveArchetype);
            Assert.DoesNotContain("ACTIVE ARCHETYPE", profileFalse.AssembledSystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(ZyxArchetypeName, profileFalse.AssembledSystemPrompt, StringComparison.Ordinal);

            // Default 3-arg overload must also suppress (default flag is false/OFF).
            var profileDefault = CharacterDefinitionLoader.Load(ZyxPath, itemRepo, anatomyRepo);

            Assert.Null(profileDefault.ActiveArchetype);
            Assert.DoesNotContain("ACTIVE ARCHETYPE", profileDefault.AssembledSystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(ZyxArchetypeName, profileDefault.AssembledSystemPrompt, StringComparison.Ordinal);
        }
    }
}

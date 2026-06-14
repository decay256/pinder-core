using System;
using System.IO;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #1153 — golden byte-identity oracle for the GM system prompt.
    ///
    /// The #1153 refactor collapsed the multi-section GM base
    /// (vision/world/doctrine/dramatic-craft/friction/curiosity/arc/probing)
    /// into a single pre-assembled <c>game_master_prompt</c> field. This is a
    /// BEHAVIOR-PRESERVING change: the live compiled prompt for both the datee
    /// and player-avatar sessions must stay byte-for-byte identical to what the
    /// old section-assembling builder produced.
    ///
    /// The golden fixtures were captured from the UNMODIFIED pre-refactor code
    /// with a fixed "PLACEHOLDER_CHAR" character spec and checked in verbatim.
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class Issue1153_GameMasterPromptGoldenTests
    {
        private static string FixturesDir =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Issue1153");

        private static string ReadFixture(string name) =>
            File.ReadAllText(Path.Combine(FixturesDir, name));

        private static string RepoYamlPath()
        {
            // tests/.../bin/<cfg>/net8.0  →  repo root is five levels up.
            return Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "data", "game-definition.yaml");
        }

        // ── PinderDefaults literal: byte-identical to the captured goldens ──

        [Fact]
        public void BuildDatee_PinderDefaults_MatchesGoldenByteForByte()
        {
            var actual = SessionSystemPromptBuilder
                .BuildDateeEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            Assert.Equal(ReadFixture("golden_datee.txt"), actual);
        }

        [Fact]
        public void BuildPlayerAvatar_PinderDefaults_MatchesGoldenByteForByte()
        {
            var actual = SessionSystemPromptBuilder
                .BuildPlayerAvatarEx("PLACEHOLDER_CHAR", GameDefinition.PinderDefaults).Text;
            Assert.Equal(ReadFixture("golden_pa.txt"), actual);
        }

        // ── Real YAML through the real parser: proves the migration is exact ──

        [Fact]
        public void BuildDatee_RealYaml_MatchesGoldenByteForByte()
        {
            var def = GameDefinition.LoadFrom(File.ReadAllText(RepoYamlPath()));
            var actual = SessionSystemPromptBuilder
                .BuildDateeEx("PLACEHOLDER_CHAR", def).Text;
            Assert.Equal(ReadFixture("golden_datee_yaml.txt"), actual);
        }

        [Fact]
        public void BuildPlayerAvatar_RealYaml_MatchesGoldenByteForByte()
        {
            var def = GameDefinition.LoadFrom(File.ReadAllText(RepoYamlPath()));
            var actual = SessionSystemPromptBuilder
                .BuildPlayerAvatarEx("PLACEHOLDER_CHAR", def).Text;
            Assert.Equal(ReadFixture("golden_pa_yaml.txt"), actual);
        }
    }
}

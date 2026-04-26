using System.IO;
using System.Linq;
using Pinder.Core.Data;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #255 — display_name + summary fields on TrapDefinition.
    /// Verifies (a) JsonTrapRepository parses both fields from data/traps/traps.json,
    /// (b) all 6 canonical traps have non-empty values, (c) defaults are safe when
    /// the fields are absent (display_name → id, summary → "").
    /// </summary>
    [Trait("Category", "Core")]
    public sealed class JsonTrapRepositoryDisplayNameSummaryTests
    {
        private static string LoadTrapsJson()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "traps.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!, "data", "traps", "traps.json"));
        }

        // === Canonical data file: every trap has display_name + summary ===

        [Fact]
        public void AllCanonicalTraps_Have_NonEmpty_DisplayName()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            foreach (var trap in repo.GetAll())
            {
                Assert.False(string.IsNullOrWhiteSpace(trap.DisplayName),
                    $"Trap '{trap.Id}' must have a non-empty display_name.");
            }
        }

        [Fact]
        public void AllCanonicalTraps_Have_NonEmpty_Summary()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            foreach (var trap in repo.GetAll())
            {
                Assert.False(string.IsNullOrWhiteSpace(trap.Summary),
                    $"Trap '{trap.Id}' must have a non-empty summary.");
            }
        }

        [Theory]
        [InlineData(StatType.Charm,         "Cringe")]
        [InlineData(StatType.Rizz,          "Creep")]
        [InlineData(StatType.Honesty,       "Overshare")]
        [InlineData(StatType.Chaos,         "Unhinged")]
        [InlineData(StatType.Wit,           "Pretentious")]
        [InlineData(StatType.SelfAwareness, "Spiral")]
        public void DisplayName_MatchesSpec(StatType stat, string expected)
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal(expected, repo.GetTrap(stat)!.DisplayName);
        }

        // Spot-check one summary so we'd catch silent drift in the canonical copy.
        [Fact]
        public void Cringe_Summary_Matches_Spec()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal(
                "You're aware of how you're coming across, which is making it worse.",
                repo.GetTrap(StatType.Charm)!.Summary);
        }

        // === Defaulting behaviour for legacy data without the new fields ===

        private const string LegacyJsonNoDisplayName = @"
        [
          {
            ""id"": ""legacy"",
            ""stat"": ""charm"",
            ""effect"": ""disadvantage"",
            ""effect_value"": 0,
            ""duration_turns"": 1,
            ""llm_instruction"": ""legacy instruction"",
            ""clear_method"": ""SA vs DC 12""
          }
        ]";

        [Fact]
        public void DisplayName_FallsBackToId_WhenAbsent()
        {
            var repo = new JsonTrapRepository(LegacyJsonNoDisplayName);
            var trap = repo.GetTrap(StatType.Charm)!;
            Assert.Equal("legacy", trap.Id);
            Assert.Equal("legacy", trap.DisplayName);
        }

        [Fact]
        public void Summary_DefaultsToEmptyString_WhenAbsent()
        {
            var repo = new JsonTrapRepository(LegacyJsonNoDisplayName);
            var trap = repo.GetTrap(StatType.Charm)!;
            Assert.Equal("", trap.Summary);
        }

        // === Explicit values when present ===

        private const string ExplicitJson = @"
        [
          {
            ""id"": ""custom"",
            ""display_name"": ""Custom Trap"",
            ""summary"": ""A bespoke flavour line."",
            ""stat"": ""rizz"",
            ""effect"": ""stat_penalty"",
            ""effect_value"": 1,
            ""duration_turns"": 2,
            ""llm_instruction"": ""..."",
            ""clear_method"": ""SA vs DC 10""
          }
        ]";

        [Fact]
        public void Explicit_DisplayName_AndSummary_AreParsed()
        {
            var repo = new JsonTrapRepository(ExplicitJson);
            var trap = repo.GetTrap(StatType.Rizz)!;
            Assert.Equal("Custom Trap", trap.DisplayName);
            Assert.Equal("A bespoke flavour line.", trap.Summary);
        }
    }
}

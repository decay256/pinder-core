using System.IO;
using System.Linq;
using Pinder.Core.Data;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Schema contract tests: verify that traps.json uses the flat field names
    /// that JsonTrapRepository.ParseTrap() expects. Guards against field name
    /// drift (e.g. "triggered_by_stat" vs "stat", nested vs flat).
    /// Issue #306 — ensures parser never crashes on load.
    /// </summary>
    public sealed class TrapsJsonSchemaContractTests
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

        /// <summary>
        /// Verifies JsonTrapRepository loads all 6 traps without any FormatException.
        /// This is the primary regression test for the schema mismatch described in #306.
        /// </summary>
        [Fact]
        public void JsonTrapRepository_LoadsAll6Traps_WithoutException()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);
            var traps = repo.GetAll().ToList();

            Assert.Equal(6, traps.Count);

            // Verify all 6 expected IDs are present
            var ids = traps.Select(t => t.Id).OrderBy(x => x).ToArray();
            Assert.Equal(
                new[] { "creep", "cringe", "overshare", "pretentious", "spiral", "unhinged" },
                ids);
        }

        /// <summary>
        /// Verifies each stat type maps to exactly one trap — no gaps in coverage.
        /// </summary>
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        public void JsonTrapRepository_ReturnsNonNull_ForEveryStatType(StatType stat)
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);
            var trap = repo.GetTrap(stat);
            Assert.NotNull(trap);
        }

        /// <summary>
        /// Validates the raw JSON uses flat field names (not nested objects).
        /// If someone rewrites traps.json with nested "mechanical_effect" or
        /// "triggered_by_stat" fields, this test catches it before runtime.
        /// </summary>
        [Fact]
        public void TrapsJson_UsesFlat_FieldNames()
        {
            var json = LoadTrapsJson();

            // These nested/wrong field names must NOT appear (the #306 mismatch)
            Assert.DoesNotContain("triggered_by_stat", json);
            Assert.DoesNotContain("mechanical_effect", json);
            Assert.DoesNotContain("prompt_taint", json);

            // These flat field names MUST appear (what the parser expects)
            Assert.Contains("\"stat\"", json);
            Assert.Contains("\"effect\"", json);
            Assert.Contains("\"effect_value\"", json);
            Assert.Contains("\"duration_turns\"", json);
            Assert.Contains("\"llm_instruction\"", json);
        }

        /// <summary>
        /// Verifies each trap has a non-empty llm_instruction (critical for LLM prompt taint).
        /// </summary>
        [Fact]
        public void AllTraps_HaveNonEmpty_LlmInstruction()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);

            foreach (var trap in repo.GetAll())
            {
                Assert.False(string.IsNullOrWhiteSpace(trap.LlmInstruction),
                    $"Trap '{trap.Id}' has empty LlmInstruction — parser may have missed the field.");
            }
        }
    }
}

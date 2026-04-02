using System.IO;
using System.Linq;
using Pinder.Core.Data;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests that data/traps/traps.json loads correctly via JsonTrapRepository
    /// and contains all 6 canonical trap definitions.
    /// </summary>
    public sealed class JsonTrapRepositoryDataFileTests
    {
        private static string LoadTrapsJson()
        {
            // Walk up from bin output to find data/traps/traps.json
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "traps.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!, "data", "traps", "traps.json"));
        }

        [Fact]
        public void TrapsJson_Loads_All6Traps()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);
            var all = repo.GetAll().ToList();
            Assert.Equal(6, all.Count);
        }

        [Fact]
        public void TrapsJson_ContainsAllExpectedIds()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);
            var ids = repo.GetAll().Select(t => t.Id).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "creep", "cringe", "overshare", "pretentious", "spiral", "unhinged" }, ids);
        }

        [Theory]
        [InlineData(StatType.Charm, "cringe", TrapEffect.Disadvantage, 0, 1)]
        [InlineData(StatType.Rizz, "creep", TrapEffect.StatPenalty, 2, 2)]
        [InlineData(StatType.Honesty, "overshare", TrapEffect.OpponentDCIncrease, 2, 1)]
        [InlineData(StatType.Chaos, "unhinged", TrapEffect.Disadvantage, 0, 1)]
        [InlineData(StatType.Wit, "pretentious", TrapEffect.OpponentDCIncrease, 3, 1)]
        [InlineData(StatType.SelfAwareness, "spiral", TrapEffect.Disadvantage, 0, 2)]
        public void TrapsJson_TrapDefinition_MatchesExpected(
            StatType stat, string expectedId, TrapEffect expectedEffect, int expectedValue, int expectedDuration)
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);
            var trap = repo.GetTrap(stat);

            Assert.NotNull(trap);
            Assert.Equal(expectedId, trap!.Id);
            Assert.Equal(stat, trap.Stat);
            Assert.Equal(expectedEffect, trap.Effect);
            Assert.Equal(expectedValue, trap.EffectValue);
            Assert.Equal(expectedDuration, trap.DurationTurns);
            Assert.False(string.IsNullOrEmpty(trap.LlmInstruction));
            Assert.Equal("SA vs DC 12", trap.ClearMethod);
        }

        [Fact]
        public void TrapsJson_AllTraps_HaveLlmInstructions()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);

            foreach (var trap in repo.GetAll())
            {
                Assert.False(string.IsNullOrWhiteSpace(trap.LlmInstruction),
                    $"Trap '{trap.Id}' is missing LLM instruction.");
            }
        }

        [Fact]
        public void TrapsJson_GetLlmInstruction_ReturnsForAllStats()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);

            var stats = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty,
                                StatType.Chaos, StatType.Wit, StatType.SelfAwareness };

            foreach (var stat in stats)
            {
                var instruction = repo.GetLlmInstruction(stat);
                Assert.False(string.IsNullOrWhiteSpace(instruction),
                    $"GetLlmInstruction for {stat} returned null/empty.");
            }
        }
    }
}

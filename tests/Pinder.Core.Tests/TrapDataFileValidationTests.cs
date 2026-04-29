using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Data;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Validates that data/traps/traps.json contains all 6 canonical trap definitions
    /// and that they match the spec exactly when loaded via JsonTrapRepository.
    /// </summary>
    [Trait("Category", "Core")]
    public sealed class TrapDataFileValidationTests
    {
        private static string FindTrapsJsonPath()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "traps.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            return Path.Combine(dir!, "data", "traps", "traps.json");
        }

        private static string LoadTrapsJson() => File.ReadAllText(FindTrapsJsonPath());

        private static JsonTrapRepository CreateRepo() => new JsonTrapRepository(LoadTrapsJson());

        // === AC1: data/traps/traps.json exists with all 6 traps ===

        // Mutation: would catch if file doesn't exist or path is wrong
        [Fact]
        public void TrapsJsonFile_Exists_AtExpectedPath()
        {
            var path = FindTrapsJsonPath();
            Assert.True(File.Exists(path), "data/traps/traps.json must exist");
        }

        // Mutation: would catch if trap-schema.json is missing (AC2)
        [Fact]
        public void TrapSchemaJsonFile_Exists_AtExpectedPath()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "trap-schema.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            var path = Path.Combine(dir!, "data", "traps", "trap-schema.json");
            Assert.True(File.Exists(path), "data/traps/trap-schema.json must exist");
        }

        // === AC3: JsonTrapRepository loads them correctly ===

        // Mutation: would catch if constructor throws on valid data
        [Fact]
        public void Constructor_DoesNotThrow_WhenLoadingTrapsJson()
        {
            var json = LoadTrapsJson();
            var ex = Record.Exception(() => new JsonTrapRepository(json));
            Assert.Null(ex);
        }

        // Mutation: would catch if GetAll returns wrong count (e.g., 5 or 7 traps)
        [Fact]
        public void GetAll_Returns_Exactly6Traps()
        {
            var repo = CreateRepo();
            var all = repo.GetAll().ToList();
            Assert.Equal(6, all.Count);
        }

        // Mutation: would catch if any stat type is missing from the data
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        public void GetTrap_ReturnsNonNull_ForEachStatType(StatType stat)
        {
            var repo = CreateRepo();
            var trap = repo.GetTrap(stat);
            Assert.NotNull(trap);
        }

        // === AC4: All 6 trap IDs match ===

        // Mutation: would catch if Charm trap has wrong id (e.g., "charm" instead of "cringe")
        [Fact]
        public void Charm_TrapId_Is_Cringe()
        {
            var repo = CreateRepo();
            Assert.Equal("cringe", repo.GetTrap(StatType.Charm)!.Id);
        }

        // Mutation: would catch if Rizz trap has wrong id
        [Fact]
        public void Rizz_TrapId_Is_Creep()
        {
            var repo = CreateRepo();
            Assert.Equal("creep", repo.GetTrap(StatType.Rizz)!.Id);
        }

        // Mutation: would catch if Honesty trap has wrong id
        [Fact]
        public void Honesty_TrapId_Is_Overshare()
        {
            var repo = CreateRepo();
            Assert.Equal("overshare", repo.GetTrap(StatType.Honesty)!.Id);
        }

        // Mutation: would catch if Chaos trap has wrong id
        [Fact]
        public void Chaos_TrapId_Is_Unhinged()
        {
            var repo = CreateRepo();
            Assert.Equal("unhinged", repo.GetTrap(StatType.Chaos)!.Id);
        }

        // Mutation: would catch if Wit trap has wrong id
        [Fact]
        public void Wit_TrapId_Is_Pretentious()
        {
            var repo = CreateRepo();
            Assert.Equal("pretentious", repo.GetTrap(StatType.Wit)!.Id);
        }

        // Mutation: would catch if SelfAwareness trap has wrong id
        [Fact]
        public void SelfAwareness_TrapId_Is_Spiral()
        {
            var repo = CreateRepo();
            Assert.Equal("spiral", repo.GetTrap(StatType.SelfAwareness)!.Id);
        }

        // === Trap effect types (spec data table) ===

        // Mutation: would catch if cringe effect is stat_penalty instead of disadvantage
        [Fact]
        public void Cringe_Effect_Is_Disadvantage()
        {
            var repo = CreateRepo();
            Assert.Equal(TrapEffect.Disadvantage, repo.GetTrap(StatType.Charm)!.Effect);
        }

        // Mutation: would catch if creep effect is disadvantage instead of stat_penalty
        [Fact]
        public void Creep_Effect_Is_StatPenalty()
        {
            var repo = CreateRepo();
            Assert.Equal(TrapEffect.StatPenalty, repo.GetTrap(StatType.Rizz)!.Effect);
        }

        // Mutation: would catch if overshare effect is disadvantage instead of opponent_dc_increase
        [Fact]
        public void Overshare_Effect_Is_OpponentDCIncrease()
        {
            var repo = CreateRepo();
            Assert.Equal(TrapEffect.OpponentDCIncrease, repo.GetTrap(StatType.Honesty)!.Effect);
        }

        // Mutation: would catch if unhinged effect is stat_penalty instead of disadvantage
        [Fact]
        public void Unhinged_Effect_Is_Disadvantage()
        {
            var repo = CreateRepo();
            Assert.Equal(TrapEffect.Disadvantage, repo.GetTrap(StatType.Chaos)!.Effect);
        }

        // Mutation: would catch if pretentious effect is disadvantage instead of opponent_dc_increase
        [Fact]
        public void Pretentious_Effect_Is_OpponentDCIncrease()
        {
            var repo = CreateRepo();
            Assert.Equal(TrapEffect.OpponentDCIncrease, repo.GetTrap(StatType.Wit)!.Effect);
        }

        // Mutation: would catch if spiral effect is stat_penalty instead of disadvantage
        [Fact]
        public void Spiral_Effect_Is_Disadvantage()
        {
            var repo = CreateRepo();
            Assert.Equal(TrapEffect.Disadvantage, repo.GetTrap(StatType.SelfAwareness)!.Effect);
        }

        // === Effect values ===

        // Mutation: would catch if cringe has effect_value != 0
        [Fact]
        public void Cringe_EffectValue_Is_0()
        {
            var repo = CreateRepo();
            Assert.Equal(0, repo.GetTrap(StatType.Charm)!.EffectValue);
        }

        // Mutation: would catch if creep has effect_value != 2 (e.g., 3)
        [Fact]
        public void Creep_EffectValue_Is_2()
        {
            var repo = CreateRepo();
            Assert.Equal(2, repo.GetTrap(StatType.Rizz)!.EffectValue);
        }

        // Mutation: would catch if overshare has effect_value != 2 (e.g., 3)
        [Fact]
        public void Overshare_EffectValue_Is_2()
        {
            var repo = CreateRepo();
            Assert.Equal(2, repo.GetTrap(StatType.Honesty)!.EffectValue);
        }

        // Mutation: would catch if pretentious has effect_value != 3 (e.g., 2)
        [Fact]
        public void Pretentious_EffectValue_Is_3()
        {
            var repo = CreateRepo();
            Assert.Equal(3, repo.GetTrap(StatType.Wit)!.EffectValue);
        }

        // === Duration turns ===

        // Per #371 (W2a): every trap is fixed at 3 turns regardless of which trap.
        // The data file's duration_turns is now 3 for every trap.
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        public void AllTraps_Duration_Is_3(StatType stat)
        {
            var repo = CreateRepo();
            Assert.Equal(3, repo.GetTrap(stat)!.DurationTurns);
        }

        // === Clear method ===

        // Mutation: would catch if any trap has wrong clear_method
        // Per #371 (W2a): clear method is SA-option-selection, not a DC-12 roll.
        [Fact]
        public void AllTraps_ClearMethod_Is_PickAnySelfAwarenessOption()
        {
            var repo = CreateRepo();
            const string expected = "Pick any Self-Awareness option (selection disarms; SA fail triggers Spiral)";
            foreach (var trap in repo.GetAll())
            {
                Assert.Equal(expected, trap.ClearMethod);
            }
        }

        // === LLM instructions (via GetLlmInstruction) ===

        // Mutation: would catch if charm llm_instruction is null or empty
        [Fact]
        public void GetLlmInstruction_ReturnsNonEmpty_ForAllStats()
        {
            var repo = CreateRepo();
            var stats = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            foreach (var stat in stats)
            {
                var instruction = repo.GetLlmInstruction(stat);
                Assert.NotNull(instruction);
                Assert.NotEmpty(instruction!);
            }
        }

        // Mutation: would catch if cringe llm_instruction doesn't contain expected key phrase
        [Fact]
        public void Cringe_LlmInstruction_ContainsExpectedContent()
        {
            var repo = CreateRepo();
            var instruction = repo.GetLlmInstruction(StatType.Charm);
            Assert.Contains("over-explained", instruction!);
            Assert.Contains("self-undermined", instruction!);
        }

        // Mutation: would catch if creep llm_instruction has wrong content
        [Fact]
        public void Creep_LlmInstruction_ContainsExpectedContent()
        {
            var repo = CreateRepo();
            var instruction = repo.GetLlmInstruction(StatType.Rizz);
            Assert.Contains("agenda", instruction!);
        }

        // Mutation: would catch if overshare llm_instruction has wrong content
        [Fact]
        public void Overshare_LlmInstruction_ContainsExpectedContent()
        {
            var repo = CreateRepo();
            var instruction = repo.GetLlmInstruction(StatType.Honesty);
            Assert.Contains("personal detail", instruction!);
        }

        // Mutation: would catch if pretentious llm_instruction has wrong content
        [Fact]
        public void Pretentious_LlmInstruction_ContainsExpectedContent()
        {
            var repo = CreateRepo();
            var instruction = repo.GetLlmInstruction(StatType.Wit);
            Assert.Contains("condescending", instruction!);
        }

        // Mutation: would catch if spiral llm_instruction has wrong content
        [Fact]
        public void Spiral_LlmInstruction_ContainsExpectedContent()
        {
            var repo = CreateRepo();
            var instruction = repo.GetLlmInstruction(StatType.SelfAwareness);
            Assert.Contains("meta-commentary", instruction!);
        }

        // === Uniqueness: no duplicate stat keys ===

        // Mutation: would catch if two traps share the same stat (one would silently overwrite)
        [Fact]
        public void AllTraps_Have_UniqueIds()
        {
            var repo = CreateRepo();
            var ids = repo.GetAll().Select(t => t.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        // Mutation: would catch if two traps share the same stat type
        [Fact]
        public void AllTraps_Have_UniqueStatTypes()
        {
            var repo = CreateRepo();
            var stats = repo.GetAll().Select(t => t.Stat).ToList();
            Assert.Equal(stats.Count, stats.Distinct().Count());
        }

        // === Edge case: nat1_bonus is empty string for all traps ===

        // Mutation: would catch if nat1_bonus is null instead of empty string
        [Fact]
        public void AllTraps_Nat1Bonus_IsEmptyString()
        {
            var repo = CreateRepo();
            foreach (var trap in repo.GetAll())
            {
                Assert.Equal("", trap.Nat1Bonus);
            }
        }

        // === Error conditions (spec §Error Conditions) ===

        // Mutation: would catch if constructor accepts null without throwing
        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenJsonIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTrapRepository(null!));
        }

        // Mutation: would catch if constructor accepts invalid JSON without throwing
        [Fact]
        public void Constructor_ThrowsFormatException_WhenJsonIsInvalid()
        {
            Assert.Throws<FormatException>(() => new JsonTrapRepository("not json"));
        }

        // Mutation: would catch if constructor accepts non-array JSON without throwing
        [Fact]
        public void Constructor_ThrowsFormatException_WhenJsonIsNotArray()
        {
            Assert.Throws<FormatException>(() => new JsonTrapRepository("{\"id\": \"test\"}"));
        }
    }
}

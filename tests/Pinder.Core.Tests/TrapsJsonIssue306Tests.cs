using System;
using System.IO;
using System.Linq;
using Pinder.Core.Data;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #306: traps.json schema mismatch — parser crashes on load.
    /// Verifies that traps.json uses the flat schema expected by JsonTrapRepository
    /// and that all 6 traps load correctly with correct field values.
    /// </summary>
    [Trait("Category", "Core")]
    public sealed class TrapsJsonIssue306Tests
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

        // ── AC1: traps.json exists and loads without exception ──

        // Mutation: catches if traps.json file is missing or path is wrong
        [Fact]
        public void TrapsJson_FileExists_AtExpectedPath()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "traps", "traps.json")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir); // file must be findable
        }

        // Mutation: catches if constructor throws FormatException due to schema mismatch
        [Fact]
        public void JsonTrapRepository_Constructor_DoesNotThrow()
        {
            var json = LoadTrapsJson();
            var exception = Record.Exception(() => new JsonTrapRepository(json));
            Assert.Null(exception);
        }

        // Mutation: catches if trap count is wrong (missing or extra traps)
        [Fact]
        public void GetAll_Returns_Exactly6Traps()
        {
            var json = LoadTrapsJson();
            var repo = new JsonTrapRepository(json);
            Assert.Equal(6, repo.GetAll().Count());
        }

        // ── AC2: All 6 trap IDs match expected values ──

        // Mutation: catches if charm trap has wrong id (e.g. "charm" instead of "cringe")
        [Fact]
        public void Charm_Trap_Id_Is_Cringe()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal("cringe", repo.GetTrap(StatType.Charm)?.Id);
        }

        // Mutation: catches if rizz trap has wrong id
        [Fact]
        public void Rizz_Trap_Id_Is_Creep()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal("creep", repo.GetTrap(StatType.Rizz)?.Id);
        }

        // Mutation: catches if honesty trap has wrong id
        [Fact]
        public void Honesty_Trap_Id_Is_Overshare()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal("overshare", repo.GetTrap(StatType.Honesty)?.Id);
        }

        // Mutation: catches if chaos trap has wrong id
        [Fact]
        public void Chaos_Trap_Id_Is_Unhinged()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal("unhinged", repo.GetTrap(StatType.Chaos)?.Id);
        }

        // Mutation: catches if wit trap has wrong id
        [Fact]
        public void Wit_Trap_Id_Is_Pretentious()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal("pretentious", repo.GetTrap(StatType.Wit)?.Id);
        }

        // Mutation: catches if self_awareness trap has wrong id
        [Fact]
        public void SelfAwareness_Trap_Id_Is_Spiral()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            Assert.Equal("spiral", repo.GetTrap(StatType.SelfAwareness)?.Id);
        }

        // ── AC3: Correct effect types per trap ──

        // Mutation: catches if cringe effect type is wrong (e.g. StatPenalty instead of Disadvantage)
        [Theory]
        [InlineData(StatType.Charm, TrapEffect.Disadvantage)]
        [InlineData(StatType.Rizz, TrapEffect.StatPenalty)]
        [InlineData(StatType.Honesty, TrapEffect.OpponentDCIncrease)]
        [InlineData(StatType.Chaos, TrapEffect.Disadvantage)]
        [InlineData(StatType.Wit, TrapEffect.OpponentDCIncrease)]
        [InlineData(StatType.SelfAwareness, TrapEffect.Disadvantage)]
        public void Trap_Effect_MatchesExpected(StatType stat, TrapEffect expectedEffect)
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            var trap = repo.GetTrap(stat);
            Assert.NotNull(trap);
            Assert.Equal(expectedEffect, trap!.Effect);
        }

        // ── AC4: Correct effect values ──

        // Mutation: catches if effect_value is wrong (e.g. creep has 0 instead of 2)
        [Theory]
        [InlineData(StatType.Charm, 0)]
        [InlineData(StatType.Rizz, 2)]
        [InlineData(StatType.Honesty, 2)]
        [InlineData(StatType.Chaos, 0)]
        [InlineData(StatType.Wit, 3)]
        [InlineData(StatType.SelfAwareness, 0)]
        public void Trap_EffectValue_MatchesExpected(StatType stat, int expectedValue)
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            var trap = repo.GetTrap(stat);
            Assert.NotNull(trap);
            Assert.Equal(expectedValue, trap!.EffectValue);
        }

        // ── AC5: Correct duration turns ──

        // Per #371 (W2a): every trap is fixed at 3 turns regardless of which trap.
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        public void Trap_DurationTurns_Is_3(StatType stat)
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            var trap = repo.GetTrap(stat);
            Assert.NotNull(trap);
            Assert.Equal(3, trap!.DurationTurns);
        }

        // ── AC6: Clear method (W2a #371): SA-option-selection ──
        [Fact]
        public void AllTraps_ClearMethod_IsPickAnySelfAwarenessOption()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            const string expected = "Pick any Self-Awareness option (selection disarms; SA fail triggers Spiral)";
            foreach (var trap in repo.GetAll())
            {
                Assert.Equal(expected, trap.ClearMethod);
            }
        }

        // ── AC7: LLM instructions are non-empty for all traps ──

        // Mutation: catches if llm_instruction field is missing/empty (parser would have crashed on nested schema)
        [Fact]
        public void AllTraps_Have_NonEmpty_LlmInstruction()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            foreach (var trap in repo.GetAll())
            {
                Assert.False(string.IsNullOrWhiteSpace(trap.LlmInstruction),
                    $"Trap '{trap.Id}' has empty LlmInstruction");
            }
        }

        // ── AC8: GetLlmInstruction works for all stats ──

        // Mutation: catches if GetLlmInstruction returns null (stat mapping broken)
        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        public void GetLlmInstruction_ReturnsNonNull_ForAllStats(StatType stat)
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            var instruction = repo.GetLlmInstruction(stat);
            Assert.NotNull(instruction);
            Assert.NotEmpty(instruction!);
        }

        // ── Schema validation: flat fields, no nested objects ──

        // Mutation: catches if someone reintroduces nested "triggered_by_stat" field
        [Fact]
        public void TrapsJson_DoesNotContain_NestedFieldNames()
        {
            var json = LoadTrapsJson();
            Assert.DoesNotContain("triggered_by_stat", json);
            Assert.DoesNotContain("mechanical_effect", json);
            Assert.DoesNotContain("prompt_taint", json);
        }

        // Mutation: catches if flat "stat" field is missing from JSON
        [Fact]
        public void TrapsJson_Contains_RequiredFlatFields()
        {
            var json = LoadTrapsJson();
            Assert.Contains("\"id\"", json);
            Assert.Contains("\"stat\"", json);
            Assert.Contains("\"effect\"", json);
            Assert.Contains("\"effect_value\"", json);
            Assert.Contains("\"duration_turns\"", json);
            Assert.Contains("\"llm_instruction\"", json);
        }

        // ── Error conditions ──

        // Mutation: catches if constructor doesn't validate null input
        [Fact]
        public void Constructor_Throws_OnNullJson()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTrapRepository(null!));
        }

        // Mutation: catches if parser accepts invalid JSON without throwing
        [Fact]
        public void Constructor_Throws_OnInvalidJson()
        {
            Assert.ThrowsAny<Exception>(() => new JsonTrapRepository("not valid json"));
        }

        // Mutation: catches if parser accepts non-array top-level JSON
        [Fact]
        public void Constructor_Throws_OnNonArrayTopLevel()
        {
            Assert.ThrowsAny<Exception>(() => new JsonTrapRepository("{\"id\": \"test\"}"));
        }

        // ── Edge case: case-sensitive stat keys ──

        // Mutation: catches if parser accepts PascalCase stat names (e.g. "Charm" instead of "charm")
        [Fact]
        public void Constructor_Throws_OnPascalCaseStat()
        {
            var json = @"[{""id"":""test"",""stat"":""Charm"",""effect"":""disadvantage"",""effect_value"":0,""duration_turns"":1,""llm_instruction"":""test""}]";
            Assert.ThrowsAny<Exception>(() => new JsonTrapRepository(json));
        }

        // Mutation: catches if parser accepts PascalCase effect names
        [Fact]
        public void Constructor_Throws_OnPascalCaseEffect()
        {
            var json = @"[{""id"":""test"",""stat"":""charm"",""effect"":""Disadvantage"",""effect_value"":0,""duration_turns"":1,""llm_instruction"":""test""}]";
            Assert.ThrowsAny<Exception>(() => new JsonTrapRepository(json));
        }

        // ── Edge case: no duplicate stat coverage ──

        // Mutation: catches if two traps share same stat (last one silently overwrites)
        [Fact]
        public void AllTraps_Have_UniqueStats()
        {
            var repo = new JsonTrapRepository(LoadTrapsJson());
            var stats = repo.GetAll().Select(t => t.Stat).ToList();
            Assert.Equal(stats.Count, stats.Distinct().Count());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    // ---------------------------------------------------------------
    // Capturing LLM adapter: records all contexts passed to it
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public sealed class CapturingLlmAdapter : ILlmAdapter
    {
        public List<DialogueContext> DialogueContexts { get; } = new List<DialogueContext>();
        public List<DateeContext> DateeContexts { get; } = new List<DateeContext>();

        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
        {
            DialogueContexts.Add(context);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey, you come here often?"),
                new DialogueOption(StatType.Honesty, "I have to be real with you..."),
                new DialogueOption(StatType.Wit, "Did you know that penguins propose with pebbles?"),
                new DialogueOption(StatType.Chaos, "I once ate a whole pizza in a bouncy castle.")
            };
            return Task.FromResult(options);
        }

        public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
        {
            DateeContexts.Add(context);
            return Task.FromResult(new DateeResponse("..."));
        }

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult<string?>(null);
        }
        public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
    }

    // ---------------------------------------------------------------
    // Trap registry that returns a specific trap with LLM instruction
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public sealed class TestTrapRegistry : ITrapRegistry
    {
        private readonly Dictionary<StatType, TrapDefinition> _traps =
            new Dictionary<StatType, TrapDefinition>();

        public void Register(TrapDefinition trap) => _traps[trap.Stat] = trap;

        public TrapDefinition? GetTrap(StatType stat)
        {
            _traps.TryGetValue(stat, out var trap);
            return trap;
        }

        public string? GetLlmInstruction(StatType stat)
        {
            _traps.TryGetValue(stat, out var trap);
            return trap?.LlmInstruction;
        }
    }

    // ---------------------------------------------------------------
    // JsonTrapRepository tests
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public class JsonTrapRepositoryTests
    {
        private const string SampleJson = @"[
            {
                ""id"": ""charm_trap"",
                ""stat"": ""charm"",
                ""effect"": ""disadvantage"",
                ""effect_value"": 0,
                ""duration_turns"": 3,
                ""llm_instruction"": ""Your character becomes extremely awkward and stutters uncontrollably."",
                ""clear_method"": ""Roll Charm DC 15"",
                ""nat1_bonus"": ""You accidentally insult their mother.""
            },
            {
                ""id"": ""wit_trap"",
                ""stat"": ""wit"",
                ""effect"": ""stat_penalty"",
                ""effect_value"": 2,
                ""duration_turns"": 2,
                ""llm_instruction"": ""Your character can only speak in terrible puns."",
                ""clear_method"": """",
                ""nat1_bonus"": """"
            }
        ]";

        [Fact]
        public void Constructor_ParsesTrapsCorrectly()
        {
            var repo = new JsonTrapRepository(SampleJson);

            var charmTrap = repo.GetTrap(StatType.Charm);
            Assert.NotNull(charmTrap);
            Assert.Equal("charm_trap", charmTrap!.Id);
            Assert.Equal(StatType.Charm, charmTrap.Stat);
            Assert.Equal(TrapEffect.Disadvantage, charmTrap.Effect);
            Assert.Equal(0, charmTrap.EffectValue);
            Assert.Equal(3, charmTrap.DurationTurns);
            Assert.Equal("Your character becomes extremely awkward and stutters uncontrollably.", charmTrap.LlmInstruction);
            Assert.Equal("Roll Charm DC 15", charmTrap.ClearMethod);
        }

        [Fact]
        public void GetLlmInstruction_ReturnsCorrectInstruction()
        {
            var repo = new JsonTrapRepository(SampleJson);

            Assert.Equal(
                "Your character can only speak in terrible puns.",
                repo.GetLlmInstruction(StatType.Wit));
        }

        [Fact]
        public void GetLlmInstruction_ReturnsNull_ForMissingStat()
        {
            var repo = new JsonTrapRepository(SampleJson);
            Assert.Null(repo.GetLlmInstruction(StatType.Rizz));
        }

        [Fact]
        public void GetTrap_ReturnsNull_ForMissingStat()
        {
            var repo = new JsonTrapRepository(SampleJson);
            Assert.Null(repo.GetTrap(StatType.Honesty));
        }

        [Fact]
        public void GetAll_ReturnsAllTraps()
        {
            var repo = new JsonTrapRepository(SampleJson);
            var all = repo.GetAll().ToList();
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public void Constructor_WithCustomFiles_MergesTraps()
        {
            var customJson = @"[
                {
                    ""id"": ""custom_rizz_trap"",
                    ""stat"": ""rizz"",
                    ""effect"": ""datee_dc_increase"",
                    ""effect_value"": 3,
                    ""duration_turns"": 4,
                    ""llm_instruction"": ""Your rizz has been tainted by custom trap."",
                    ""clear_method"": """",
                    ""nat1_bonus"": """"
                }
            ]";

            var repo = new JsonTrapRepository(SampleJson, new[] { customJson });

            Assert.NotNull(repo.GetTrap(StatType.Charm));
            Assert.NotNull(repo.GetTrap(StatType.Wit));
            Assert.NotNull(repo.GetTrap(StatType.Rizz));
            Assert.Equal("Your rizz has been tainted by custom trap.", repo.GetLlmInstruction(StatType.Rizz));
        }

        [Fact]
        public void Constructor_CustomOverridesPrimary()
        {
            var customJson = @"[
                {
                    ""id"": ""custom_charm_trap"",
                    ""stat"": ""charm"",
                    ""effect"": ""stat_penalty"",
                    ""effect_value"": 1,
                    ""duration_turns"": 5,
                    ""llm_instruction"": ""Custom charm instruction overrides default."",
                    ""clear_method"": """",
                    ""nat1_bonus"": """"
                }
            ]";

            var repo = new JsonTrapRepository(SampleJson, new[] { customJson });

            var charmTrap = repo.GetTrap(StatType.Charm);
            Assert.NotNull(charmTrap);
            Assert.Equal("custom_charm_trap", charmTrap!.Id);
            Assert.Equal("Custom charm instruction overrides default.", charmTrap.LlmInstruction);
        }

        [Fact]
        public void Constructor_ThrowsOnMissingId()
        {
            var badJson = @"[{ ""stat"": ""charm"", ""effect"": ""disadvantage"", ""llm_instruction"": ""test"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnUnknownStat()
        {
            var badJson = @"[{ ""id"": ""bad"", ""stat"": ""unknown"", ""effect"": ""disadvantage"", ""llm_instruction"": ""test"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnUnknownEffect()
        {
            var badJson = @"[{ ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""unknown"", ""llm_instruction"": ""test"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnMissingLlmInstruction()
        {
            var badJson = @"[{ ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnNullJson()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTrapRepository(null!));
        }

        [Fact]
        public void Constructor_ThrowsOnNonArrayJson()
        {
            Assert.Throws<FormatException>(() => new JsonTrapRepository(@"{ ""key"": ""value"" }"));
        }

        [Fact]
        public void Constructor_ParsesAllEffectTypes()
        {
            var json = @"[
                { ""id"": ""t1"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""i1"" },
                { ""id"": ""t2"", ""stat"": ""rizz"", ""effect"": ""stat_penalty"", ""effect_value"": 2, ""duration_turns"": 1, ""llm_instruction"": ""i2"" },
                { ""id"": ""t3"", ""stat"": ""wit"", ""effect"": ""datee_dc_increase"", ""effect_value"": 3, ""duration_turns"": 1, ""llm_instruction"": ""i3"" }
            ]";

            var repo = new JsonTrapRepository(json);
            Assert.Equal(TrapEffect.Disadvantage, repo.GetTrap(StatType.Charm)!.Effect);
            Assert.Equal(TrapEffect.StatPenalty, repo.GetTrap(StatType.Rizz)!.Effect);
            Assert.Equal(TrapEffect.DateeDCIncrease, repo.GetTrap(StatType.Wit)!.Effect);
        }

        [Fact]
        public void Constructor_ParsesAllStatTypes()
        {
            var json = @"[
                { ""id"": ""t1"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i1"" },
                { ""id"": ""t2"", ""stat"": ""rizz"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i2"" },
                { ""id"": ""t3"", ""stat"": ""honesty"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i3"" },
                { ""id"": ""t4"", ""stat"": ""chaos"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i4"" },
                { ""id"": ""t5"", ""stat"": ""wit"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i5"" },
                { ""id"": ""t6"", ""stat"": ""self_awareness"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i6"" }
            ]";

            var repo = new JsonTrapRepository(json);
            Assert.NotNull(repo.GetTrap(StatType.Charm));
            Assert.NotNull(repo.GetTrap(StatType.Rizz));
            Assert.NotNull(repo.GetTrap(StatType.Honesty));
            Assert.NotNull(repo.GetTrap(StatType.Chaos));
            Assert.NotNull(repo.GetTrap(StatType.Wit));
            Assert.NotNull(repo.GetTrap(StatType.SelfAwareness));
        }
    }
}

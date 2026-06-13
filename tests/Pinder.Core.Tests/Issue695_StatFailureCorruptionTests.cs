using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #695: stat-specific failure corruption instructions from
    /// delivery-instructions.yaml.
    ///
    /// #1125: the delivery LLM call (and DeliveryContext) were collapsed into the
    /// deterministic, non-LLM DeliveryOverlay commit step, so the instruction is
    /// no longer threaded into a creative "delivery" prompt. The data-layer
    /// lookup (StatDeliveryInstructions.GetStatFailureInstruction) is retained
    /// and still covered here; the prior DeliveryContext-wiring tests were
    /// rewritten to assert (a) a failed turn still commits a degraded line and
    /// (b) the per-stat instruction the engine would have surfaced is correct.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue695_StatFailureCorruptionTests
    {
        private static StatDeliveryInstructions LoadYaml()
        {
            // Walk up from bin/Debug/netX to repo root
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 10; i++)
            {
                string candidate = Path.Combine(dir, "data", "delivery-instructions.yaml");
                if (File.Exists(candidate))
                    return StatDeliveryInstructions.LoadFrom(File.ReadAllText(candidate));
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            // Fallback: try repo root directly
            string fallback = Path.Combine("/root/.openclaw/workspace/pinder-core", "data", "delivery-instructions.yaml");
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
        }

        [Fact]
        public async Task Failure_RIZZ_turn_commits_degraded_line_and_RIZZ_instruction_is_correct()
        {
            var instructions = LoadYaml();
            var llm = new CapturingLlm(StatType.Rizz);
            // d20=1 → guaranteed failure (stat mod 2, DC ~10+), timing=5
            var dice = new FixedDice(1, 5);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: instructions);
            var session = new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            int rizzIndex = FindOptionIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIndex);

            // #1125: no DeliveryContext is built; the failed roll still commits a
            // deterministically degraded line, and the per-stat instruction the
            // engine surfaces for RIZZ failures remains correct.
            Assert.False(result.Roll.IsSuccess);
            Assert.NotEqual("Nice vibes", result.DeliveredMessage);
            Assert.Contains("DESPAIR",
                instructions.GetStatFailureInstruction(StatType.Rizz, result.Roll.Tier));
        }

        [Fact]
        public async Task Failure_WIT_turn_commits_degraded_line_and_WIT_instruction_is_correct()
        {
            var instructions = LoadYaml();
            var llm = new CapturingLlm(StatType.Wit);
            var dice = new FixedDice(1, 5);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: instructions);
            var session = new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            int witIndex = FindOptionIndex(llm.LastOptions, StatType.Wit);
            var result = await session.ResolveTurnAsync(witIndex);

            Assert.False(result.Roll.IsSuccess);
            Assert.NotEqual("Clever remark", result.DeliveredMessage);
            Assert.Contains("OVERTHINKING",
                instructions.GetStatFailureInstruction(StatType.Wit, result.Roll.Tier));
        }

        [Fact]
        public async Task Success_commits_picked_line_verbatim_noDegradation()
        {
            var instructions = LoadYaml();
            var llm = new CapturingLlm(StatType.Charm);
            // d10=5 (horniness in ctor), d20=20 (nat 20 auto-success), d100=50 (padding)
            var dice = new FixedDice(5, 20, 50);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: instructions);
            var session = new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Nat 20 should always be success → picked line commits verbatim,
            // no failure degradation.
            Assert.Equal(FailureTier.Success, result.Roll.Tier);
            Assert.Equal("Hey there", result.DeliveredMessage);
        }

        [Fact]
        public void GetStatFailureInstruction_returns_correct_text()
        {
            var instructions = LoadYaml();

            string rizzFumble = instructions.GetStatFailureInstruction(StatType.Rizz, FailureTier.Fumble);
            Assert.NotNull(rizzFumble);
            Assert.Contains("DESPAIR", rizzFumble);

            string witCatastrophe = instructions.GetStatFailureInstruction(StatType.Wit, FailureTier.Catastrophe);
            Assert.NotNull(witCatastrophe);
            Assert.Contains("OVERTHINKING", witCatastrophe);

            string charmMisfire = instructions.GetStatFailureInstruction(StatType.Charm, FailureTier.Misfire);
            Assert.NotNull(charmMisfire);
            Assert.Contains("FIXATION", charmMisfire);
        }

        private static int FindOptionIndex(DialogueOption[] options, StatType stat)
        {
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].Stat == stat) return i;
            }
            return 0;
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private sealed class CapturingLlm : ILlmAdapter
        {
            private readonly StatType _targetStat;
            public DialogueOption[] LastOptions { get; private set; }

            public CapturingLlm(StatType targetStat)
            {
                _targetStat = targetStat;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                LastOptions = new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Rizz, "Nice vibes"),
                    new DialogueOption(StatType.Wit, "Clever remark"),
                    new DialogueOption(StatType.Honesty, "Real talk")
                };
                return Task.FromResult(LastOptions);
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new DateeResponse("Reply from datee"));
            }

            public Task<string> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => Task.FromResult(message);
        }

        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _rolls = new Queue<int>();

            public FixedDice(params int[] rolls)
            {
                foreach (var r in rolls)
                    _rolls.Enqueue(r);
            }

            public int Roll(int sides) => _rolls.Count > 0 ? _rolls.Dequeue() : 10;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition GetTrap(StatType stat) => null;
            public string GetLlmInstruction(StatType stat) => null;
        }
    }
}

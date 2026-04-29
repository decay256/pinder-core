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
    /// Issue #695: Verify that stat-specific failure corruption instructions
    /// from delivery-instructions.yaml reach DeliveryContext on failure.
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
        public async Task Failure_RIZZ_delivers_RIZZ_specific_instruction()
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

            // Pick the RIZZ option (index depends on what CapturingLlm returns)
            int rizzIndex = FindOptionIndex(llm.LastOptions, StatType.Rizz);
            await session.ResolveTurnAsync(rizzIndex);

            Assert.NotNull(llm.CapturedDeliveryContext);
            var ctx = llm.CapturedDeliveryContext;

            // Should be a failure
            Assert.NotEqual(FailureTier.None, ctx.Outcome);

            // StatFailureInstruction should be populated with RIZZ-specific text
            Assert.NotNull(ctx.StatFailureInstruction);
            Assert.Contains("DESPAIR", ctx.StatFailureInstruction);
        }

        [Fact]
        public async Task Failure_WIT_delivers_WIT_specific_instruction()
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
            await session.ResolveTurnAsync(witIndex);

            Assert.NotNull(llm.CapturedDeliveryContext);
            var ctx = llm.CapturedDeliveryContext;

            Assert.NotEqual(FailureTier.None, ctx.Outcome);
            Assert.NotNull(ctx.StatFailureInstruction);
            Assert.Contains("OVERTHINKING", ctx.StatFailureInstruction);
        }

        [Fact]
        public async Task Success_has_no_failure_instruction()
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
            await session.ResolveTurnAsync(0);

            Assert.NotNull(llm.CapturedDeliveryContext);
            var sctx = llm.CapturedDeliveryContext;
            // Nat 20 should always be success
            Assert.Equal(FailureTier.None, sctx.Outcome);
            Assert.Null(sctx.StatFailureInstruction);
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
            public DeliveryContext CapturedDeliveryContext { get; private set; }
            public DialogueOption[] LastOptions { get; private set; }

            public CapturingLlm(StatType targetStat)
            {
                _targetStat = targetStat;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
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

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                CapturedDeliveryContext = context;
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("Reply from opponent"));
            }

            public Task<string> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null)
                => Task.FromResult(message);
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

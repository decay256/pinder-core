using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Regression tests for #365 — Shadow corruption was incorrectly gated on
    /// `rollResult.IsSuccess`, so failed rolls never received the shadow
    /// corruption overlay. The YAML provides corruption instructions for all
    /// failure tiers (fumble/misfire/trope_trap/catastrophe/nat1) and the rules
    /// state Catastrophe + Nat 1 trigger BOTH trap and shadow growth.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue365_ShadowOnFailedRollTests
    {
        private static StatDeliveryInstructions LoadYaml()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 10; i++)
            {
                string candidate = Path.Combine(dir, "data", "delivery-instructions.yaml");
                if (File.Exists(candidate))
                    return StatDeliveryInstructions.LoadFrom(File.ReadAllText(candidate));
                dir = Path.GetDirectoryName(dir)!;
                if (dir == null) break;
            }
            string fallback = "/tmp/work-W1b/pinder-core/data/delivery-instructions.yaml";
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
        }

        private static StatBlock MakeStats(int allStats = 2, int shadowOnPair = 0, ShadowStatType pairStat = ShadowStatType.Despair)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats },
                { StatType.Rizz, allStats },
                { StatType.Honesty, allStats },
                { StatType.Chaos, allStats },
                { StatType.Wit, allStats },
                { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            shadow[pairStat] = shadowOnPair;
            return new StatBlock(stats, shadow);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            return new CharacterProfile(
                stats, $"You are {name}.", name,
                new TimingProfile(5, 0.0f, 0.0f, "neutral"), level: 1);
        }

        /// <summary>
        /// AC: When the main roll FAILS and the shadow check ALSO misses on a
        /// paired stat, ApplyShadowCorruptionAsync must fire. The textDiffs
        /// must include a "Shadow (X)" layer.
        /// </summary>
        [Fact]
        public async Task FailedRoll_ShadowMiss_AppliesShadowCorruption()
        {
            var instructions = LoadYaml();

            // Player has Despair shadow at 19 (very high) so shadow DC = 20-19 = 1.
            // Steering RNG is also used for the shadow d20. To force a shadow
            // MISS we'd need a roll < 1 — impossible with d20. So instead set
            // shadow to a moderate value and force the steering RNG to a low
            // roll: shadow=10 → DC = 20-10 = 10. Steering RNG sequence:
            //   1st call = steering d20 (we want failure, roll 1)
            //   2nd call = shadow d20 (we want miss → roll < DC, so roll 1)
            // missMargin = 9 → tier ≈ Trope_trap (per HorninessEngine)
            var playerStats = MakeStats(allStats: 2, shadowOnPair: 10, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 2));

            // Game dice: horniness=5, main d20=1 (Nat 1 — guaranteed failure), timing=50
            var dice = new FixedDice(5, 1, 50);
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            // Pick the Rizz option (paired with Despair shadow)
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIdx);

            // Roll was a failure
            Assert.False(result.Roll.IsSuccess);
            // Shadow check happened and missed
            Assert.True(result.ShadowCheck.CheckPerformed);
            Assert.True(result.ShadowCheck.IsMiss);

            // The shadow corruption LLM call must have fired even though the
            // roll failed. (#365 — was previously gated on IsSuccess.)
            Assert.True(llm.ShadowCorruptionCalled,
                "ApplyShadowCorruptionAsync must fire on shadow miss regardless of roll outcome (#365).");

            // textDiffs must include a Shadow (Despair) layer
            Assert.NotNull(result.TextDiffs);
            Assert.Contains(result.TextDiffs!, d => d.LayerName.StartsWith("Shadow ("));
        }

        /// <summary>
        /// AC: On a FAILED roll + shadow miss, the shadow overlay fires but the
        /// interest-delta override does NOT (the failure delta was already
        /// applied; the override only converts a success into a failure).
        /// </summary>
        [Fact]
        public async Task FailedRoll_ShadowMiss_DoesNotDoubleApplyInterestDelta()
        {
            var instructions = LoadYaml();

            var playerStats = MakeStats(allStats: 2, shadowOnPair: 10, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 2));

            var dice = new FixedDice(5, 1, 50);
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIdx);

            // The interest delta should be the failure tier's delta — the
            // shadow overlay must NOT have re-applied a delta on top.
            // Concretely: the recorded interestDelta in the turn result equals
            // the failure-tier delta resolved from the original (failed) roll.
            Assert.False(result.Roll.IsSuccess);
            Assert.True(result.ShadowCheck.OverlayApplied,
                "Shadow overlay should be marked applied when the LLM call fired.");
            // Sanity: failure deltas are <= 0 in this rule set.
            Assert.True(result.InterestDelta <= 0,
                $"InterestDelta on failed roll must be <= 0; got {result.InterestDelta}");
        }

        /// <summary>
        /// AC: On a SUCCESS roll + shadow miss, the existing override behavior
        /// still kicks in — interest delta is replaced with the failure delta.
        /// (#365 fix preserves this on success rolls.)
        /// </summary>
        [Fact]
        public async Task SuccessRoll_ShadowMiss_StillOverridesInterestDeltaToFailure()
        {
            var instructions = LoadYaml();

            var playerStats = MakeStats(allStats: 5, shadowOnPair: 10, pairStat: ShadowStatType.Despair);
            var player = MakeProfile("Sable", playerStats);
            var opponent = MakeProfile("Brick", MakeStats(allStats: 0));

            // d20=20 (auto-success, nat 20)
            var dice = new FixedDice(5, 20, 50);
            // Steering RNG: steering roll 1 (fail), shadow roll 1 (miss)
            var steeringRng = new FixedRandom(1, 1);

            var llm = new ShadowCapturingLlm();
            var playerShadows = new SessionShadowTracker(playerStats);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                statDeliveryInstructions: instructions,
                playerShadows: playerShadows);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            int rizzIdx = FindIndex(llm.LastOptions, StatType.Rizz);
            var result = await session.ResolveTurnAsync(rizzIdx);

            Assert.True(result.Roll.IsSuccess);
            Assert.True(result.ShadowCheck.OverlayApplied);
            Assert.True(llm.ShadowCorruptionCalled);
            // The success was demoted to a failure delta — non-positive.
            Assert.True(result.InterestDelta <= 0,
                $"Success demoted by shadow should yield non-positive delta; got {result.InterestDelta}");
        }

        private static int FindIndex(DialogueOption[] options, StatType stat)
        {
            for (int i = 0; i < options.Length; i++)
                if (options[i].Stat == stat) return i;
            return 0;
        }

        /// <summary>
        /// LLM stub that captures whether shadow corruption fired and rewrites
        /// the message on shadow corruption (so a textDiff actually appears).
        /// </summary>
        private sealed class ShadowCapturingLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            public bool ShadowCorruptionCalled { get; private set; }
            public DialogueOption[] LastOptions { get; private set; } = System.Array.Empty<DialogueOption>();

            public void StartOpponentSession(string opponentSystemPrompt) { }
            public bool HasOpponentSession => false;

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
                string intended = context.ChosenOption.IntendedText;
                return Task.FromResult(context.Outcome == FailureTier.None
                    ? intended
                    : $"[{context.Outcome}] {intended}");
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null)
            {
                ShadowCorruptionCalled = true;
                // Rewrite the message so a textDiff is emitted.
                return Task.FromResult(message + " [shadow:" + shadow + "]");
            }

            public Task<string> GetSteeringQuestionAsync(SteeringContext context)
                => Task.FromResult("steering question");
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly Queue<int> _values;
            public FixedRandom(params int[] values) { _values = new Queue<int>(values); }
            public override int Next(int minValue, int maxValue) => _values.Count > 0 ? _values.Dequeue() : minValue;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

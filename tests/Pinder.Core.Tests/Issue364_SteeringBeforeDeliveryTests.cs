using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for #364 — Steering question must be appended to the
    /// intended text BEFORE DeliverMessageAsync is called, so the LLM degrades
    /// the combined intended+steering text on a failed roll instead of producing
    /// a perfectly lucid question after a Catastrophe-degraded message.
    ///
    /// Also verifies the textDiffs ordering: Steering layer is emitted FIRST,
    /// the tier-modifier layer is emitted SECOND.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue364_SteeringBeforeDeliveryTests
    {
        private static StatBlock MakeStats(
            int charm = 2, int rizz = 2, int honesty = 2,
            int chaos = 2, int wit = 2, int sa = 2)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, charm },
                { StatType.Rizz, rizz },
                { StatType.Honesty, honesty },
                { StatType.Chaos, chaos },
                { StatType.Wit, wit },
                { StatType.SelfAwareness, sa }
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
            return new StatBlock(stats, shadow);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            return new CharacterProfile(
                stats,
                $"You are {name}.",
                name,
                new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// AC: When steering succeeds, the question is appended to the intended
        /// text BEFORE DeliverMessageAsync is invoked. The LLM sees the combined
        /// text and can degrade the whole thing for a failed roll.
        /// </summary>
        [Fact]
        public async Task Steering_success_appends_question_to_intended_text_before_delivery()
        {
            // Player Charm/Wit/SA = 5 → steering mod 5
            // Opponent SA/Rizz/Honesty = 0 → steering DC 16
            var player = MakeProfile("Sable", MakeStats(charm: 5, wit: 5, sa: 5));
            var opponent = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // Game dice: horniness=1, main d20=15 (success), response delay=50
            var dice = new FixedDice(1, 15, 50);
            // Steering RNG: roll 20 → 20+5=25 ≥ DC 16 → success
            var steeringRng = new FixedRandom(20);

            var llm = new CapturingLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Steering succeeded
            Assert.True(result.Steering.SteeringSucceeded);
            Assert.NotNull(result.Steering.SteeringQuestion);

            // The DeliveryContext seen by the LLM must already contain the
            // combined intended + steering question — that's the whole point
            // of #364.
            Assert.NotNull(llm.CapturedDeliveryContext);
            string intendedSeenByLlm = llm.CapturedDeliveryContext!.ChosenOption.IntendedText;
            Assert.Contains(result.Steering.SteeringQuestion!, intendedSeenByLlm);
            // It should NOT be just the original intended text without steering.
            Assert.NotEqual("Hey there", intendedSeenByLlm);
            Assert.StartsWith("Hey there", intendedSeenByLlm);
        }

        /// <summary>
        /// AC: On a Catastrophe-tier failure with a successful steering roll,
        /// the LLM receives intended+steering and degrades the whole thing.
        /// The final delivered text is the catastrophe-degraded version,
        /// containing no original lucid steering question.
        /// </summary>
        [Fact]
        public async Task Catastrophe_with_steering_success_degrades_combined_text()
        {
            // Player charm=2, wit=5, sa=5 (steering mod = (2+5+5)/3 = 4)
            // Opponent stats=0 → steering DC = 16
            var player = MakeProfile("Sable", MakeStats(charm: 2, wit: 5, sa: 5));
            var opponent = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // Catastrophe: with stat mod 2 vs DC ~10+, a d20=1 (Nat 1) yields
            // catastrophe-tier failure. (NB: Nat 1 is itself a special tier;
            // the assertion tolerates either Catastrophe or Nat1 because both
            // are heavy degradation tiers and either proves the point.)
            var dice = new FixedDice(1, 1, 50);
            // Steering RNG: roll 20 → 20+4=24 ≥ DC 16 → success
            var steeringRng = new FixedRandom(20);

            var llm = new CapturingLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Steering succeeded
            Assert.True(result.Steering.SteeringSucceeded);
            Assert.NotNull(result.Steering.SteeringQuestion);
            // Roll was a failure tier
            Assert.False(result.Roll.IsSuccess);

            // The DeliveryContext that hit the LLM had the combined text
            Assert.NotNull(llm.CapturedDeliveryContext);
            string combined = llm.CapturedDeliveryContext!.ChosenOption.IntendedText;
            Assert.Contains(result.Steering.SteeringQuestion!, combined);

            // The CapturingLlm degrades by uppercasing on failure tiers.
            // The final delivered message is the degraded combined text,
            // i.e. it should be uppercased (proving the LLM rewrote it
            // including the steering question).
            Assert.Equal(combined.ToUpperInvariant(), result.DeliveredMessage);
        }

        /// <summary>
        /// AC: textDiffs are emitted in order Steering FIRST, tier modifier
        /// SECOND. The Steering diff goes intended → intended+question; the
        /// tier diff goes intended+question → delivered.
        /// </summary>
        [Fact]
        public async Task TextDiffs_ordering_steering_first_then_tier_modifier()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 5, wit: 5, sa: 5));
            var opponent = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // d20=1 is Nat 1 (always failure), steering rolls 20 → succeeds
            var dice = new FixedDice(1, 1, 50);
            var steeringRng = new FixedRandom(20);

            var llm = new CapturingLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Steering.SteeringSucceeded);
            Assert.False(result.Roll.IsSuccess);
            Assert.NotNull(result.TextDiffs);
            var diffs = result.TextDiffs!;

            // Find the Steering and tier-modifier layers
            int steeringIdx = -1;
            int tierIdx = -1;
            for (int i = 0; i < diffs.Count; i++)
            {
                if (diffs[i].LayerName == "Steering" && steeringIdx < 0) steeringIdx = i;
                if ((diffs[i].LayerName == "Nat 1" || diffs[i].LayerName == "Catastrophe") && tierIdx < 0) tierIdx = i;
            }

            Assert.True(steeringIdx >= 0, "Steering diff layer must be present");
            Assert.True(tierIdx >= 0, "Tier-modifier diff layer must be present");
            Assert.True(steeringIdx < tierIdx, $"Steering diff (idx {steeringIdx}) must appear before tier diff (idx {tierIdx})");

            // Steering diff goes intended → intended+question
            Assert.Equal("Hey there", diffs[steeringIdx].Before);
            Assert.Contains(result.Steering.SteeringQuestion!, diffs[steeringIdx].After);

            // Tier diff goes intended+question → delivered (i.e. uppercased)
            Assert.Equal(diffs[steeringIdx].After, diffs[tierIdx].Before);
            Assert.Equal(diffs[steeringIdx].After.ToUpperInvariant(), diffs[tierIdx].After);
        }

        /// <summary>
        /// AC: When steering FAILS, the pipeline behaves as before: no
        /// steering diff, no question appended, only the tier-modifier
        /// diff (if any).
        /// </summary>
        [Fact]
        public async Task Steering_failure_no_steering_diff_no_question_appended()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 2, wit: 2, sa: 2));
            var opponent = MakeProfile("Brick", MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, sa: 3));

            var dice = new FixedDice(1, 15, 50);
            // Steering: roll 1 → 1+2=3 < DC 19 → miss
            var steeringRng = new FixedRandom(1);

            var llm = new CapturingLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, opponent, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Steering.SteeringSucceeded);
            Assert.Null(result.Steering.SteeringQuestion);

            // Delivery context's intended text is unchanged
            Assert.NotNull(llm.CapturedDeliveryContext);
            Assert.Equal("Hey there", llm.CapturedDeliveryContext!.ChosenOption.IntendedText);

            if (result.TextDiffs != null)
            {
                Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Steering");
            }
        }

        /// <summary>
        /// LLM stub that captures the DeliveryContext and degrades the message
        /// on failure tiers by uppercasing it. Implements IStatefulLlmAdapter
        /// so steering's "is the adapter stateful?" check passes and a
        /// steering question is generated.
        /// </summary>
        private sealed class CapturingLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            public DeliveryContext? CapturedDeliveryContext { get; private set; }

            public void StartOpponentSession(string opponentSystemPrompt) { }
            public bool HasOpponentSession => false;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Rizz, "Nice"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Real talk")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                CapturedDeliveryContext = context;
                string intended = context.ChosenOption.IntendedText;
                if (context.Outcome == FailureTier.None)
                    return Task.FromResult(intended);
                // Failure: degrade by uppercasing the WHOLE intended text
                // (which now includes the steering question per #364).
                return Task.FromResult(intended.ToUpperInvariant());
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow)
                => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null) => Task.FromResult(message);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context)
                => Task.FromResult("so when are we doing this?");
        }

        private sealed class FixedRandom : Random
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

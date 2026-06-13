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
    /// Regression tests for #364 — the steering question must be appended to the
    /// picked line BEFORE the delivery degradation runs, so a failed roll degrades
    /// the combined intended+steering text instead of leaving a perfectly lucid
    /// question after a degraded message.
    ///
    /// <para>
    /// #1125: the "delivery" step is now the deterministic, non-LLM
    /// <see cref="DeliveryOverlay"/> commit (no <c>DeliverMessageAsync</c> LLM
    /// call). The #364 ordering invariant is unchanged — steering still appends
    /// first, then the tier overlay degrades the whole combined line — so these
    /// tests now assert against <see cref="DeliveryOverlay.Apply"/> instead of a
    /// captured DeliveryContext / an LLM uppercase stub.
    /// </para>
    ///
    /// Also verifies the textDiffs ordering: Steering layer is emitted FIRST,
    /// the tier-modifier layer is emitted SECOND.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue364_SteeringBeforeDeliveryTests
    {
        private const string PickedOption = "Hey there";

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
        /// AC: When steering succeeds, the question is appended to the picked
        /// line BEFORE the delivery overlay runs. On a SUCCESS roll the overlay
        /// commits verbatim, so the committed line is exactly "picked + question".
        /// </summary>
        [Fact]
        public async Task Steering_success_appends_question_to_picked_line_before_delivery()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 5, wit: 5, sa: 5));
            var datee = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // Game dice: horniness=1, main d20=15 (success), response delay=50
            var dice = new FixedDice(1, 15, 50);
            var steeringRng = new FixedRandom(20);

            var llm = new SteeringLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Steering.SteeringSucceeded);
            Assert.NotNull(result.Steering.SteeringQuestion);
            Assert.True(result.Roll.IsSuccess);

            // Success commits the combined picked+question line verbatim.
            string combined = PickedOption + " " + result.Steering.SteeringQuestion;
            Assert.Equal(combined, result.DeliveredMessage);
            Assert.Contains(result.Steering.SteeringQuestion!, result.DeliveredMessage);
            Assert.StartsWith(PickedOption, result.DeliveredMessage);
        }

        /// <summary>
        /// AC: On a Nat-1/Catastrophe-tier failure with a successful steering
        /// roll, the deterministic overlay degrades the WHOLE combined
        /// intended+steering line — the committed text is the overlay output of
        /// "picked + question", proving the question was folded in before
        /// degradation (no lucid question survives intact).
        /// </summary>
        [Fact]
        public async Task Catastrophe_with_steering_success_degrades_combined_text()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 2, wit: 5, sa: 5));
            var datee = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // d20=1 → Nat 1 (Legendary failure tier), steering rolls 20 → succeeds
            var dice = new FixedDice(1, 1, 50);
            var steeringRng = new FixedRandom(20);

            var llm = new SteeringLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Steering.SteeringSucceeded);
            Assert.NotNull(result.Steering.SteeringQuestion);
            Assert.False(result.Roll.IsSuccess);

            // The committed line is the deterministic overlay of the COMBINED line.
            string combined = PickedOption + " " + result.Steering.SteeringQuestion;
            string expected = DeliveryOverlay.Apply(combined, result.Roll.Tier, result.Roll.MissMargin);
            Assert.Equal(expected, result.DeliveredMessage);

            // And it actually degraded (differs from the clean combined line).
            Assert.NotEqual(combined, result.DeliveredMessage);
        }

        /// <summary>
        /// AC: textDiffs are emitted in order Steering FIRST, tier modifier
        /// SECOND. The Steering diff goes picked → picked+question; the tier diff
        /// goes picked+question → committed (overlay output).
        /// </summary>
        [Fact]
        public async Task TextDiffs_ordering_steering_first_then_tier_modifier()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 5, wit: 5, sa: 5));
            var datee = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // d20=1 is Nat 1 (always failure), steering rolls 20 → succeeds
            var dice = new FixedDice(1, 1, 50);
            var steeringRng = new FixedRandom(20);

            var llm = new SteeringLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

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

            // Steering diff goes picked → picked+question
            Assert.Equal(PickedOption, diffs[steeringIdx].Before);
            Assert.Contains(result.Steering.SteeringQuestion!, diffs[steeringIdx].After);

            // Tier diff goes picked+question → committed (deterministic overlay output)
            Assert.Equal(diffs[steeringIdx].After, diffs[tierIdx].Before);
            Assert.Equal(
                DeliveryOverlay.Apply(diffs[steeringIdx].After, result.Roll.Tier, result.Roll.MissMargin),
                diffs[tierIdx].After);
        }

        /// <summary>
        /// AC: When steering FAILS, the pipeline behaves as before: no steering
        /// diff, no question appended; the committed line is the plain picked
        /// option degraded by the tier overlay (if the roll failed).
        /// </summary>
        [Fact]
        public async Task Steering_failure_no_steering_diff_no_question_appended()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 2, wit: 2, sa: 2));
            var datee = MakeProfile("Brick", MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 3, wit: 3, sa: 3));

            var dice = new FixedDice(1, 15, 50);
            // Steering: roll 1 → miss
            var steeringRng = new FixedRandom(1);

            var llm = new SteeringLlm();
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: steeringRng);
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Steering.SteeringSucceeded);
            Assert.Null(result.Steering.SteeringQuestion);

            // No steering question was folded in: the committed line is the PLAIN
            // picked option run through the deterministic overlay for whatever
            // tier the roll produced (no steering text anywhere).
            string expected = DeliveryOverlay.Apply(PickedOption, result.Roll.Tier, result.Roll.MissMargin);
            Assert.Equal(expected, result.DeliveredMessage);

            if (result.TextDiffs != null)
            {
                Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Steering");
            }
        }

        /// <summary>
        /// LLM stub providing dialogue options, a steering question, and a datee
        /// reply. #1125: there is no delivery LLM call to stub — degradation is
        /// the engine's deterministic <see cref="DeliveryOverlay"/>. Implements
        /// IStatefulLlmAdapter so steering's "is the adapter stateful?" check
        /// passes and a steering question is generated.
        /// </summary>
        private sealed class SteeringLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                System.Threading.CancellationToken ct = default)
                => Task.FromResult(new StatefulDateeResult(
                    new DateeResponse("..."),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User(string.Empty),
                        ConversationMessage.Assistant("..."),
                    }));

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, PickedOption),
                    new DialogueOption(StatType.Rizz, "Nice"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Honesty, "Real talk")
                });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context, System.Threading.CancellationToken ct = default)
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

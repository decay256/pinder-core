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
    [Trait("Category", "Core")]
    public class Issue840_NoLengthClampTests
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

        [Fact]
        public async Task Long_message_delivered_intact_without_length_clamp()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 5, wit: 5, sa: 5));
            var datee = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // Generate a 120-word string
            var longWords = Enumerable.Range(1, 120).Select(i => $"word{i}").ToArray();
            string longMessage = string.Join(" ", longWords);

            // Game dice & steering RNG (steering fails so we only test the raw message delivery)
            var dice = new FixedDice(1, 15, 50); // d20=15 (success)
            var steeringRng = new FixedRandom(1); // fail steering

            var llm = new CapturingLlmWithLongOutput(longMessage);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(), 
                steeringRng: steeringRng, 
                maxDeliveryWords: 80 // Max words is 80, but message is 120!
            );
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Assertions
            Assert.Equal(longMessage, result.DeliveredMessage);
            Assert.True(result.TextDiffs == null || !result.TextDiffs.Any(d => d.LayerName == "LengthClamp"), "Should not emit LengthClamp text diff.");
            
            // Verify word count is fully intact
            var resultWordCount = result.DeliveredMessage.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.Equal(120, resultWordCount);
        }

        [Fact]
        public async Task Steering_question_appended_even_when_intended_text_exceeds_max_delivery_words()
        {
            var player = MakeProfile("Sable", MakeStats(charm: 5, wit: 5, sa: 5));
            var datee = MakeProfile("Brick", MakeStats(charm: 0, rizz: 0, honesty: 0, chaos: 0, wit: 0, sa: 0));

            // Intended text is 100 words (exceeds maxDeliveryWords of 80)
            var longWords = Enumerable.Range(1, 100).Select(i => $"word{i}").ToArray();
            string intendedText = string.Join(" ", longWords);

            // Steering succeeds (roll 20)
            var dice = new FixedDice(1, 15, 50);
            var steeringRng = new FixedRandom(20);

            var llm = new CapturingLlmWithLongOutput(intendedText, "how about now?");
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(), 
                steeringRng: steeringRng, 
                maxDeliveryWords: 80
            );
            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Assert steering succeeded
            Assert.True(result.Steering.SteeringSucceeded);
            Assert.Equal("how about now?", result.Steering.SteeringQuestion);
            Assert.True(result.Roll.IsSuccess);

            // #1125: no DeliveryContext to capture — a SUCCESS roll commits the
            // combined line verbatim, so the steering question must be present in
            // the COMMITTED line even though the picked line already exceeded
            // maxDeliveryWords (no length clamp, no truncation).
            Assert.Contains("how about now?", result.DeliveredMessage);
            Assert.StartsWith("word1", result.DeliveredMessage);
        }

        private sealed class CapturingLlmWithLongOutput : ILlmAdapter, IStatefulLlmAdapter
        {
            private readonly string _longMessage;
            private readonly string _steeringQuestion;

            public CapturingLlmWithLongOutput(string longMessage, string steeringQuestion = "how about now?")
            {
                _longMessage = longMessage;
                _steeringQuestion = steeringQuestion;
            }

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                System.Collections.Generic.IReadOnlyList<ConversationMessage> history,
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
                    new DialogueOption(StatType.Charm, _longMessage),
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
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => Task.FromResult(message);

        public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default) => Task.FromResult(context.DeliveredMessage);

            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default) => Task.FromResult("question?");
        public Task<string> GetSteeringQuestionAsync(SteeringContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(_steeringQuestion);
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
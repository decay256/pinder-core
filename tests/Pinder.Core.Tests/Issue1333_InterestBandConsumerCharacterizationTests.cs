using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public sealed class Issue1333_InterestBandConsumerCharacterizationTests
    {
        [Theory]
        [InlineData(0, InterestState.Unmatched)]
        [InlineData(1, InterestState.Bored)]
        [InlineData(4, InterestState.Bored)]
        [InlineData(5, InterestState.Lukewarm)]
        [InlineData(9, InterestState.Lukewarm)]
        [InlineData(10, InterestState.Interested)]
        [InlineData(15, InterestState.Interested)]
        [InlineData(16, InterestState.VeryIntoIt)]
        [InlineData(20, InterestState.VeryIntoIt)]
        [InlineData(21, InterestState.AlmostThere)]
        [InlineData(24, InterestState.AlmostThere)]
        [InlineData(25, InterestState.DateSecured)]
        public void InterestMeter_CanonicalFallbackBoundaries_AreCurrentBehavior(
            int interest,
            InterestState expected)
        {
            Assert.Equal(expected, new InterestMeter(interest).GetState());
        }

        [Fact]
        public async Task DateeContext_AfterShadowAndHorniness_CarriesFinalCurrentInterestAndFinalInterestAfter()
        {
            var playerStats = MakeStats(allStats: 5, despair: 10);
            var dateeStats = MakeStats(allStats: 0);
            var player = MakeProfile("Player", playerStats);
            var datee = MakeProfile("Datee", dateeStats);
            var adapter = new CapturingAdapter();

            var session = new GameSession(
                player,
                datee,
                adapter,
                new FixedDice(
                    5,  // constructor horniness roll: session horniness > 0
                    20, // main option roll: nat 20, positive roll-stage interest delta
                    50  // DATEE timing roll
                ),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    startingInterest: 10,
                    playerShadows: new SessionShadowTracker(playerStats),
                    steeringRng: new QueuedRandom(1, 1, 1),
                    statDeliveryInstructions: LoadDeliveryInstructions()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(1);

            Assert.True(result.ShadowCheck.OverlayApplied);
            Assert.True(result.HorninessCheck.IsMiss);
            Assert.Equal(0, result.InterestDelta);
            Assert.Equal(result.StateAfter.Interest, adapter.LastDateeContext!.CurrentInterest);

            Assert.Equal(10, adapter.LastDateeContext.InterestBefore);
            Assert.Equal(adapter.LastDateeContext.CurrentInterest, adapter.LastDateeContext.InterestAfter);
            Assert.Equal(new InterestMeter(adapter.LastDateeContext.CurrentInterest).GetState(), adapter.LastDateeContext.InterestAfterState);
        }

        private static StatBlock MakeStats(int allStats, int despair = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                [StatType.Charm] = allStats,
                [StatType.Rizz] = allStats,
                [StatType.Honesty] = allStats,
                [StatType.Chaos] = allStats,
                [StatType.Wit] = allStats,
                [StatType.SelfAwareness] = allStats
            };
            var shadows = new Dictionary<ShadowStatType, int>
            {
                [ShadowStatType.Madness] = 0,
                [ShadowStatType.Despair] = despair,
                [ShadowStatType.Denial] = 0,
                [ShadowStatType.Fixation] = 0,
                [ShadowStatType.Dread] = 0,
                [ShadowStatType.Overthinking] = 0
            };
            return new StatBlock(stats, shadows);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: stats,
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private static StatDeliveryInstructions LoadDeliveryInstructions()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "data", "delivery-instructions.yaml");
                if (File.Exists(candidate))
                    return StatDeliveryInstructions.LoadFrom(File.ReadAllText(candidate));
                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException("Could not locate data/delivery-instructions.yaml.");
        }

        private sealed class CapturingAdapter : ILlmAdapter, IStatefulLlmAdapter
        {
            public DateeContext? LastDateeContext { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "warm opener"),
                    new DialogueOption(StatType.Rizz, "magnetic line"),
                    new DialogueOption(StatType.Honesty, "honest line"),
                    new DialogueOption(StatType.Wit, "witty line")
                });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
            {
                LastDateeContext = context;
                return Task.FromResult(new DateeResponse("noted"));
            }

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                LastDateeContext = context;
                return Task.FromResult(new StatefulDateeResult(
                    new DateeResponse("noted"),
                    Array.Empty<ConversationMessage>()));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message + " [shadow]");

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
                => Task.FromResult(context.DeliveredMessage);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult("steering?");

            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default)
                => Task.FromResult("too much?");
        }

        private sealed class QueuedRandom : Random
        {
            private readonly Queue<int> _values;

            public QueuedRandom(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public override int Next(int minValue, int maxValue)
            {
                if (_values.Count == 0)
                    return minValue;

                int value = _values.Dequeue();
                return value >= minValue && value < maxValue ? value : minValue;
            }

            public override int Next(int maxValue)
            {
                if (_values.Count == 0)
                    return 0;

                int value = _values.Dequeue();
                return value >= 0 && value < maxValue ? value : 0;
            }

            public override int Next() => _values.Count == 0 ? 0 : _values.Dequeue();
        }
    }
}

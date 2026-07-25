using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public sealed class Issue1334_RelationshipStatePropagationTests
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
        public void ContextDefaultTypedStates_MatchCanonicalBoundaries(
            int interest,
            InterestState expected)
        {
            var dialogue = new DialogueContext(
                "player",
                "datee",
                Array.Empty<(string Sender, string Text)>(),
                "",
                Array.Empty<string>(),
                interest);

            var datee = new DateeContext(
                "datee",
                Array.Empty<(string Sender, string Text)>(),
                "",
                Array.Empty<string>(),
                interest,
                "delivered",
                interest,
                interest,
                0);

            Assert.Equal(expected, dialogue.CurrentInterestState);
            Assert.Equal(expected, datee.InterestBeforeState);
            Assert.Equal(expected, datee.InterestAfterState);
        }

        [Fact]
        public async Task ResolverFirstTypedState_IsPassedToDialogueAndDateeContexts()
        {
            var rules = new DisagreeingInterestRules();
            var adapter = new CapturingAdapter();
            var session = new GameSession(
                MakeProfile("Player", TestHelpers.MakeStatBlock(5)),
                MakeProfile("Datee", TestHelpers.MakeStatBlock(0)),
                adapter,
                new FixedDice(
                    0,  // constructor horniness
                    2,  // resolver says interest 10 is Bored; avoid ghosting
                    20, // roll
                    50  // timing
                ),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    startingInterest: 10,
                    rules: rules));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.NotNull(adapter.LastDialogueContext);
            Assert.Equal(10, adapter.LastDialogueContext!.CurrentInterest);
            Assert.Equal(InterestState.Bored, adapter.LastDialogueContext.CurrentInterestState);

            Assert.NotNull(adapter.LastDateeContext);
            Assert.Equal(10, adapter.LastDateeContext!.InterestBefore);
            Assert.Equal(12, adapter.LastDateeContext.InterestAfter);
            Assert.Equal(12, adapter.LastDateeContext.CurrentInterest);
            Assert.Equal(InterestState.Bored, adapter.LastDateeContext.InterestBeforeState);
            Assert.Equal(InterestState.Interested, adapter.LastDateeContext.InterestAfterState);
        }

        [Fact]
        public async Task PostRollShadowAndHorninessMutation_ReplacesStaleRollStageState()
        {
            var playerStats = MakeStats(allStats: 5, despair: 10);
            var adapter = new CapturingAdapter();
            var session = new GameSession(
                MakeProfile("Player", playerStats),
                MakeProfile("Datee", TestHelpers.MakeStatBlock(0)),
                adapter,
                new FixedDice(
                    5,  // constructor horniness: enables horniness miss
                    20, // main roll: nat 20 creates a positive roll-stage delta
                    50  // timing
                ),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    startingInterest: 15,
                    playerShadows: new SessionShadowTracker(playerStats),
                    steeringRng: new QueuedRandom(1, 1, 1),
                    statDeliveryInstructions: LoadDeliveryInstructions()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(1);

            Assert.True(result.ShadowCheck.OverlayApplied);
            Assert.True(result.HorninessCheck.IsMiss);
            Assert.Equal(0, result.InterestDelta);

            Assert.NotNull(adapter.LastDateeContext);
            Assert.Equal(15, adapter.LastDateeContext!.InterestBefore);
            Assert.Equal(15, adapter.LastDateeContext.InterestAfter);
            Assert.Equal(15, adapter.LastDateeContext.CurrentInterest);
            Assert.Equal(InterestState.Interested, adapter.LastDateeContext.InterestBeforeState);
            Assert.Equal(InterestState.Interested, adapter.LastDateeContext.InterestAfterState);
            Assert.Equal(result.StateAfter.Interest, adapter.LastDateeContext.InterestAfter);
            Assert.Equal(result.StateAfter.State, adapter.LastDateeContext.InterestAfterState);
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

        private static StatBlock MakeStats(int allStats, int despair)
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

        private sealed class DisagreeingInterestRules : IRuleResolver
        {
            public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => 0;
            public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => 0;
            public InterestState? GetInterestState(int interest)
                => interest == 10 ? InterestState.Bored : (InterestState?)null;
            public int? GetShadowThresholdLevel(int shadowValue) => null;
            public int? GetMomentumBonus(int streak) => null;
            public double? GetRiskTierXpMultiplier(RiskTier riskTier) => null;
            public double? GetTerminalOutcomeMultiplier(GameOutcome outcome) => null;
            public int? GetSuccessBaseXp(int dc) => null;
            public SuccessDcLabelThresholds? GetSuccessDcLabelThresholds() => null;
            public int? GetFlatXpAward(string awardType) => null;
            public int? GetXpThresholdForLevel(int level) => null;
            public int? GetLevelRollBonus(int level) => 0;
            public int? GetBuildPointsForLevel(int level) => null;
            public int? GetItemSlotsForLevel(int level) => null;
            public int? GetFailurePoolTierMinLevel(string tierName) => null;
            public int? GetProgressionCurrencyPerXp() => 10;
            public bool AllowDefaultFallback => true;
        }

        private sealed class CapturingAdapter : NullLlmAdapter
        {
            public DialogueContext? LastDialogueContext { get; private set; }
            public DateeContext? LastDateeContext { get; private set; }

            public override Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                LastDialogueContext = context;
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "warm opener"),
                    new DialogueOption(StatType.Rizz, "magnetic line"),
                    new DialogueOption(StatType.Honesty, "honest line"),
                    new DialogueOption(StatType.Wit, "witty line")
                });
            }

            public override Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                LastDateeContext = context;
                return Task.FromResult(new StatefulDateeResult(
                    new DateeResponse("noted"),
                    Array.Empty<ConversationMessage>()));
            }

            public override Task<string> ApplyShadowCorruptionAsync(
                string message,
                string instruction,
                ShadowStatType shadow,
                string? archetypeDirective = null,
                CancellationToken ct = default)
                => Task.FromResult(message + " [shadow]");

            public override Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult("steering?");

            public override Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default)
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
        }
    }
}

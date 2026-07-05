using System;
using System.Collections.Generic;
using System.Threading;
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
    public class Issue1207_Nat20ImprovementTests
    {
        private const string PickedLine = "This is a carefully crafted charming statement.";

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private sealed class DeliveryAdapterWithSuccessImprovement : ILlmAdapter, IStatefulLlmAdapter
        {
            private readonly string _optionText;

            public DeliveryAdapterWithSuccessImprovement(string optionText)
            {
                _optionText = optionText;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
                => Task.FromResult(new[] { new DialogueOption(StatType.Charm, _optionText) });

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("ok, go on..."));

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context, IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken = default)
            {
                var resp = new DateeResponse("ok, go on...");
                var entries = new[] { ConversationMessage.User(string.Empty), ConversationMessage.Assistant("ok, go on...") };
                return Task.FromResult(new StatefulDateeResult(resp, entries));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default) => Task.FromResult("question?");
        public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult("so... when are we actually doing this?");

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
            {
                return Task.FromResult(context.DeliveredMessage + $" [IMPROVED:{context.TierKey}]");
            }
        
        public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(message);
        }
}

        private static GameSession NewSession(ILlmAdapter llm, IDiceRoller dice, int playerStats = 2, int dateeStats = 2)
            => new GameSession(
                MakeProfile("Player", playerStats), MakeProfile("Datee", dateeStats),
                llm, dice, new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: new AlwaysMinRandom()));

        private sealed class AlwaysMinRandom : Random
        {
            public override int Next(int minValue, int maxValue) => minValue;
            public override int Next(int maxValue) => 0;
            public override int Next() => 0;
        }

        [Fact]
        public async Task Nat20_ImprovesDeliveredMessage_AndEmitsNat20Diff()
        {
            // Nat20 setup. Datee 0 stats -> DC 16. Player 2 stats. d20 = 20. Total = 22.
            // Result is success, Nat20 = true.
            var dice = new FixedDice(5, 20, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 2, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Contains("[IMPROVED:nat20]", result.DeliveredMessage);
            Assert.Contains(result.TextDiffs, d => d.LayerName == "Nat 20");
        }

        [Fact]
        public async Task Nat20_TierKeyIsNat20_NotExceptional()
        {
            // High player stats so margin would be exceptional if it weren't Nat20.
            // Datee 0 stats -> DC 16. Player 20 stats. d20 = 20. Total = 40.
            // Margin = 24.
            var dice = new FixedDice(5, 20, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 20, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Contains("[IMPROVED:nat20]", result.DeliveredMessage);
            Assert.DoesNotContain("[IMPROVED:exceptional]", result.DeliveredMessage);
            Assert.Contains(result.TextDiffs, d => d.LayerName == "Nat 20");
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Legendary success");
        }

        [Fact]
        public async Task PlainStrongSuccess_DoesNotEmitNat20Diff()
        {
            // Margin success, NOT Nat20.
            // Datee 0 stats -> DC 16. Player 5 stats. d20 = 16. Total = 21. Margin = 5. (Strong).
            var dice = new FixedDice(5, 16, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 5, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.False(result.Roll.IsNatTwenty);
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Nat 20");
            // Optional: assert strong success presence
            Assert.Contains(result.TextDiffs, d => d.LayerName == "Strong success");
        }

        [Fact]
        public async Task PlainFailure_DoesNotEmitNat20Diff()
        {
            // Failure.
            // Datee 0 stats -> DC 16. Player 2 stats. d20 = 2. Total = 4. Failure.
            var dice = new FixedDice(5, 2, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 2, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Nat 20");
        }
    }
}

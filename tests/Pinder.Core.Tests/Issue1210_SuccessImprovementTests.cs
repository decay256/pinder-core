using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1210_SuccessImprovementTests
    {
        private const string PickedLine = "That sounds like a perfectly reasonable answer to me.";

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
                // Appends a marker for assertion
                return Task.FromResult(context.DeliveredMessage + $" [IMPROVED:{context.TierKey}]");
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
        public async Task StrongSuccess_ImprovesDeliveredMessage_AndEmitsTextDiffLayer()
        {
            // Strong margin 5..14. 
            // If datee has 0 stats, DC is 16+0=16. Player has 5 stats. Mod = 5. d20 = 16. Total = 21.
            // Margin = 21 - 16 = 5. Not a Nat20 (16).
            var dice = new FixedDice(5, 16, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 5, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Contains("[IMPROVED:strong]", result.DeliveredMessage);
            Assert.Contains(result.TextDiffs, d => d.LayerName == "Strong success");
        }

        [Fact]
        public async Task ExceptionalSuccess_ImprovesDeliveredMessage_AndEmitsTextDiffLayer()
        {
            // Exceptional margin >= 15.
            // If datee has 0 stats, DC is 16+0=16. Player has 20 stats. Mod = 20. d20 = 19. Total = 39.
            // Margin = 39 - 16 = 23. Not a Nat20 (19).
            var dice = new FixedDice(5, 19, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 20, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Contains("[IMPROVED:exceptional]", result.DeliveredMessage);
            Assert.Contains(result.TextDiffs, d => d.LayerName == "Legendary success");
        }

        [Fact]
        public async Task CleanSuccess_DoesNotImproveMessage_AndStaysVerbatim()
        {
            // Clean margin < 5.
            // If datee has 0 stats, DC is 16. Player has 2 stats. Mod = 2. d20 = 14. Total = 16.
            // Margin = 16 - 16 = 0.
            var dice = new FixedDice(5, 14, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement(PickedLine);
            var session = NewSession(llm, dice, playerStats: 2, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(PickedLine, result.DeliveredMessage);
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Strong success");
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Legendary success");
        }

        [Fact]
        public async Task ExceptionalSuccess_WithEllipsisOption_DoesNotReturnEllipsisAsFinalMessage()
        {
            // Even if somehow the option makes it as "...", Legendary shouldn't return "..."
            // "If the chosen line somehow reaches delivery as '...', the engine does not silently return '...' as the final delivered message on Legendary success."
            // We can test this by providing "..." as the raw picked line.
            var dice = new FixedDice(5, 19, 50);
            var llm = new DeliveryAdapterWithSuccessImprovement("...");
            var session = NewSession(llm, dice, playerStats: 20, dateeStats: 0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // It might fallback to the pad line or improve it, but it should NOT be "..."
            Assert.NotEqual("...", result.DeliveredMessage.Trim());
        }
    }
}

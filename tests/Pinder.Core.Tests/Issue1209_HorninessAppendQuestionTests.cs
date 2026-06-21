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
    /// <summary>
    /// Contract tests for #1209 — Horniness becomes a steering-style append-one-horny-question mechanic.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1209_HorninessAppendQuestionTests
    {
        private const string PickedLine = "Genuinely enjoying this conversation with you so far.";
        private const string AppendedQuestion = "wanna get out of here? [HORNYQ]";
        private const string AppendedSteering = "so... when are we actually doing this? [STEERINGQ]";

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private sealed class HorninessFakeAdapter : ILlmAdapter, IStatefulLlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
                => Task.FromResult(new[] { new DialogueOption(StatType.Charm, PickedLine) });

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

            public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
                => Task.FromResult(context.DeliveredMessage);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult(AppendedSteering);

            // THE NEW HOOK FOR #1209
            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default)
                => Task.FromResult(AppendedQuestion);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);
        }

        // Shared RNGs for predictable checks
        private sealed class AlwaysMinRandom : Random
        {
            public override int Next(int minValue, int maxValue) => minValue;
            public override int Next(int maxValue) => 0;
            public override int Next() => 0;
        }

        private sealed class AlwaysMaxRandom : Random
        {
            public override int Next(int minValue, int maxValue) => maxValue - 1;
            public override int Next(int maxValue) => maxValue - 1;
            public override int Next() => int.MaxValue;
        }

        private static GameSession MakeSession(int sessionHorniness, Random steeringRng, int mainRoll)
        {
            // Dice: index0 = horniness d10 (determines sessionHorniness via clock mod 0), then main d20, then d100.
            var dice = new FixedDice(sessionHorniness, mainRoll, 50);
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var clock = TestHelpers.MakeClock(); 
            var config = new GameSessionConfig(
                clock: clock,
                playerShadows: shadows,
                steeringRng: steeringRng,
                startingInterest: 10); // positive interest

            return new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                new HorninessFakeAdapter(), dice, new NullTrapRegistry(), config);
        }

        [Fact]
        public async Task HorninessMiss_AppendsOneQuestion_DoesNotRewritePrefix()
        {
            // mainRoll = 16 (clean success, low margin so SuccessImprovement doesn't trigger)
            var session = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMinRandom(), mainRoll: 16);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Miss logic
            Assert.True(result.HorninessCheck.IsMiss);
            
            // Should contain original prefix
            Assert.StartsWith(PickedLine, result.DeliveredMessage);
            Assert.EndsWith(AppendedQuestion, result.DeliveredMessage.TrimEnd());

            // Should contain a Horniness TextDiff
            var diff = Assert.Single(result.TextDiffs, d => d.LayerName == "Horniness");
            Assert.StartsWith(diff.Before, diff.After); // Append only
            Assert.EndsWith(AppendedQuestion, diff.After.TrimEnd());
        }

        [Fact]
        public async Task HorninessMiss_NoInterestPenalty()
        {
            // We want positive interest delta. mainRoll = 18 ensures success.
            var sessionMiss = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMinRandom(), mainRoll: 18);
            await sessionMiss.StartTurnAsync();
            var resultMiss = await sessionMiss.ResolveTurnAsync(0);

            Assert.True(resultMiss.HorninessCheck.IsMiss);
            Assert.Equal(0, resultMiss.HorninessInterestPenalty);

            // No horniness breakdown item
            Assert.DoesNotContain(resultMiss.InterestBreakdown, b => 
                b.Source.Contains("horniness", StringComparison.OrdinalIgnoreCase) ||
                b.Label.Contains("horniness", StringComparison.OrdinalIgnoreCase));

            // Compare to control (success horniness)
            var sessionSuccess = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMaxRandom(), mainRoll: 18);
            await sessionSuccess.StartTurnAsync();
            var resultSuccess = await sessionSuccess.ResolveTurnAsync(0);

            Assert.False(resultSuccess.HorninessCheck.IsMiss);
            
            // Both turns should have exact same interest delta (horniness no longer halves it)
            Assert.Equal(resultSuccess.InterestDelta, resultMiss.InterestDelta);
        }

        [Fact]
        public async Task HorninessSuccess_NoQuestionAppended()
        {
            var session = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMaxRandom(), mainRoll: 16);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.HorninessCheck.IsMiss);
            
            Assert.DoesNotContain("Horniness", result.TextDiffs.Select(d => d.LayerName));
            Assert.DoesNotContain(AppendedQuestion, result.DeliveredMessage);
        }

        [Fact]
        public async Task Steering_StillWorks_Independently()
        {
            // steeringRng returns max -> steering succeeds. 
            var session = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMaxRandom(), mainRoll: 16);
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Steering.SteeringSucceeded, "Steering should succeed");
            Assert.Contains(AppendedSteering, result.DeliveredMessage);
            
            var diff = Assert.Single(result.TextDiffs, d => d.LayerName == "Steering");
            Assert.Contains(AppendedSteering, diff.After);
        }
    }
}

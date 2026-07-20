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
using System.IO;
using Pinder.LlmAdapters;
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
            return TestHelpers.MakeCharacterProfile(
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
        
        public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return Task.FromResult(message);
        }
}

        private sealed class ThrowingHorninessAdapter : ILlmAdapter, IStatefulLlmAdapter
        {
            private readonly Exception _toThrow;

            public ThrowingHorninessAdapter(Exception toThrow)
            {
                _toThrow = toThrow;
            }

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

            // THE HOOK UNDER TEST — throws instead of returning a question.
            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default)
                => throw _toThrow;

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);
        }

        private static GameSession MakeSessionWithAdapter(
            int sessionHorniness, Random steeringRng, int mainRoll, ILlmAdapter adapter,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            var dice = new FixedDice(sessionHorniness, mainRoll, 50);
            var shadows = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var clock = TestHelpers.MakeClock();
            var config = new GameSessionConfig(
                clock: clock,
                playerShadows: shadows,
                steeringRng: steeringRng,
                statDeliveryInstructions: LoadDeliveryInstructions(),
                startingInterest: 10,
                onDiagnostic: onDiagnostic);

            return new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                adapter, dice, new NullTrapRegistry(), config);
        }

        [Fact]
        public async Task HorninessMiss_QuestionThrowsNonCancellation_EmitsDiagnosticAndFallsBackToNull()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var adapter = new ThrowingHorninessAdapter(new InvalidOperationException("adapter contract drift"));
            var session = MakeSessionWithAdapter(
                sessionHorniness: 10, steeringRng: new AlwaysMinRandom(), mainRoll: 16, adapter: adapter,
                onDiagnostic: diagnostics.Add);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Miss logic still fires, but the question generation blew up.
            Assert.True(result.HorninessCheck.IsMiss);

            // Fallback preserved: no question appended, no Horniness TextDiff layer.
            Assert.DoesNotContain("Horniness", result.TextDiffs.Select(d => d.LayerName));
            Assert.Equal(PickedLine, result.DeliveredMessage.TrimEnd());

            // But the failure is now observable via the diagnostic callback.
            var diag = Assert.Single(diagnostics, d => d.EventName == "HorninessQuestionFailure");
            Assert.Equal("DeliveryStage", diag.Source);
            Assert.Equal(OperationalDiagnosticSeverity.Warning, diag.Severity);
            Assert.IsType<InvalidOperationException>(diag.Exception);
            Assert.Contains("adapter contract drift", diag.Message);
        }

        [Fact]
        public async Task HorninessMiss_QuestionThrowsCancellation_PropagatesAndEmitsNoDiagnostic()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var adapter = new ThrowingHorninessAdapter(new OperationCanceledException(cts.Token));
            var session = MakeSessionWithAdapter(
                sessionHorniness: 10, steeringRng: new AlwaysMinRandom(), mainRoll: 16, adapter: adapter,
                onDiagnostic: diagnostics.Add);

            await session.StartTurnAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.ResolveTurnAsync(0, progress: null, ct: cts.Token));

            // Cancellation must propagate untouched — no diagnostic swallowing/logging of cancellation.
            Assert.Empty(diagnostics);
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

        private static StatDeliveryInstructions LoadDeliveryInstructions()
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
            string fallback = Path.Combine("/root/.openclaw/workspace/pinder-core", "data", "delivery-instructions.yaml");
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
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
                statDeliveryInstructions: LoadDeliveryInstructions(),
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
        public async Task HorninessMiss_HalvesInterestPenalty()
        {
            // We want positive interest delta. mainRoll = 18 ensures success.
            var sessionMiss = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMinRandom(), mainRoll: 18);
            await sessionMiss.StartTurnAsync();
            var resultMiss = await sessionMiss.ResolveTurnAsync(0);

            Assert.True(resultMiss.HorninessCheck.IsMiss);
            int pre = resultMiss.InterestDelta - resultMiss.HorninessInterestPenalty;
            Assert.True(pre > 0, "Expected a positive pre-penalty interest delta");
            Assert.Equal((int)Math.Floor(pre / 2.0) - pre, resultMiss.HorninessInterestPenalty);

            // Horniness breakdown item should exist and match penalty
            var horninessItem = Assert.Single(resultMiss.InterestBreakdown, b => b.Source.Contains("horniness", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(resultMiss.HorninessInterestPenalty, horninessItem.Delta);

            // Compare to control (success horniness)
            var sessionSuccess = MakeSession(sessionHorniness: 10, steeringRng: new AlwaysMaxRandom(), mainRoll: 18);
            await sessionSuccess.StartTurnAsync();
            var resultSuccess = await sessionSuccess.ResolveTurnAsync(0);

            Assert.False(resultSuccess.HorninessCheck.IsMiss);
            
            // Both turns start with the same base positive delta, but Miss gets halved
            Assert.True(resultMiss.InterestDelta < resultSuccess.InterestDelta);
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

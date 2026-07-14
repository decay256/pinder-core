using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;

namespace Pinder.Core.Tests.Conversation
{
    [Collection("GameSession")]
    [Trait("Category", "Core")]
    public class DateeResponseStageTests
    {
        // Simple mock dice roller
        private sealed class SimpleDiceRoller : IDiceRoller
        {
            private readonly int _value;
            public SimpleDiceRoller(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        // Mock Progress for progress verification
        private sealed class MockProgress : IProgress<TurnProgressEvent>
        {
            public List<TurnProgressEvent> Events { get; } = new List<TurnProgressEvent>();
            public void Report(TurnProgressEvent value) => Events.Add(value);
        }

        // Pure stateless LLM adapter (implements ILlmAdapter but NOT IStatefulLlmAdapter)
        private class PureStatelessMockLlm : ILlmAdapter
        {
            private readonly string _response;
            public PureStatelessMockLlm(string response) => _response = response;

            public virtual Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
            {
                return Task.FromResult(new DateeResponse(_response));
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default) => Task.FromResult(Array.Empty<DialogueOption>());
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default) => Task.FromResult<string?>(null);
            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default) => Task.FromResult(string.Empty);
            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default) => Task.FromResult(string.Empty);
            public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default) => Task.FromResult(context.DeliveredMessage);
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
            public Task<string> ApplyFailureCorruptionAsync(string message, string instruction, StatType stat, FailureTier tier, string? archetypeDirective = null, CancellationToken ct = default) => Task.FromResult(message);
        }

        private sealed class CancelingLlm : PureStatelessMockLlm
        {
            public CancelingLlm() : base("unused")
            {
            }

            public override Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
            {
                throw new OperationCanceledException("cancelled");
            }
        }

        // Fake LLM adapter for stateful responses (inherits from NullLlmAdapter to prevent interface drift)
        private sealed class StatefulMockLlm : NullLlmAdapter
        {
            private readonly string _response;
            private readonly ConversationMessage[] _entries;
            public StatefulMockLlm(string response, ConversationMessage[] entries)
            {
                _response = response;
                _entries = entries;
            }

            public override Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                var resp = new DateeResponse(_response);
                return Task.FromResult(new StatefulDateeResult(resp, _entries));
            }
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(10, 0.2f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public async Task ExecuteAsync_StatelessAdapter_ComputesDelayAndGetsResponse()
        {
            // Arrange
            var mockLlm = new PureStatelessMockLlm("Datee Reply text");
            var stage = new DateeResponseStage(mockLlm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);

            var rollStageResult = new RollStageResult
            {
                ResolveDice = new SimpleDiceRoller(50),
                InterestBefore = 10,
                InterestAfter = 12,
                RollResult = new RollResult(
                    dieRoll: 10,
                    secondDieRoll: null,
                    usedDieRoll: 10,
                    stat: StatType.Charm,
                    statModifier: 0,
                    levelBonus: 0,
                    dc: 10,
                    tier: FailureTier.Success)
            };

            var deliveryStageResult = new DeliveryStageResult
            {
                DeliveredMessage = "Hello!",
                HorninessCheckResult = HorninessCheckResult.NotPerformed
            };

            var player = MakeProfile("Player");
            var datee = MakeProfile("Datee");
            var progress = new MockProgress();

            // Act
            var result = await stage.ExecuteAsync(
                state,
                rollStageResult,
                deliveryStageResult,
                player,
                datee,
                progress,
                CancellationToken.None);

            // Assert
            Assert.Equal("Datee Reply text", result.DateeMessage);
            Assert.Equal("Datee Reply text", result.DateeResponse.MessageText);
            Assert.True(result.ResponseDelayMinutes >= 1);
            Assert.Equal(2, progress.Events.Count);
            Assert.Equal(TurnProgressStage.DateeResponseStarted, progress.Events[0].Stage);
            Assert.Equal(TurnProgressStage.DateeResponseCompleted, progress.Events[1].Stage);
            Assert.Equal("Datee Reply text", progress.Events[1].Text);
        }

        [Fact]
        public async Task ExecuteAsync_DiagnosticsEmitStartThenOneTerminal()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var stage = new DateeResponseStage(
                new PureStatelessMockLlm("Datee Reply text"),
                diagnostics.Add);
            var state = new GameSessionState { Interest = new InterestMeter(10) };

            await stage.ExecuteAsync(
                state,
                MakeRollStageResult(),
                new DeliveryStageResult
                {
                    DeliveredMessage = "Hello!",
                    HorninessCheckResult = HorninessCheckResult.NotPerformed
                },
                MakeProfile("Player"),
                MakeProfile("Datee"),
                null,
                CancellationToken.None);

            Assert.Equal(2, diagnostics.Count);
            Assert.Equal(OperationalDiagnosticLifecycle.Start, diagnostics[0].Lifecycle);
            Assert.Equal(OperationalDiagnosticOperationKind.DateeResponse, diagnostics[0].OperationKind);
            Assert.Equal(OperationalDiagnosticLifecycle.Terminal, diagnostics[1].Lifecycle);
            Assert.Equal(OperationalDiagnosticOutcome.Succeeded, diagnostics[1].Outcome);
            Assert.Equal(diagnostics[0].CallId, diagnostics[1].CallId);
            Assert.Single(diagnostics.FindAll(d => d.Lifecycle == OperationalDiagnosticLifecycle.Terminal));
        }

        [Fact]
        public async Task ExecuteAsync_CancellationDiagnosticIsCancelledAndTerminal()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var stage = new DateeResponseStage(new CancelingLlm(), diagnostics.Add);
            var state = new GameSessionState { Interest = new InterestMeter(10) };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                stage.ExecuteAsync(
                    state,
                    MakeRollStageResult(),
                    new DeliveryStageResult
                    {
                        DeliveredMessage = "Hello!",
                        HorninessCheckResult = HorninessCheckResult.NotPerformed
                    },
                    MakeProfile("Player"),
                    MakeProfile("Datee"),
                    null,
                    CancellationToken.None));

            var terminal = Assert.Single(diagnostics.FindAll(d => d.Lifecycle == OperationalDiagnosticLifecycle.Terminal));
            Assert.Equal(OperationalDiagnosticOutcome.Cancelled, terminal.Outcome);
            Assert.Equal(OperationalDiagnosticFailureClassification.Cancelled, terminal.FailureClassification);
        }

        [Fact]
        public async Task ExecuteAsync_StatefulAdapter_PushesNewHistoryEntries()
        {
            // Arrange
            var newEntries = new[]
            {
                ConversationMessage.User("User stateful"),
                ConversationMessage.Assistant("Assistant stateful")
            };
            var mockLlm = new StatefulMockLlm("Stateful response", newEntries);
            var stage = new DateeResponseStage(mockLlm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);

            var rollStageResult = new RollStageResult
            {
                ResolveDice = new SimpleDiceRoller(50),
                InterestBefore = 10,
                InterestAfter = 12,
                RollResult = new RollResult(
                    dieRoll: 10,
                    secondDieRoll: null,
                    usedDieRoll: 10,
                    stat: StatType.Charm,
                    statModifier: 0,
                    levelBonus: 0,
                    dc: 10,
                    tier: FailureTier.Success)
            };

            var deliveryStageResult = new DeliveryStageResult
            {
                DeliveredMessage = "Hello!",
                HorninessCheckResult = HorninessCheckResult.NotPerformed
            };

            var player = MakeProfile("Player");
            var datee = MakeProfile("Datee");

            // Act
            var result = await stage.ExecuteAsync(
                state,
                rollStageResult,
                deliveryStageResult,
                player,
                datee,
                null,
                CancellationToken.None);

            // Assert
            Assert.Equal("Stateful response", result.DateeMessage);
            Assert.Equal(2, state.DateeHistory.Count);
            Assert.Equal("User stateful", state.DateeHistory[0].Content);
            Assert.Equal("Assistant stateful", state.DateeHistory[1].Content);
        }

        private static RollStageResult MakeRollStageResult()
        {
            return new RollStageResult
            {
                ResolveDice = new SimpleDiceRoller(50),
                InterestBefore = 10,
                InterestAfter = 12,
                RollResult = new RollResult(
                    dieRoll: 10,
                    secondDieRoll: null,
                    usedDieRoll: 10,
                    stat: StatType.Charm,
                    statModifier: 0,
                    levelBonus: 0,
                    dc: 10,
                    tier: FailureTier.Success)
            };
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesSpentBackstory_BasedOnResolvedTarget()
        {
            // Arrange
            var mockLlm = new StatefulMockLlm("Dialogue", Array.Empty<ConversationMessage>());
            var stage = new DateeResponseStage(mockLlm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);
            state.CurrentResolvedTarget = new ResolvedRevelationTarget { Registry = "BACKSTORY", Index = 4 };

            var rollStageResult = new RollStageResult
            {
                ResolveDice = new SimpleDiceRoller(50),
                InterestBefore = 10,
                InterestAfter = 12,
                RollResult = new RollResult(
                    dieRoll: 10,
                    secondDieRoll: null,
                    usedDieRoll: 10,
                    stat: StatType.Charm,
                    statModifier: 0,
                    levelBonus: 0,
                    dc: 10,
                    tier: FailureTier.Success)
            };

            var deliveryStageResult = new DeliveryStageResult
            {
                DeliveredMessage = "Hello!",
                HorninessCheckResult = HorninessCheckResult.NotPerformed
            };

            var player = MakeProfile("Player");
            var datee = MakeProfile("Datee");

            // Act
            var result = await stage.ExecuteAsync(
                state,
                rollStageResult,
                deliveryStageResult,
                player,
                datee,
                null,
                CancellationToken.None);

            // Assert
            Assert.Contains(4, state.SpentBackstoryIndices);
            Assert.Empty(state.SpentStakeIndices);
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesSpentStakes_BasedOnResolvedTarget()
        {
            // Arrange
            var mockLlm = new StatefulMockLlm("Dialogue", Array.Empty<ConversationMessage>());
            var stage = new DateeResponseStage(mockLlm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);
            state.CurrentResolvedTarget = new ResolvedRevelationTarget { Registry = "STAKE", Index = 7 };

            var rollStageResult = new RollStageResult
            {
                ResolveDice = new SimpleDiceRoller(50),
                InterestBefore = 10,
                InterestAfter = 12,
                RollResult = new RollResult(
                    dieRoll: 10,
                    secondDieRoll: null,
                    usedDieRoll: 10,
                    stat: StatType.Charm,
                    statModifier: 0,
                    levelBonus: 0,
                    dc: 10,
                    tier: FailureTier.Success)
            };

            var deliveryStageResult = new DeliveryStageResult
            {
                DeliveredMessage = "Hello!",
                HorninessCheckResult = HorninessCheckResult.NotPerformed
            };

            var player = MakeProfile("Player");
            var datee = MakeProfile("Datee");

            // Act
            var result = await stage.ExecuteAsync(
                state,
                rollStageResult,
                deliveryStageResult,
                player,
                datee,
                null,
                CancellationToken.None);

            // Assert
            Assert.Contains(7, state.SpentStakeIndices);
            Assert.Empty(state.SpentBackstoryIndices);
        }

        [Fact]
        public async Task ExecuteAsync_NoTargetResolved_KeepsIndicesEmpty()
        {
            // Arrange
            var mockLlm = new StatefulMockLlm("Dialogue", Array.Empty<ConversationMessage>());
            var stage = new DateeResponseStage(mockLlm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);
            state.CurrentResolvedTarget = null;

            var rollStageResult = new RollStageResult
            {
                ResolveDice = new SimpleDiceRoller(50),
                InterestBefore = 10,
                InterestAfter = 12,
                RollResult = new RollResult(
                    dieRoll: 10,
                    secondDieRoll: null,
                    usedDieRoll: 10,
                    stat: StatType.Charm,
                    statModifier: 0,
                    levelBonus: 0,
                    dc: 10,
                    tier: FailureTier.Success)
            };

            var deliveryStageResult = new DeliveryStageResult
            {
                DeliveredMessage = "Hello!",
                HorninessCheckResult = HorninessCheckResult.NotPerformed
            };

            var player = MakeProfile("Player");
            var datee = MakeProfile("Datee");

            // Act
            var result = await stage.ExecuteAsync(
                state,
                rollStageResult,
                deliveryStageResult,
                player,
                datee,
                null,
                CancellationToken.None);

            // Assert
            Assert.Empty(state.SpentBackstoryIndices);
            Assert.Empty(state.SpentStakeIndices);
        }
    }
}

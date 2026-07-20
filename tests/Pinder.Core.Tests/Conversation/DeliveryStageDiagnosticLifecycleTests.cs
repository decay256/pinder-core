using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    [Trait("Category", "Core")]
    public sealed class DeliveryStageDiagnosticLifecycleTests
    {
        [Fact]
        public async Task ExecuteAsync_Success_EmitsExactlyOneSucceededTerminal()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var stage = CreateStage(diagnostics.Add);

            DeliveryStageResult result = await stage.ExecuteAsync(
                CreateState(),
                new DialogueOption(StatType.Charm, "A clear invitation."),
                CreateSuccessfulRoll(),
                CreateProfile("Player"),
                CreateProfile("Datee"),
                progress: null,
                interestDelta: 1,
                CancellationToken.None);

            Assert.Equal("A clear invitation.", result.DeliveredMessage);
            AssertLifecycle(diagnostics, OperationalDiagnosticOutcome.Succeeded, "DeliverySucceeded");
        }

        [Fact]
        public async Task ExecuteAsync_Exception_EmitsExactlyOneFailedTerminalAndRethrows()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var stage = CreateStage(diagnostics.Add);
            var expected = new InvalidOperationException("progress failed");

            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                stage.ExecuteAsync(
                    CreateState(),
                    new DialogueOption(StatType.Charm, "A clear invitation."),
                    CreateSuccessfulRoll(),
                    CreateProfile("Player"),
                    CreateProfile("Datee"),
                    new ThrowingProgress(expected),
                    interestDelta: 1,
                    CancellationToken.None));

            Assert.Same(expected, actual);
            OperationalDiagnosticEvent terminal = AssertLifecycle(
                diagnostics,
                OperationalDiagnosticOutcome.Failed,
                "DeliveryFailed");
            Assert.Same(expected, terminal.Exception);
            Assert.Equal(OperationalDiagnosticFailureClassification.Permanent, terminal.FailureClassification);
        }

        [Fact]
        public async Task ExecuteAsync_Cancellation_EmitsExactlyOneCancelledTerminalAndRethrows()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var stage = CreateStage(diagnostics.Add);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                stage.ExecuteAsync(
                    CreateState(),
                    new DialogueOption(StatType.Charm, "A clear invitation."),
                    CreateSuccessfulRoll(),
                    CreateProfile("Player"),
                    CreateProfile("Datee"),
                    progress: null,
                    interestDelta: 1,
                    cancellation.Token));

            OperationalDiagnosticEvent terminal = AssertLifecycle(
                diagnostics,
                OperationalDiagnosticOutcome.Cancelled,
                "DeliveryCancelled");
            Assert.IsAssignableFrom<OperationCanceledException>(terminal.Exception);
            Assert.Equal(OperationalDiagnosticFailureClassification.Cancelled, terminal.FailureClassification);
        }

        private static DeliveryStage CreateStage(Action<OperationalDiagnosticEvent> onDiagnostic)
        {
            return new DeliveryStage(
                new NullLlmAdapter(),
                rules: null,
                new SteeringEngine(new Random(1)),
                new HorninessEngine(new Random(2)),
                new ShadowCheckEngine(new Random(3)),
                statDeliveryInstructions: null,
                onTextLayerNoop: null,
                onDiagnostic,
                maxDeliveryWords: 100);
        }

        private static GameSessionState CreateState()
        {
            return new GameSessionState { TurnNumber = 7 };
        }

        private static CharacterProfile CreateProfile(string name)
        {
            return TestHelpers.MakeCharacterProfile(
                TestHelpers.MakeStatBlock(2),
                $"You are {name}.",
                name,
                new TimingProfile(10, 0.2f, 0.0f, "neutral"),
                level: 1);
        }

        private static RollResult CreateSuccessfulRoll()
        {
            return new RollResult(
                dieRoll: 10,
                secondDieRoll: null,
                usedDieRoll: 10,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: 10,
                tier: FailureTier.Success);
        }

        private static OperationalDiagnosticEvent AssertLifecycle(
            IReadOnlyCollection<OperationalDiagnosticEvent> diagnostics,
            OperationalDiagnosticOutcome expectedOutcome,
            string expectedEventName)
        {
            OperationalDiagnosticEvent started = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Source == "DeliveryStage"
                && diagnostic.OperationKind == OperationalDiagnosticOperationKind.Delivery
                && diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Start));
            OperationalDiagnosticEvent terminal = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Source == "DeliveryStage"
                && diagnostic.OperationKind == OperationalDiagnosticOperationKind.Delivery
                && diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Terminal));

            Assert.Equal(expectedEventName, terminal.EventName);
            Assert.Equal(expectedOutcome, terminal.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(started.CallId));
            Assert.Equal(started.CallId, terminal.CallId);
            Assert.Equal("7", terminal.CorrelationHints["turn"]);
            return terminal;
        }

        private sealed class ThrowingProgress : IProgress<TurnProgressEvent>
        {
            private readonly Exception _exception;

            public ThrowingProgress(Exception exception)
            {
                _exception = exception;
            }

            public void Report(TurnProgressEvent value)
            {
                throw _exception;
            }
        }
    }
}

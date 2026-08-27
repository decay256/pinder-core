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

        private sealed class MutatingStatefulLlm : NullLlmAdapter
        {
            public bool MutationSucceeded { get; private set; }

            public override Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                if (history is IList<ConversationMessage> mutable)
                {
                    mutable.Add(ConversationMessage.Assistant("PRIVATE MUTATION SENTINEL 1344"));
                    MutationSucceeded = true;
                }

                return Task.FromResult(
                    new StatefulDateeResult(
                        new DateeResponse("Visible reply"),
                        Array.Empty<ConversationMessage>()));
            }
        }

        private sealed class SessionStatefulLlm : NullLlmAdapter, ISessionStatefulLlmAdapter
        {
            private readonly StatefulDateeResult _result;

            public SessionStatefulLlm(StatefulDateeResult result)
            {
                _result = result;
            }

            public bool SupportsConversationSessions => true;
            public int LegacyDateeCallCount { get; private set; }
            public IReadOnlyList<ConversationMessage>? DateeHistorySeen { get; private set; }
            public IReadOnlyList<ConversationMessage>? AvatarHistorySeen { get; private set; }
            public LlmConversationSessionSnapshot? DateeSnapshotSeen { get; private set; }
            public LlmConversationSessionSnapshot? AvatarSnapshotSeen { get; private set; }

            public override Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                LegacyDateeCallCount++;
                return base.GetDateeResponseAsync(context, history, cancellationToken);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(
                DialogueContext context,
                IReadOnlyList<ConversationMessage> avatarHistory,
                LlmConversationSessionSnapshot? avatarSession,
                CancellationToken cancellationToken = default)
                => Task.FromResult(Array.Empty<DialogueOption>());

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context,
                IReadOnlyList<ConversationMessage> dateeHistory,
                IReadOnlyList<ConversationMessage> avatarHistory,
                LlmConversationSessionSnapshot? dateeSession,
                LlmConversationSessionSnapshot? avatarSession,
                CancellationToken cancellationToken = default)
            {
                DateeHistorySeen = dateeHistory;
                AvatarHistorySeen = avatarHistory;
                DateeSnapshotSeen = dateeSession;
                AvatarSnapshotSeen = avatarSession;
                return Task.FromResult(_result);
            }
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return TestHelpers.MakeCharacterProfile(
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
        public async Task ExecuteAsync_StatefulAdapter_AppendsCanonicalVisibleHistoryEntries()
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
            Assert.Equal("Hello!", state.DateeHistory[0].Content);
            Assert.Equal("Stateful response", state.DateeHistory[1].Content);
        }

        [Fact]
        public async Task ExecuteAsync_StatefulAdapter_CommitsOnlyCanonicalVisibleHistoryPair()
        {
            var adapterSuppliedEntries = new[]
            {
                ConversationMessage.User("PRIVATE EVENT ONLY SENTINEL 1344"),
                ConversationMessage.Assistant(
                    "Visible parsed reply\n[SIGNALS]\nTELL: Charm (internal)\nPrimary emotion: private"),
                ConversationMessage.Assistant("duplicate attempt that must not persist")
            };
            var mockLlm = new StatefulMockLlm("Visible parsed reply", adapterSuppliedEntries);
            var stage = new DateeResponseStage(mockLlm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);
            var deliveredMessage = "Hello, this is the delivered player line.";

            await stage.ExecuteAsync(
                state,
                MakeRollStageResult(),
                new DeliveryStageResult
                {
                    DeliveredMessage = deliveredMessage,
                    HorninessCheckResult = HorninessCheckResult.NotPerformed
                },
                MakeProfile("Player"),
                MakeProfile("Datee"),
                null,
                CancellationToken.None);

            Assert.Equal(2, state.DateeHistory.Count);
            Assert.Equal(ConversationMessage.UserRole, state.DateeHistory[0].Role);
            Assert.Equal(deliveredMessage, state.DateeHistory[0].Content);
            Assert.Equal(ConversationMessage.AssistantRole, state.DateeHistory[1].Role);
            Assert.Equal("Visible parsed reply", state.DateeHistory[1].Content);
            Assert.DoesNotContain(
                state.DateeHistory,
                entry => entry.Content.Contains("PRIVATE EVENT ONLY SENTINEL 1344", StringComparison.Ordinal)
                    || entry.Content.Contains("[SIGNALS]", StringComparison.OrdinalIgnoreCase)
                    || entry.Content.Contains("Primary emotion:", StringComparison.OrdinalIgnoreCase)
                    || entry.Content.Contains("duplicate attempt", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ExecuteAsync_StatefulAdapter_CannotMutateEngineOwnedHistory()
        {
            var llm = new MutatingStatefulLlm();
            var stage = new DateeResponseStage(llm);
            var state = new GameSessionState();
            state.Interest = new InterestMeter(10);
            state.DateeHistory.Add(ConversationMessage.Assistant("Older visible reply"));

            await stage.ExecuteAsync(
                state,
                MakeRollStageResult(),
                new DeliveryStageResult
                {
                    DeliveredMessage = "Current delivered line",
                    HorninessCheckResult = HorninessCheckResult.NotPerformed
                },
                MakeProfile("Player"),
                MakeProfile("Datee"),
                null,
                CancellationToken.None);

            Assert.True(llm.MutationSucceeded);
            Assert.Equal(3, state.DateeHistory.Count);
            Assert.Equal("Older visible reply", state.DateeHistory[0].Content);
            Assert.Equal("Current delivered line", state.DateeHistory[1].Content);
            Assert.Equal("Visible reply", state.DateeHistory[2].Content);
            Assert.DoesNotContain(
                state.DateeHistory,
                entry => entry.Content.Contains("PRIVATE MUTATION", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ExecuteAsync_SessionAdapter_AtomicallyAdoptsSnapshotsAndDualWritesSemanticHistory()
        {
            var oldDateeSnapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                "{\"old\":\"datee\"}");
            var oldAvatarSnapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                "{\"old\":\"avatar\"}");
            var newDateeSnapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                "{\"new\":\"datee\"}");
            var newAvatarSnapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                "{\"new\":\"avatar\"}");
            var llm = new SessionStatefulLlm(new StatefulDateeResult(
                new DateeResponse("Accepted visible reply"),
                Array.Empty<ConversationMessage>(),
                newDateeSnapshot,
                newAvatarSnapshot));
            var stage = new DateeResponseStage(llm);
            var state = new GameSessionState { Interest = new InterestMeter(10) };
            state.DateeHistory.Add(ConversationMessage.User("Older player line"));
            state.DateeHistory.Add(ConversationMessage.Assistant("Older DATEE reply"));
            state.AvatarHistory.Add(ConversationMessage.Assistant("Older player line"));
            state.AvatarHistory.Add(ConversationMessage.User("Older DATEE reply"));
            state.DateeSessionSnapshot = oldDateeSnapshot;
            state.AvatarSessionSnapshot = oldAvatarSnapshot;

            await stage.ExecuteAsync(
                state,
                MakeRollStageResult(),
                new DeliveryStageResult
                {
                    DeliveredMessage = "Current delivered line",
                    HorninessCheckResult = HorninessCheckResult.NotPerformed
                },
                MakeProfile("Player"),
                MakeProfile("Datee"),
                null,
                CancellationToken.None);

            Assert.Equal(0, llm.LegacyDateeCallCount);
            Assert.Same(oldDateeSnapshot, llm.DateeSnapshotSeen);
            Assert.Same(oldAvatarSnapshot, llm.AvatarSnapshotSeen);
            Assert.Equal(2, llm.DateeHistorySeen!.Count);
            Assert.Equal(2, llm.AvatarHistorySeen!.Count);
            Assert.Equal(4, state.DateeHistory.Count);
            Assert.Equal(ConversationMessage.UserRole, state.DateeHistory[2].Role);
            Assert.Equal("Current delivered line", state.DateeHistory[2].Content);
            Assert.Equal(ConversationMessage.AssistantRole, state.DateeHistory[3].Role);
            Assert.Equal("Accepted visible reply", state.DateeHistory[3].Content);
            Assert.Equal(4, state.AvatarHistory.Count);
            Assert.Equal(ConversationMessage.AssistantRole, state.AvatarHistory[2].Role);
            Assert.Equal("Current delivered line", state.AvatarHistory[2].Content);
            Assert.Equal(ConversationMessage.UserRole, state.AvatarHistory[3].Role);
            Assert.Equal("Accepted visible reply", state.AvatarHistory[3].Content);
            Assert.Same(newDateeSnapshot, state.DateeSessionSnapshot);
            Assert.Same(newAvatarSnapshot, state.AvatarSessionSnapshot);
        }

        [Fact]
        public async Task ExecuteAsync_SessionAdapterWithIncompleteSnapshot_DoesNotMutateState()
        {
            var oldDateeSnapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                "{\"old\":\"datee\"}");
            var oldAvatarSnapshot = new LlmConversationSessionSnapshot(
                LlmConversationSessionSnapshot.PiAgentSessionV1,
                "{\"old\":\"avatar\"}");
            var llm = new SessionStatefulLlm(new StatefulDateeResult(
                new DateeResponse("Must not commit"),
                Array.Empty<ConversationMessage>(),
                oldDateeSnapshot,
                avatarSessionSnapshot: null));
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var stage = new DateeResponseStage(llm, diagnostics.Add);
            var state = new GameSessionState { Interest = new InterestMeter(10) };
            state.DateeHistory.Add(ConversationMessage.Assistant("Existing DATEE history"));
            state.AvatarHistory.Add(ConversationMessage.User("Existing avatar history"));
            state.DateeSessionSnapshot = oldDateeSnapshot;
            state.AvatarSessionSnapshot = oldAvatarSnapshot;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => stage.ExecuteAsync(
                state,
                MakeRollStageResult(),
                new DeliveryStageResult
                {
                    DeliveredMessage = "Must not append",
                    HorninessCheckResult = HorninessCheckResult.NotPerformed
                },
                MakeProfile("Player"),
                MakeProfile("Datee"),
                null,
                CancellationToken.None));

            Assert.Contains("avatar session snapshot", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(state.DateeHistory);
            Assert.Single(state.AvatarHistory);
            Assert.Same(oldDateeSnapshot, state.DateeSessionSnapshot);
            Assert.Same(oldAvatarSnapshot, state.AvatarSessionSnapshot);
            var terminal = Assert.Single(diagnostics.FindAll(
                d => d.Lifecycle == OperationalDiagnosticLifecycle.Terminal));
            Assert.Equal(OperationalDiagnosticOutcome.Failed, terminal.Outcome);
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
            var fact = new OwnedPromptFactV1(
                datee.CharacterId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.Backstory,
                PromptFactSourceIds.Backstory(datee.CharacterId, BackstoryValidator.RequiredCategories[4], "bio_lie"),
                "DATEE backstory target");
            state.CurrentDateeReactionTarget = DateeReactionTarget.Create(datee.CharacterId, fact);

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
            Assert.Contains(4, state.DateeSpentBackstoryIndices);
            Assert.Empty(state.DateeSpentStakeIndices);
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesSpentStakes_BasedOnResolvedTarget()
        {
            // Arrange
            var mockLlm = new StatefulMockLlm("Dialogue", Array.Empty<ConversationMessage>());
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
            var fact = new OwnedPromptFactV1(
                datee.CharacterId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.PsychologicalStake,
                PromptFactSourceIds.PsychologicalStake(datee.CharacterId, 7),
                "DATEE stake target");
            state.CurrentDateeReactionTarget = DateeReactionTarget.Create(datee.CharacterId, fact);

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
            Assert.Contains(7, state.DateeSpentStakeIndices);
            Assert.Empty(state.DateeSpentBackstoryIndices);
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

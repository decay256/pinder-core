using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue1409_RetryHistorySafetyTests
    {
        private const string PartialProviderOutput = "PARTIAL-PROVIDER-OUTPUT-MUST-NOT-COMMIT";
        private const string AcceptedReply = "Visible accepted DATEE reply after provider retries.";

        static Issue1409_RetryHistorySafetyTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        [Trait("RetryHistorySafety", "physical_retry_success")]
        public async Task FiveFailedPhysicalAttemptsThenSuccess_CommitsOneAcceptedDateeExchangeAndOnlyAcceptedPerformanceLink()
        {
            var sink = new RecordingJournalSink();
            var transport = new PhysicalRetryScriptTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson())
            {
                PhysicalFailuresBeforeSuccess = 5,
            });
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, AcceptedReply)
            {
                PhysicalFailuresBeforeSuccess = 5,
            });
            var adapter = CreateAdapter(transport, sink);
            LlmConversationSessionSnapshot beforeDatee = await SnapshotAsync(DateeHistory(), "datee");
            LlmConversationSessionSnapshot beforeAvatar = await SnapshotAsync(AvatarHistory(), "avatar");
            string beforeDateeHash = Sha256(beforeDatee.Payload);
            string beforeAvatarHash = Sha256(beforeAvatar.Payload);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                beforeDatee,
                beforeAvatar);

            Assert.Equal(6, transport.PhysicalAttemptCount(LlmPhase.EmotionalDirector));
            Assert.Equal(6, transport.PhysicalAttemptCount(LlmPhase.OpponentResponse));
            Assert.Equal(5, transport.FailedPhysicalAttemptCount(LlmPhase.EmotionalDirector));
            Assert.Equal(5, transport.FailedPhysicalAttemptCount(LlmPhase.OpponentResponse));
            Assert.NotNull(result.DateeSessionSnapshot);
            Assert.NotNull(result.AvatarSessionSnapshot);
            Assert.NotEqual(beforeDateeHash, Sha256(result.DateeSessionSnapshot!.Payload));
            Assert.NotEqual(beforeAvatarHash, Sha256(result.AvatarSessionSnapshot!.Payload));

            ConversationMessage[] dateeSemantic = await SemanticHistoryAsync(result.DateeSessionSnapshot, "datee");
            ConversationMessage[] avatarSemantic = await SemanticHistoryAsync(result.AvatarSessionSnapshot, "avatar");
            Assert.Equal(
                new[]
                {
                    "existing player line",
                    "existing datee reply",
                    "delivered player line",
                    AcceptedReply,
                },
                dateeSemantic.Select(message => message.Content).ToArray());
            Assert.Equal(
                new[]
                {
                    "existing datee reply seen by avatar",
                    "existing avatar reply",
                    "delivered player line",
                    AcceptedReply,
                },
                avatarSemantic.Select(message => message.Content).ToArray());

            AssertNoPartialProviderOutput(result.NewHistoryEntries);
            AssertNoPartialProviderOutput(dateeSemantic);
            AssertNoPartialProviderOutput(avatarSemantic);
            AssertNoPartialProviderOutput(transport.LogicalCalls.SelectMany(call => call.PriorMessages));
            AssertNoPartialProviderOutput(transport.LogicalCalls.Select(call => ConversationMessage.User(call.UserMessage)));

            LlmResultRecord performance = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded));
            MessageLinkRecord performanceLink = Assert.Single(sink.MessageLinks.Where(link =>
                link.InvocationId == performance.Correlation.InvocationId));
            Assert.Equal(performance.Correlation.AgentSessionId, performanceLink.AgentSessionId);
            Assert.Equal(performance.Correlation.TurnId, performanceLink.TurnId);
            Assert.Equal(performance.Correlation.BranchId, performanceLink.BranchId);
            Assert.DoesNotContain(sink.Results.Where(record => record.TerminalStatus != AgentJournalTerminalStatus.Succeeded), resultRecord =>
                sink.MessageLinks.Any(link => link.InvocationId == resultRecord.Correlation.InvocationId));
            Assert.Equal(1, sink.MessageLinks.Count(link =>
                sink.Results.Any(record =>
                    record.Correlation.InvocationId == link.InvocationId
                    && record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance)));
        }

        [Fact]
        [Trait("RetryHistorySafety", "physical_retry_exhaustion")]
        public async Task SixFailedPhysicalAttemptsLeaveDateeAndAvatarSnapshotsUnchangedAndCreateNoFailedPerformanceLink()
        {
            var sink = new RecordingJournalSink();
            var transport = new PhysicalRetryScriptTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson()));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, output: null)
            {
                PhysicalFailuresBeforeSuccess = 6,
                ExhaustAfterFailures = true,
            });
            var adapter = CreateAdapter(transport, sink);
            LlmConversationSessionSnapshot beforeDatee = await SnapshotAsync(DateeHistory(), "datee");
            LlmConversationSessionSnapshot beforeAvatar = await SnapshotAsync(AvatarHistory(), "avatar");
            string beforeDateeHash = Sha256(beforeDatee.Payload);
            string beforeAvatarHash = Sha256(beforeAvatar.Payload);
            ConversationMessage[] beforeDateeSemantic = await SemanticHistoryAsync(beforeDatee, "datee");
            ConversationMessage[] beforeAvatarSemantic = await SemanticHistoryAsync(beforeAvatar, "avatar");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                beforeDatee,
                beforeAvatar));

            Assert.Equal("Provider physical retries exhausted.", exception.Message);
            Assert.Equal(beforeDateeHash, Sha256(beforeDatee.Payload));
            Assert.Equal(beforeAvatarHash, Sha256(beforeAvatar.Payload));
            Assert.Equal(beforeDateeSemantic.Select(message => message.Content), (await SemanticHistoryAsync(beforeDatee, "datee")).Select(message => message.Content));
            Assert.Equal(beforeAvatarSemantic.Select(message => message.Content), (await SemanticHistoryAsync(beforeAvatar, "avatar")).Select(message => message.Content));
            Assert.Equal(6, transport.PhysicalAttemptCount(LlmPhase.OpponentResponse));
            Assert.Empty(transport.SuccessfulPhysicalAttempts(LlmPhase.OpponentResponse));
            AssertNoPartialProviderOutput(beforeDateeSemantic);
            AssertNoPartialProviderOutput(beforeAvatarSemantic);
            AssertNoPartialProviderOutput(sink.Results.Select(record => ConversationMessage.Assistant(record.OutputText ?? string.Empty)));

            LlmResultRecord failedPerformance = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Failed));
            Assert.Equal(nameof(InvalidOperationException), failedPerformance.ErrorCode);
            Assert.Null(failedPerformance.OutputText);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == failedPerformance.Correlation.InvocationId);
        }

        [Theory]
        [InlineData(LlmPhase.EmotionalDirector)]
        [InlineData(LlmPhase.OpponentResponse)]
        [Trait("RetryHistorySafety", "cancellation")]
        public async Task CancellationDuringReasoningTextOrToolProgressPreservesSnapshotsAndCreatesNoCancelledLink(string cancelPhase)
        {
            var sink = new RecordingJournalSink();
            var transport = new PhysicalRetryScriptTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, cancelPhase == LlmPhase.EmotionalDirector ? null : ValidDirectionJson())
            {
                PhysicalFailuresBeforeSuccess = cancelPhase == LlmPhase.EmotionalDirector ? 2 : 0,
                CancelDuringProgress = cancelPhase == LlmPhase.EmotionalDirector,
            });
            if (cancelPhase == LlmPhase.OpponentResponse)
            {
                transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, output: null)
                {
                    PhysicalFailuresBeforeSuccess = 2,
                    CancelDuringProgress = true,
                });
            }
            var adapter = CreateAdapter(transport, sink);
            LlmConversationSessionSnapshot beforeDatee = await SnapshotAsync(DateeHistory(), "datee");
            LlmConversationSessionSnapshot beforeAvatar = await SnapshotAsync(AvatarHistory(), "avatar");
            string beforeDateeHash = Sha256(beforeDatee.Payload);
            string beforeAvatarHash = Sha256(beforeAvatar.Payload);
            ConversationMessage[] beforeDateeSemantic = await SemanticHistoryAsync(beforeDatee, "datee");
            ConversationMessage[] beforeAvatarSemantic = await SemanticHistoryAsync(beforeAvatar, "avatar");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                beforeDatee,
                beforeAvatar));

            Assert.Equal(beforeDateeHash, Sha256(beforeDatee.Payload));
            Assert.Equal(beforeAvatarHash, Sha256(beforeAvatar.Payload));
            Assert.Equal(beforeDateeSemantic.Select(message => message.Content), (await SemanticHistoryAsync(beforeDatee, "datee")).Select(message => message.Content));
            Assert.Equal(beforeAvatarSemantic.Select(message => message.Content), (await SemanticHistoryAsync(beforeAvatar, "avatar")).Select(message => message.Content));
            AssertNoPartialProviderOutput(beforeDateeSemantic);
            AssertNoPartialProviderOutput(beforeAvatarSemantic);
            AssertNoPartialProviderOutput(sink.Results.Select(record => ConversationMessage.Assistant(record.OutputText ?? string.Empty)));

            LlmResultRecord cancelled = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == (cancelPhase == LlmPhase.EmotionalDirector
                    ? GameRunConversationJournalInventory.EmotionalDirector
                    : GameRunConversationJournalInventory.DateePerformance)
                && record.TerminalStatus == AgentJournalTerminalStatus.Cancelled));
            Assert.Equal(AgentJournalTerminalCodes.Cancelled, cancelled.ErrorCode);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == cancelled.Correlation.InvocationId);
        }

        [Fact]
        [Trait("RetryHistorySafety", "semantic_validation_recovery")]
        public async Task SemanticValidationRecoveryStaysRejectedThenAcceptedAndIsNotProviderRetry()
        {
            var sink = new RecordingJournalSink();
            var transport = new PhysicalRetryScriptTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson()));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, ""));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, AcceptedReply));
            var adapter = CreateAdapter(transport, sink, maxContractViolationRetries: 1);
            LlmConversationSessionSnapshot beforeDatee = await SnapshotAsync(DateeHistory(), "datee");
            LlmConversationSessionSnapshot beforeAvatar = await SnapshotAsync(AvatarHistory(), "avatar");

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                beforeDatee,
                beforeAvatar);

            Assert.Equal(AcceptedReply, result.Response.MessageText);
            Assert.Equal(2, transport.LogicalCalls.Count(call => call.Phase == LlmPhase.OpponentResponse));
            Assert.Equal(2, transport.PhysicalAttemptCount(LlmPhase.OpponentResponse));
            Assert.Empty(transport.FailedPhysicalAttempts(LlmPhase.OpponentResponse));
            Assert.All(transport.LogicalCalls.Where(call => call.Phase == LlmPhase.OpponentResponse), call =>
            {
                Assert.Equal(DateeHistory().Select(message => message.Content), call.PriorMessages.Select(message => message.Content));
                Assert.DoesNotContain(PartialProviderOutput, call.UserMessage, StringComparison.Ordinal);
            });

            LlmResultRecord[] performanceResults = sink.Results
                .Where(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance)
                .OrderBy(record => record.Correlation.AttemptOrdinal)
                .ToArray();
            Assert.Equal(2, performanceResults.Length);
            Assert.Equal(AgentJournalTerminalStatus.Rejected, performanceResults[0].TerminalStatus);
            Assert.Equal("invalid_message", performanceResults[0].ValidationCode);
            Assert.Null(performanceResults[0].ErrorCode);
            Assert.Null(performanceResults[0].OutputText);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, performanceResults[1].TerminalStatus);
            Assert.Equal(AgentJournalTerminalCodes.Accepted, performanceResults[1].ValidationCode);
            Assert.DoesNotContain(performanceResults, record => record.TerminalStatus == AgentJournalTerminalStatus.Failed);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == performanceResults[0].Correlation.InvocationId);
            Assert.Single(sink.MessageLinks.Where(link => link.InvocationId == performanceResults[1].Correlation.InvocationId));
        }

        [Fact]
        [Trait("RetryHistorySafety", "avatar_director_failure")]
        public async Task AvatarDirectorProviderFailureLeavesAvatarSnapshotUnchangedAndCreatesNoFailedDirectorLink()
        {
            var sink = new RecordingJournalSink();
            var transport = new PhysicalRetryScriptTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.AvatarEmotionalDirector, output: null)
            {
                PhysicalFailuresBeforeSuccess = 6,
                ExhaustAfterFailures = true,
            });
            var adapter = CreateAdapter(transport, sink);
            LlmConversationSessionSnapshot beforeAvatar = await SnapshotAsync(AvatarHistory(), "avatar");
            string beforeAvatarHash = Sha256(beforeAvatar.Payload);
            ConversationMessage[] beforeAvatarSemantic = await SemanticHistoryAsync(beforeAvatar, "avatar");

            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetAvatarEmotionalDirectionAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                beforeAvatar));

            Assert.Equal(beforeAvatarHash, Sha256(beforeAvatar.Payload));
            Assert.Equal(beforeAvatarSemantic.Select(message => message.Content), (await SemanticHistoryAsync(beforeAvatar, "avatar")).Select(message => message.Content));
            Assert.Equal(6, transport.PhysicalAttemptCount(LlmPhase.AvatarEmotionalDirector));
            AssertNoPartialProviderOutput(beforeAvatarSemantic);

            LlmResultRecord failedDirector = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.AvatarEmotionalDirector
                && record.TerminalStatus == AgentJournalTerminalStatus.Failed));
            Assert.Null(failedDirector.OutputText);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == failedDirector.Correlation.InvocationId);
        }

        private static PinderLlmAdapter CreateAdapter(
            PhysicalRetryScriptTransport transport,
            RecordingJournalSink sink,
            int maxContractViolationRetries = 0)
            => new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = maxContractViolationRetries,
                    ContractViolationBackoffMs = 0,
                    AgentJournalHostSink = sink,
                    AgentJournalClock = () => new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
                });

        private static ConversationMessage[] DateeHistory()
            => new[]
            {
                ConversationMessage.User("existing player line"),
                ConversationMessage.Assistant("existing datee reply"),
            };

        private static ConversationMessage[] AvatarHistory()
            => new[]
            {
                ConversationMessage.User("existing datee reply seen by avatar"),
                ConversationMessage.Assistant("existing avatar reply"),
            };

        private static DateeContext MakeDateeContext(GameRunAgentJournalContext journalContext)
            => new DateeContext(
                dateePrompt: "DATEE character prompt.",
                conversationHistory: new[] { ("Player", "legacy player line"), ("Datee", "legacy datee line") },
                dateeLastMessage: "legacy datee line",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "delivered player line",
                interestBefore: 8,
                interestAfter: 12,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 9,
                deliveryTier: FailureTier.Success,
                interestBeforeState: InterestState.Lukewarm,
                interestAfterState: InterestState.Interested,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis()),
                agentJournalContext: journalContext);

        private static DialogueContext MakeDialogueContext(GameRunAgentJournalContext journalContext)
            => new DialogueContext(
                playerAvatarPrompt: "Player avatar prompt.",
                dateePrompt: "DATEE character prompt.",
                conversationHistory: Array.Empty<(string, string)>(),
                dateeLastMessage: "datee line for avatar director",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 9,
                availableStats: new[] { StatType.Charm, StatType.Wit, StatType.Honesty },
                currentInterestState: InterestState.Interested,
                agentJournalContext: journalContext);

        private static GameRunAgentJournalContext JournalContext()
            => new GameRunAgentJournalContext(
                "game-run-core-1409",
                "agent-session-core-1409",
                requestId: "request-core-1409",
                branchId: "main");

        private static async Task<LlmConversationSessionSnapshot> SnapshotAsync(
            IReadOnlyList<ConversationMessage> history,
            string sessionKind)
        {
            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                snapshot: null,
                history,
                sessionKind);
            return await session.SnapshotAsync();
        }

        private static async Task<ConversationMessage[]> SemanticHistoryAsync(
            LlmConversationSessionSnapshot snapshot,
            string sessionKind)
        {
            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                snapshot,
                Array.Empty<ConversationMessage>(),
                sessionKind);
            return (await session.BuildSemanticHistoryAsync()).ToArray();
        }

        private static string Sha256(string payload)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void AssertNoPartialProviderOutput(IEnumerable<ConversationMessage> messages)
        {
            foreach (ConversationMessage message in messages)
            {
                Assert.DoesNotContain(PartialProviderOutput, message.Content, StringComparison.Ordinal);
            }
        }

        private static string ValidDirectionJson()
            => new JObject
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["primary_emotion"] = "relief",
                ["secondary_emotion"] = CharacterEmotionalDirection.NoneSecondaryEmotion,
                ["regulatory_state"] = "controlled",
                ["activation"] = 4,
                ["trajectory"] = "escalating",
                ["core_threat_or_desire"] = "fear of being dismissed",
                ["interpretation"] = "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "Writing from relief, turns warmer while still checking sincerity",
            }.ToString(Formatting.None);

        private static PromptCatalog BuiltInCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            catalog.ValidateRuntimeCatalog();
            return catalog;
        }

        private static string FindPromptsRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private sealed class RecordingJournalSink : IAgentJournalSink
        {
            private readonly ConcurrentQueue<AgentJournalSinkRecord> _records =
                new ConcurrentQueue<AgentJournalSinkRecord>();

            public IReadOnlyList<LlmResultRecord> Results => _records
                .Where(record => record.CustomType == AgentJournalSchemaNames.LlmResultV1)
                .Select(record => (LlmResultRecord)record.Record)
                .ToArray();

            public IReadOnlyList<MessageLinkRecord> MessageLinks => _records
                .Where(record => record.CustomType == AgentJournalSchemaNames.MessageLinkV1)
                .Select(record => (MessageLinkRecord)record.Record)
                .ToArray();

            public Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
            {
                _records.Enqueue(record);
                return Task.CompletedTask;
            }
        }

        private sealed class PhysicalRetryScriptTransport : IConversationLlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport, ITokenUsageProvider
        {
            private readonly Queue<LogicalCallPlan> _plans = new Queue<LogicalCallPlan>();
            private readonly List<LogicalCall> _logicalCalls = new List<LogicalCall>();
            private readonly List<PhysicalAttempt> _physicalAttempts = new List<PhysicalAttempt>();
            private int _inputTokens;
            private int _outputTokens;
            private int _callCount;

            public bool SupportsConversationMessages => true;
            public bool SupportsStructuredConversationMessages => true;

            public IReadOnlyList<LogicalCall> LogicalCalls => _logicalCalls.ToArray();

            public void Queue(LogicalCallPlan plan) => _plans.Enqueue(plan);

            public int PhysicalAttemptCount(string phase)
                => _physicalAttempts.Count(attempt => attempt.Phase == phase);

            public int FailedPhysicalAttemptCount(string phase)
                => FailedPhysicalAttempts(phase).Count;

            public IReadOnlyList<PhysicalAttempt> FailedPhysicalAttempts(string phase)
                => _physicalAttempts
                    .Where(attempt => attempt.Phase == phase && attempt.Outcome == PhysicalAttemptOutcome.Failed)
                    .ToArray();

            public IReadOnlyList<PhysicalAttempt> SuccessfulPhysicalAttempts(string phase)
                => _physicalAttempts
                    .Where(attempt => attempt.Phase == phase && attempt.Outcome == PhysicalAttemptOutcome.Succeeded)
                    .ToArray();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
                => SendCoreAsync(phase, Array.Empty<ConversationMessage>(), userMessage, ct);

            public Task<string> SendConversationAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken cancellationToken = default)
                => SendCoreAsync(phase, priorMessages, userMessage, cancellationToken);

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
                => SendStructuredCoreAsync(request, Array.Empty<ConversationMessage>(), ct);

            public Task<StructuredLlmResponse> SendStructuredConversationAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken = default)
                => SendStructuredCoreAsync(request, priorMessages, cancellationToken);

            private async Task<StructuredLlmResponse> SendStructuredCoreAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken)
            {
                string output = await SendCoreAsync(
                    request.Phase,
                    priorMessages,
                    request.UserMessage,
                    cancellationToken).ConfigureAwait(false);
                return new StructuredLlmResponse(
                    StructuredOutput(request.SchemaName, output),
                    provider: "test",
                    model: "retry-script");
            }

            private Task<string> SendCoreAsync(
                string? phase,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_plans.Count == 0)
                    throw new InvalidOperationException("No scripted transport plan is available.");

                LogicalCallPlan plan = _plans.Dequeue();
                Assert.Equal(plan.Phase, phase);
                _logicalCalls.Add(new LogicalCall(
                    plan.Phase,
                    priorMessages.ToArray(),
                    userMessage));

                for (int i = 1; i <= plan.PhysicalFailuresBeforeSuccess; i++)
                {
                    AddPhysicalAttempt(plan.Phase, i, PhysicalAttemptOutcome.Failed);
                }

                if (plan.CancelDuringProgress)
                {
                    AddPhysicalAttempt(
                        plan.Phase,
                        plan.PhysicalFailuresBeforeSuccess + 1,
                        PhysicalAttemptOutcome.Cancelled);
                    return Task.FromException<string>(new OperationCanceledException(cancellationToken));
                }

                if (plan.ExhaustAfterFailures)
                {
                    return Task.FromException<string>(new InvalidOperationException(
                        "Provider physical retries exhausted."));
                }

                AddPhysicalAttempt(
                    plan.Phase,
                    plan.PhysicalFailuresBeforeSuccess + 1,
                    PhysicalAttemptOutcome.Succeeded);
                return Task.FromResult(plan.Output ?? string.Empty);
            }

            public SessionTokenUsage GetSessionUsage()
                => new SessionTokenUsage
                {
                    InputTokens = _inputTokens,
                    OutputTokens = _outputTokens,
                    CallCount = _callCount,
                };

            private void AddPhysicalAttempt(string phase, int ordinal, PhysicalAttemptOutcome outcome)
            {
                _physicalAttempts.Add(new PhysicalAttempt(
                    phase,
                    ordinal,
                    outcome,
                    PartialProviderOutput + ":" + phase + ":" + ordinal));
                _inputTokens += 3;
                _outputTokens += outcome == PhysicalAttemptOutcome.Succeeded ? 5 : 1;
                _callCount++;
            }

            private static string StructuredOutput(string schemaName, string output)
            {
                if (schemaName != DateePerformanceStructuredContract.SchemaName)
                    return output;
                return new JObject
                {
                    ["schema_version"] = DateePerformanceStructuredContract.SchemaVersion,
                    ["message"] = output,
                    ["signals"] = new JObject
                    {
                        ["tell"] = JValue.CreateNull(),
                        ["weakness"] = JValue.CreateNull(),
                    },
                }.ToString(Formatting.None);
            }
        }

        private sealed class LogicalCallPlan
        {
            public LogicalCallPlan(string phase, string? output)
            {
                Phase = phase;
                Output = output;
            }

            public string Phase { get; }
            public string? Output { get; }
            public int PhysicalFailuresBeforeSuccess { get; set; }
            public bool ExhaustAfterFailures { get; set; }
            public bool CancelDuringProgress { get; set; }
        }

        private sealed class LogicalCall
        {
            public LogicalCall(
                string phase,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage)
            {
                Phase = phase;
                PriorMessages = priorMessages;
                UserMessage = userMessage;
            }

            public string Phase { get; }
            public IReadOnlyList<ConversationMessage> PriorMessages { get; }
            public string UserMessage { get; }
        }

        private sealed class PhysicalAttempt
        {
            public PhysicalAttempt(
                string phase,
                int ordinal,
                PhysicalAttemptOutcome outcome,
                string partialOutput)
            {
                Phase = phase;
                Ordinal = ordinal;
                Outcome = outcome;
                PartialOutput = partialOutput;
            }

            public string Phase { get; }
            public int Ordinal { get; }
            public PhysicalAttemptOutcome Outcome { get; }
            public string PartialOutput { get; }
        }

        private enum PhysicalAttemptOutcome
        {
            Failed,
            Cancelled,
            Succeeded,
        }
    }
}

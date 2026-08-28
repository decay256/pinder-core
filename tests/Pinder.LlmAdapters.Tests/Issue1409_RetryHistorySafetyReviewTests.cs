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
    public sealed class Issue1409_RetryHistorySafetyReviewTests
    {
        private const string PartialProviderOutput = "PARTIAL-RETRY-ATTEMPT-CONTENT-MUST-NOT-COMMIT";
        private const string AcceptedReply = "Visible accepted DATEE reply after physical provider retries.";

        static Issue1409_RetryHistorySafetyReviewTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        [Trait("RetryHistorySafety", "physical_retry_boundary")]
        public async Task ProviderRetryDecorator_FiveFailedPhysicalAttemptsThenSuccess_CommitsOnlyAcceptedPerformanceExchange()
        {
            var sink = new RecordingJournalSink();
            var snapshots = new SnapshotRecorder();
            var transport = new RetryingConversationTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson()));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, AcceptedReply)
            {
                PhysicalFailuresBeforeTerminal = 5,
            });
            var adapter = CreateAdapter(transport, sink, snapshots);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                await SnapshotAsync(DateeHistory(), "datee"),
                await SnapshotAsync(AvatarHistory(), "avatar"));

            Assert.Equal(AcceptedReply, result.Response.MessageText);
            Assert.Equal(6, transport.Attempts.Count(attempt => attempt.Phase == LlmPhase.OpponentResponse));
            Assert.Equal(5, transport.Attempts.Count(attempt => attempt.Phase == LlmPhase.OpponentResponse && attempt.Outcome == ProviderAttemptOutcome.Failed));
            Assert.All(transport.Attempts.Where(attempt => attempt.Phase == LlmPhase.OpponentResponse && attempt.Outcome == ProviderAttemptOutcome.Failed), AssertFailedAttemptCarriedProgressPayload);

            SnapshotCapture restoredDatee = snapshots.Single("datee.restored");
            SnapshotCapture restoredAvatar = snapshots.Single("avatar.restored");
            AssertSameSnapshot(restoredDatee, snapshots.Single("datee.before-accepted-commit"));
            AssertSameSnapshot(restoredAvatar, snapshots.Single("avatar.before-accepted-commit"));

            ProviderAttempt acceptedAttempt = Assert.Single(transport.Attempts.Where(attempt =>
                attempt.Phase == LlmPhase.OpponentResponse && attempt.Outcome == ProviderAttemptOutcome.Succeeded));
            AssertNoPartialProviderOutput(acceptedAttempt.PriorMessages);
            Assert.DoesNotContain(PartialProviderOutput, acceptedAttempt.UserMessage, StringComparison.Ordinal);
            AssertNoPartialProviderOutput(transport.Attempts.SelectMany(attempt => attempt.PriorMessages));
            Assert.DoesNotContain(transport.Attempts, attempt => attempt.UserMessage.Contains(PartialProviderOutput, StringComparison.Ordinal));

            ConversationMessage[] dateeSemantic = await SemanticHistoryAsync(result.DateeSessionSnapshot!, "datee");
            ConversationMessage[] avatarSemantic = await SemanticHistoryAsync(result.AvatarSessionSnapshot!, "avatar");
            Assert.Equal(
                new[] { "existing player line", "existing datee reply", "delivered player line", AcceptedReply },
                dateeSemantic.Select(message => message.Content).ToArray());
            Assert.Equal(
                new[] { "existing datee reply seen by avatar", "existing avatar reply", "delivered player line", AcceptedReply },
                avatarSemantic.Select(message => message.Content).ToArray());
            AssertNoPartialProviderOutput(result.NewHistoryEntries);
            AssertNoPartialProviderOutput(dateeSemantic);
            AssertNoPartialProviderOutput(avatarSemantic);
            AssertNoPartialProviderOutput(sink.Results.Select(record => ConversationMessage.Assistant(record.OutputText ?? string.Empty)));

            LlmResultRecord performance = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded));
            MessageLinkRecord performanceLink = Assert.Single(sink.MessageLinks.Where(link =>
                link.InvocationId == performance.Correlation.InvocationId));
            Assert.Equal(performance.Correlation.AgentSessionId, performanceLink.AgentSessionId);
            Assert.DoesNotContain(sink.Results.Where(record => record.TerminalStatus != AgentJournalTerminalStatus.Succeeded), record =>
                sink.MessageLinks.Any(link => link.InvocationId == record.Correlation.InvocationId));
        }

        [Fact]
        [Trait("RetryHistorySafety", "performance_exhaustion")]
        public async Task SixPerformanceFailures_LeaveActualRestoredDateeAvatarSnapshotsUnchangedAndCreateNoFailedLink()
        {
            var sink = new RecordingJournalSink();
            var snapshots = new SnapshotRecorder();
            var transport = new RetryingConversationTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson()));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, output: null)
            {
                PhysicalFailuresBeforeTerminal = 6,
                ExhaustAfterFailures = true,
            });
            var adapter = CreateAdapter(transport, sink, snapshots);

            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                SnapshotAsync(DateeHistory(), "datee").GetAwaiter().GetResult(),
                SnapshotAsync(AvatarHistory(), "avatar").GetAwaiter().GetResult()));

            AssertSameSnapshot(snapshots.Single("datee.restored"), snapshots.Single("datee.before-error-dispose"));
            AssertSameSnapshot(snapshots.Single("avatar.restored"), snapshots.Single("avatar.before-error-dispose"));
            Assert.Equal(6, transport.Attempts.Count(attempt => attempt.Phase == LlmPhase.OpponentResponse));
            Assert.Empty(transport.Attempts.Where(attempt => attempt.Phase == LlmPhase.OpponentResponse && attempt.Outcome == ProviderAttemptOutcome.Succeeded));
            Assert.All(transport.Attempts.Where(attempt => attempt.Phase == LlmPhase.OpponentResponse), AssertFailedAttemptCarriedProgressPayload);
            AssertNoPartialProviderOutput(transport.Attempts.SelectMany(attempt => attempt.PriorMessages));
            Assert.DoesNotContain(transport.Attempts, attempt => attempt.UserMessage.Contains(PartialProviderOutput, StringComparison.Ordinal));
            AssertNoPartialProviderOutput(sink.Results.Select(record => ConversationMessage.Assistant(record.OutputText ?? string.Empty)));

            LlmResultRecord failedPerformance = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Failed));
            Assert.Null(failedPerformance.OutputText);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == failedPerformance.Correlation.InvocationId);
        }

        [Theory]
        [InlineData(false, LlmProgressKind.Reasoning)]
        [InlineData(true, LlmProgressKind.Reasoning)]
        [InlineData(true, LlmProgressKind.Text)]
        [InlineData(true, LlmProgressKind.ToolCall)]
        [Trait("RetryHistorySafety", "datee_director")]
        public async Task DateeDirectorFailureAndCancellation_LeaveActualDirectorBranchAndMainSessionsUnchanged(
            bool cancel,
            LlmProgressKind cancelAt)
        {
            var sink = new RecordingJournalSink();
            var snapshots = new SnapshotRecorder();
            var transport = new RetryingConversationTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, output: null)
            {
                PhysicalFailuresBeforeTerminal = cancel ? 0 : 6,
                ExhaustAfterFailures = !cancel,
                CancelDuringProgress = cancel,
                CancelAtProgressKind = cancelAt,
            });
            var adapter = CreateAdapter(transport, sink, snapshots);

            await Assert.ThrowsAnyAsync<Exception>(() => adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                SnapshotAsync(DateeHistory(), "datee").GetAwaiter().GetResult(),
                SnapshotAsync(AvatarHistory(), "avatar").GetAwaiter().GetResult()));

            AssertSameSnapshot(snapshots.Single("datee.restored"), snapshots.Single("datee.before-error-dispose"));
            AssertSameSnapshot(snapshots.Single("avatar.restored"), snapshots.Single("avatar.before-error-dispose"));
            AssertSameSnapshot(snapshots.Single("datee.director.branch.restored"), snapshots.Single("datee.director.branch.before-dispose"));
            AssertNoPartialProviderOutput(snapshots.Single("datee.director.branch.before-dispose").SemanticHistory);

            AgentJournalTerminalStatus expectedStatus = cancel
                ? AgentJournalTerminalStatus.Cancelled
                : AgentJournalTerminalStatus.Failed;
            LlmResultRecord director = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.EmotionalDirector
                && record.TerminalStatus == expectedStatus));
            Assert.Null(director.OutputText);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == director.Correlation.InvocationId);
        }

        [Theory]
        [InlineData(LlmProgressKind.Reasoning)]
        [InlineData(LlmProgressKind.Text)]
        [InlineData(LlmProgressKind.ToolCall)]
        [Trait("RetryHistorySafety", "performance_cancellation")]
        public async Task PerformanceCancellationDuringReasoningTextOrToolProgress_LeavesActualSnapshotsUnchanged(
            LlmProgressKind cancelAt)
        {
            var sink = new RecordingJournalSink();
            var snapshots = new SnapshotRecorder();
            var transport = new RetryingConversationTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson()));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, output: null)
            {
                CancelDuringProgress = true,
                CancelAtProgressKind = cancelAt,
            });
            var adapter = CreateAdapter(transport, sink, snapshots);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                SnapshotAsync(DateeHistory(), "datee").GetAwaiter().GetResult(),
                SnapshotAsync(AvatarHistory(), "avatar").GetAwaiter().GetResult()));

            AssertSameSnapshot(snapshots.Single("datee.restored"), snapshots.Single("datee.before-error-dispose"));
            AssertSameSnapshot(snapshots.Single("avatar.restored"), snapshots.Single("avatar.before-error-dispose"));
            LlmResultRecord cancelled = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Cancelled));
            Assert.Null(cancelled.OutputText);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == cancelled.Correlation.InvocationId);
        }

        [Theory]
        [InlineData(false, LlmProgressKind.Reasoning)]
        [InlineData(true, LlmProgressKind.Reasoning)]
        [InlineData(true, LlmProgressKind.Text)]
        [InlineData(true, LlmProgressKind.ToolCall)]
        [Trait("RetryHistorySafety", "avatar_director")]
        public async Task AvatarDirectorFailureAndCancellation_LeaveActualAvatarAndDirectorBranchSnapshotsUnchanged(
            bool cancel,
            LlmProgressKind cancelAt)
        {
            var sink = new RecordingJournalSink();
            var snapshots = new SnapshotRecorder();
            var transport = new RetryingConversationTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.AvatarEmotionalDirector, output: null)
            {
                PhysicalFailuresBeforeTerminal = cancel ? 0 : 6,
                ExhaustAfterFailures = !cancel,
                CancelDuringProgress = cancel,
                CancelAtProgressKind = cancelAt,
            });
            var adapter = CreateAdapter(transport, sink, snapshots);

            await Assert.ThrowsAnyAsync<Exception>(() => adapter.GetAvatarEmotionalDirectionAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                SnapshotAsync(AvatarHistory(), "avatar").GetAwaiter().GetResult()));

            AssertSameSnapshot(snapshots.Single("avatar.director.parent.restored"), snapshots.Single("avatar.director.parent.before-dispose"));
            AssertSameSnapshot(snapshots.Single("avatar.director.branch.restored"), snapshots.Single("avatar.director.branch.before-dispose"));
            AssertNoPartialProviderOutput(snapshots.Single("avatar.director.branch.before-dispose").SemanticHistory);

            AgentJournalTerminalStatus expectedStatus = cancel
                ? AgentJournalTerminalStatus.Cancelled
                : AgentJournalTerminalStatus.Failed;
            LlmResultRecord director = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.AvatarEmotionalDirector
                && record.TerminalStatus == expectedStatus));
            Assert.Null(director.OutputText);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == director.Correlation.InvocationId);
        }

        [Fact]
        [Trait("RetryHistorySafety", "semantic_validation_recovery")]
        public async Task SemanticValidationRecovery_RemainsRejectedThenAcceptedAndNotProviderRetry()
        {
            var sink = new RecordingJournalSink();
            var snapshots = new SnapshotRecorder();
            var transport = new RetryingConversationTransport();
            transport.Queue(new LogicalCallPlan(LlmPhase.EmotionalDirector, ValidDirectionJson()));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, ""));
            transport.Queue(new LogicalCallPlan(LlmPhase.OpponentResponse, AcceptedReply));
            var adapter = CreateAdapter(transport, sink, snapshots, maxContractViolationRetries: 1);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                await SnapshotAsync(DateeHistory(), "datee"),
                await SnapshotAsync(AvatarHistory(), "avatar"));

            Assert.Equal(AcceptedReply, result.Response.MessageText);
            Assert.Equal(2, transport.LogicalCalls.Count(call => call.Phase == LlmPhase.OpponentResponse));
            Assert.Equal(2, transport.Attempts.Count(attempt => attempt.Phase == LlmPhase.OpponentResponse));
            Assert.Empty(transport.Attempts.Where(attempt => attempt.Phase == LlmPhase.OpponentResponse && attempt.Outcome == ProviderAttemptOutcome.Failed));

            LlmResultRecord[] performanceResults = sink.Results
                .Where(record => record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance)
                .OrderBy(record => record.Correlation.AttemptOrdinal)
                .ToArray();
            Assert.Equal(2, performanceResults.Length);
            Assert.Equal(AgentJournalTerminalStatus.Rejected, performanceResults[0].TerminalStatus);
            Assert.Equal("invalid_message", performanceResults[0].ValidationCode);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, performanceResults[1].TerminalStatus);
            Assert.Equal(AgentJournalTerminalCodes.Accepted, performanceResults[1].ValidationCode);
            Assert.DoesNotContain(performanceResults, record => record.TerminalStatus == AgentJournalTerminalStatus.Failed);
            Assert.DoesNotContain(sink.MessageLinks, link => link.InvocationId == performanceResults[0].Correlation.InvocationId);
            Assert.Single(sink.MessageLinks.Where(link => link.InvocationId == performanceResults[1].Correlation.InvocationId));
        }

        private static PinderLlmAdapter CreateAdapter(
            RetryingConversationTransport transport,
            RecordingJournalSink sink,
            SnapshotRecorder snapshots,
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
                    AgentJournalSessionSnapshotObserver = snapshots.Record,
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
                "game-run-core-1409-review",
                "agent-session-core-1409-review",
                requestId: "request-core-1409-review",
                branchId: "main");

        private static async Task<LlmConversationSessionSnapshot> SnapshotAsync(
            IReadOnlyList<ConversationMessage> history,
            string sessionKind)
        {
            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                snapshot: null,
                history,
                sessionKind);
            return await session.SnapshotAsync().ConfigureAwait(false);
        }

        private static async Task<ConversationMessage[]> SemanticHistoryAsync(
            LlmConversationSessionSnapshot snapshot,
            string sessionKind)
        {
            await using PiConversationSession session = await PiConversationSession.RestoreOrImportAsync(
                snapshot,
                Array.Empty<ConversationMessage>(),
                sessionKind);
            return (await session.BuildSemanticHistoryAsync().ConfigureAwait(false)).ToArray();
        }

        private static string Sha256(string payload)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string SemanticHash(IEnumerable<ConversationMessage> messages)
            => Sha256(string.Join("\n", messages.Select(message => message.Role + ":" + message.Content)));

        private static void AssertSameSnapshot(SnapshotCapture expected, SnapshotCapture actual)
        {
            Assert.Equal(expected.SemanticHash, actual.SemanticHash);
            Assert.Equal(
                expected.SemanticHistory.Select(message => message.Role + ":" + message.Content).ToArray(),
                actual.SemanticHistory.Select(message => message.Role + ":" + message.Content).ToArray());
            AssertNoPartialProviderOutput(actual.SemanticHistory);
        }

        private static void AssertNoPartialProviderOutput(IEnumerable<ConversationMessage> messages)
        {
            foreach (ConversationMessage message in messages)
                Assert.DoesNotContain(PartialProviderOutput, message.Content, StringComparison.Ordinal);
        }

        private static void AssertFailedAttemptCarriedProgressPayload(ProviderAttempt attempt)
        {
            Assert.Contains(LlmProgressKind.ResponseStarted, attempt.ProgressKinds);
            Assert.Contains(LlmProgressKind.Reasoning, attempt.ProgressKinds);
            Assert.Contains(attempt.PartialPayloads, payload => payload.Contains(PartialProviderOutput, StringComparison.Ordinal));
            if (attempt.Outcome == ProviderAttemptOutcome.Failed)
            {
                Assert.Contains(LlmProgressKind.Text, attempt.ProgressKinds);
                Assert.Contains(LlmProgressKind.ToolCall, attempt.ProgressKinds);
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

        private sealed class SnapshotRecorder
        {
            private readonly ConcurrentQueue<SnapshotCapture> _captures = new ConcurrentQueue<SnapshotCapture>();

            public void Record(AgentJournalSessionSnapshotProbe probe)
                => _captures.Enqueue(new SnapshotCapture(
                    probe.Label,
                    probe.SessionKind,
                    Sha256(probe.Snapshot.Payload),
                    SemanticHash(probe.SemanticHistory),
                    probe.SemanticHistory.ToArray()));

            public SnapshotCapture Single(string label)
                => Assert.Single(_captures.Where(capture => capture.Label == label));
        }

        private sealed class SnapshotCapture
        {
            public SnapshotCapture(
                string label,
                string sessionKind,
                string snapshotPayloadHash,
                string semanticHash,
                IReadOnlyList<ConversationMessage> semanticHistory)
            {
                Label = label;
                SessionKind = sessionKind;
                SnapshotPayloadHash = snapshotPayloadHash;
                SemanticHash = semanticHash;
                SemanticHistory = semanticHistory;
            }

            public string Label { get; }
            public string SessionKind { get; }
            public string SnapshotPayloadHash { get; }
            public string SemanticHash { get; }
            public IReadOnlyList<ConversationMessage> SemanticHistory { get; }
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

        private sealed class RetryingConversationTransport : IConversationLlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport, ITokenUsageProvider
        {
            private readonly Queue<LogicalCallPlan> _plans = new Queue<LogicalCallPlan>();
            private readonly AttemptProgressTransport _inner = new AttemptProgressTransport();
            private int _inputTokens;
            private int _outputTokens;
            private int _callCount;

            public bool SupportsConversationMessages => true;
            public bool SupportsStructuredConversationMessages => true;
            public IReadOnlyList<ProviderAttempt> Attempts => _inner.Attempts;
            public IReadOnlyList<LogicalCall> LogicalCalls => _inner.LogicalCalls;

            public void Queue(LogicalCallPlan plan) => _plans.Enqueue(plan);

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
                => SendConversationAsync(systemPrompt, Array.Empty<ConversationMessage>(), userMessage, temperature, maxTokens, phase, ct);

            public async Task<string> SendConversationAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken cancellationToken = default)
            {
                if (_plans.Count == 0)
                    throw new InvalidOperationException("No scripted transport plan is available.");

                LogicalCallPlan plan = _plans.Dequeue();
                Assert.Equal(plan.Phase, phase);
                int terminalOrdinal = plan.CancelDuringProgress || !plan.ExhaustAfterFailures
                    ? plan.PhysicalFailuresBeforeTerminal + 1
                    : plan.PhysicalFailuresBeforeTerminal;

                for (int ordinal = 1; ordinal <= terminalOrdinal; ordinal++)
                {
                    ProviderAttemptOutcome outcome =
                        plan.CancelDuringProgress && ordinal == terminalOrdinal
                            ? ProviderAttemptOutcome.Cancelled
                            : ordinal <= plan.PhysicalFailuresBeforeTerminal
                                ? ProviderAttemptOutcome.Failed
                                : ProviderAttemptOutcome.Succeeded;
                    _inner.Queue(new ProviderAttemptScript(
                        plan.Phase,
                        ordinal,
                        outcome,
                        plan.Output ?? string.Empty,
                        plan.CancelAtProgressKind));
                    _callCount++;
                    _inputTokens += 7;
                    _outputTokens += outcome == ProviderAttemptOutcome.Succeeded ? 11 : 2;

                    try
                    {
                        return await _inner.SendConversationWithProgressAsync(
                            systemPrompt,
                            priorMessages,
                            userMessage,
                            new ImmediateProgress(),
                            temperature,
                            maxTokens,
                            phase,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ordinal < terminalOrdinal)
                    {
                        Assert.DoesNotContain(PartialProviderOutput, ex.Message, StringComparison.Ordinal);
                    }
                }

                throw new InvalidOperationException("Provider physical retries exhausted.");
            }

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
                string output = await SendConversationAsync(
                    request.SystemPrompt,
                    priorMessages,
                    request.UserMessage,
                    request.Temperature,
                    request.MaxTokens,
                    request.Phase,
                    cancellationToken).ConfigureAwait(false);
                return new StructuredLlmResponse(
                    StructuredOutput(request.SchemaName, output),
                    provider: "test",
                    model: "retry-script");
            }

            public SessionTokenUsage GetSessionUsage()
                => new SessionTokenUsage
                {
                    InputTokens = _inputTokens,
                    OutputTokens = _outputTokens,
                    CallCount = _callCount,
                };

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

        private sealed class AttemptProgressTransport : IProgressAwareConversationLlmTransport
        {
            private readonly Queue<ProviderAttemptScript> _scripts = new Queue<ProviderAttemptScript>();
            private readonly List<ProviderAttempt> _attempts = new List<ProviderAttempt>();
            private readonly List<LogicalCall> _logicalCalls = new List<LogicalCall>();

            public IReadOnlyList<ProviderAttempt> Attempts => _attempts.ToArray();
            public IReadOnlyList<LogicalCall> LogicalCalls => _logicalCalls.ToArray();

            public void Queue(ProviderAttemptScript script) => _scripts.Enqueue(script);

            public Task<string> SendWithProgressAsync(
                string systemPrompt,
                string userMessage,
                IProgress<LlmProgressEvent>? progress = null,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
                => SendConversationWithProgressAsync(systemPrompt, Array.Empty<ConversationMessage>(), userMessage, progress, temperature, maxTokens, phase, ct);

            public Task<string> SendConversationWithProgressAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                IProgress<LlmProgressEvent>? progress = null,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken cancellationToken = default)
            {
                if (_scripts.Count == 0)
                    throw new InvalidOperationException("No scripted physical attempt is available.");

                ProviderAttemptScript script = _scripts.Dequeue();
                Assert.Equal(script.Phase, phase);
                var progressKinds = new List<LlmProgressKind>();
                var partialPayloads = new List<string>();
                ConversationMessage[] context = priorMessages.ToArray();
                _logicalCalls.Add(new LogicalCall(script.Phase, context, userMessage));

                void Emit(LlmProgressKind kind, string? payload = null)
                {
                    progressKinds.Add(kind);
                    progress?.Report(new LlmProgressEvent(kind, new DateTimeOffset(2026, 8, 25, 12, 0, script.Ordinal, TimeSpan.Zero)));
                    if (payload != null)
                        partialPayloads.Add(payload);
                    if (script.Outcome == ProviderAttemptOutcome.Cancelled && script.CancelAtProgressKind == kind)
                        throw new OperationCanceledException(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Emit(LlmProgressKind.ResponseStarted);
                Emit(LlmProgressKind.Reasoning, $"{PartialProviderOutput}:reasoning:{script.Phase}:{script.Ordinal}");
                Emit(LlmProgressKind.Text, $"{PartialProviderOutput}:text:{script.Phase}:{script.Ordinal}");
                Emit(LlmProgressKind.ToolCall, $"{PartialProviderOutput}:tool:{{\"phase\":\"{script.Phase}\",\"attempt\":{script.Ordinal}}}");

                if (script.Outcome == ProviderAttemptOutcome.Failed)
                {
                    _attempts.Add(new ProviderAttempt(script.Phase, script.Ordinal, script.Outcome, context, userMessage, progressKinds, partialPayloads));
                    return Task.FromException<string>(new InvalidOperationException("Provider physical attempt failed before final result."));
                }

                Emit(LlmProgressKind.Completion);
                _attempts.Add(new ProviderAttempt(script.Phase, script.Ordinal, script.Outcome, context, userMessage, progressKinds, partialPayloads));
                return Task.FromResult(script.Output);
            }
        }

        private sealed class ImmediateProgress : IProgress<LlmProgressEvent>
        {
            public void Report(LlmProgressEvent value)
            {
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
            public int PhysicalFailuresBeforeTerminal { get; set; }
            public bool ExhaustAfterFailures { get; set; }
            public bool CancelDuringProgress { get; set; }
            public LlmProgressKind CancelAtProgressKind { get; set; } = LlmProgressKind.Text;
        }

        private sealed class ProviderAttemptScript
        {
            public ProviderAttemptScript(
                string phase,
                int ordinal,
                ProviderAttemptOutcome outcome,
                string output,
                LlmProgressKind cancelAtProgressKind)
            {
                Phase = phase;
                Ordinal = ordinal;
                Outcome = outcome;
                Output = output;
                CancelAtProgressKind = cancelAtProgressKind;
            }

            public string Phase { get; }
            public int Ordinal { get; }
            public ProviderAttemptOutcome Outcome { get; }
            public string Output { get; }
            public LlmProgressKind CancelAtProgressKind { get; }
        }

        private sealed class LogicalCall
        {
            public LogicalCall(string phase, IReadOnlyList<ConversationMessage> priorMessages, string userMessage)
            {
                Phase = phase;
                PriorMessages = priorMessages;
                UserMessage = userMessage;
            }

            public string Phase { get; }
            public IReadOnlyList<ConversationMessage> PriorMessages { get; }
            public string UserMessage { get; }
        }

        private sealed class ProviderAttempt
        {
            public ProviderAttempt(
                string phase,
                int ordinal,
                ProviderAttemptOutcome outcome,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                IReadOnlyList<LlmProgressKind> progressKinds,
                IReadOnlyList<string> partialPayloads)
            {
                Phase = phase;
                Ordinal = ordinal;
                Outcome = outcome;
                PriorMessages = priorMessages;
                UserMessage = userMessage;
                ProgressKinds = progressKinds;
                PartialPayloads = partialPayloads;
            }

            public string Phase { get; }
            public int Ordinal { get; }
            public ProviderAttemptOutcome Outcome { get; }
            public IReadOnlyList<ConversationMessage> PriorMessages { get; }
            public string UserMessage { get; }
            public IReadOnlyList<LlmProgressKind> ProgressKinds { get; }
            public IReadOnlyList<string> PartialPayloads { get; }
        }

        private enum ProviderAttemptOutcome
        {
            Failed,
            Cancelled,
            Succeeded,
        }
    }
}

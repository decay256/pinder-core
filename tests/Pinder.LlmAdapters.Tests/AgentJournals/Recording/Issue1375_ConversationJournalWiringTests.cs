using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Traps;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Recording
{
    [Collection("PromptTraceSingleton")]
    public sealed class Issue1375_ConversationJournalWiringTests
    {
        private const string DateePrivatePhaseDirector = "director";
        private const string DateePrivatePhasePerformance = "performance";

        private const string DialogueOptions =
            "OPTION_1\n[STAT: Charm]\n\"Hey, you come here often?\"\n\n" +
            "OPTION_2\n[STAT: Wit]\n\"Did you know penguins propose with pebbles?\"\n\n" +
            "OPTION_3\n[STAT: Honesty]\n\"I have to be real with you.\"\n";

        private const string SixDialogueOptions =
            "OPTION_1\n[STAT: Charm]\n\"Charm line.\"\n\n" +
            "OPTION_2\n[STAT: Rizz]\n\"Rizz line.\"\n\n" +
            "OPTION_3\n[STAT: Honesty]\n\"Honesty line.\"\n\n" +
            "OPTION_4\n[STAT: Chaos]\n\"Chaos line.\"\n\n" +
            "OPTION_5\n[STAT: Wit]\n\"Wit line.\"\n\n" +
            "OPTION_6\n[STAT: SelfAwareness]\n\"Self-awareness line.\"\n";

        static Issue1375_ConversationJournalWiringTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        [Trait("CORE-1375", "accepted_datee")]
        public async Task accepted_datee()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.Queue(LlmPhase.EmotionalDirector, ValidDirectionJson());
            transport.Queue(LlmPhase.OpponentResponse, "Visible accepted DATEE reply.");
            var adapter = CreateAdapter(transport, sink);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            Assert.Equal("Visible accepted DATEE reply.", result.Response.MessageText);
            LlmResultRecord accepted = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded));
            MessageLinkRecord link = Assert.Single(sink.MessageLinks.Where(candidate =>
                candidate.InvocationId == accepted.Correlation.InvocationId));
            Assert.Equal("game-run-core-1375", accepted.Correlation.GameRunId);
            Assert.DoesNotContain("PRIVATE", string.Join("|", result.NewHistoryEntries.Select(entry => entry.Content)), StringComparison.Ordinal);
        }

        [Fact]
        [Trait("CORE-1375", "accepted_avatar")]
        public async Task accepted_avatar()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.Queue(LlmPhase.DialogueOptions, DialogueOptions);
            var adapter = CreateAdapter(transport, sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                avatarSession: null);

            Assert.Equal(3, options.Length);
            Assert.Contains(sink.Results, record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.AvatarReply
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded);
        }

        [Fact]
        [Trait("CORE-1387", "datee_usage_identity")]
        public async Task datee_director_and_performance_records_complete_usage_and_shared_call_ids()
        {
            var sink = new RecordingJournalSink();
            var diagnostics = new ConcurrentQueue<OperationalDiagnosticEvent>();
            var transport = new ScriptedConversationTransport();
            transport.Queue(
                LlmPhase.EmotionalDirector,
                ValidDirectionJson(),
                inputTokens: 13,
                outputTokens: 7,
                cacheReadInputTokens: 3,
                cacheCreationInputTokens: 2);
            transport.Queue(
                LlmPhase.OpponentResponse,
                "Visible complete-usage DATEE reply.",
                inputTokens: 23,
                outputTokens: 11,
                cacheReadInputTokens: 5,
                cacheCreationInputTokens: 4);
            var adapter = CreateAdapter(transport, sink, diagnostics: diagnostics);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            Assert.Equal("Visible complete-usage DATEE reply.", result.Response.MessageText);
            LlmResultRecord director = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.EmotionalDirector
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded));
            LlmResultRecord performance = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded));
            AssertCompleteUsage(director, 13, 7, cacheCreationInputTokens: 2, cacheReadInputTokens: 3);
            AssertCompleteUsage(performance, 23, 11, cacheCreationInputTokens: 4, cacheReadInputTokens: 5);
            Assert.Equal("attempt-1", director.Correlation.AttemptId);
            Assert.Equal("attempt-1", performance.Correlation.AttemptId);
            Assert.EndsWith(":attempt-1", director.Correlation.InvocationId, StringComparison.Ordinal);
            Assert.EndsWith(":attempt-1", performance.Correlation.InvocationId, StringComparison.Ordinal);
            AssertTerminalDiagnosticCallId(diagnostics, director, LlmPhase.EmotionalDirector, DateePrivatePhaseDirector);
            AssertTerminalDiagnosticCallId(diagnostics, performance, LlmPhase.OpponentResponse, DateePrivatePhasePerformance);
        }

        [Fact]
        [Trait("CORE-1387", "avatar_usage_identity")]
        public async Task avatar_records_complete_usage_and_shared_call_id()
        {
            var sink = new RecordingJournalSink();
            var diagnostics = new ConcurrentQueue<OperationalDiagnosticEvent>();
            var transport = new ScriptedConversationTransport();
            transport.Queue(
                LlmPhase.DialogueOptions,
                DialogueOptions,
                inputTokens: 31,
                outputTokens: 17,
                cacheReadInputTokens: 6,
                cacheCreationInputTokens: 5);
            var adapter = CreateAdapter(transport, sink, diagnostics: diagnostics);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                avatarSession: null);

            Assert.Equal(3, options.Length);
            LlmResultRecord avatar = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.AvatarReply
                && record.TerminalStatus == AgentJournalTerminalStatus.Succeeded));
            AssertCompleteUsage(avatar, 31, 17, cacheCreationInputTokens: 5, cacheReadInputTokens: 6);
            AssertTerminalDiagnosticCallId(diagnostics, avatar, LlmPhase.DialogueOptions, privatePhase: null);
        }

        [Fact]
        [Trait("CORE-1387", "conversation_ambiguous_usage")]
        public async Task conversational_capture_marks_multi_call_delta_incomplete()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.Queue(
                LlmPhase.DialogueOptions,
                DialogueOptions,
                inputTokens: 22,
                outputTokens: 14,
                cacheReadInputTokens: 8,
                cacheCreationInputTokens: 6,
                callCount: 2);
            var adapter = CreateAdapter(transport, sink);

            DialogueOption[] options = await adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                avatarSession: null);

            Assert.Equal(3, options.Length);
            LlmResultRecord avatar = Assert.Single(sink.Results);
            Assert.Equal(AgentJournalUsageStatus.Incomplete, avatar.UsageStatus);
            Assert.NotNull(avatar.Usage);
            Assert.Equal(22, avatar.Usage!.InputTokens);
            Assert.Equal(14, avatar.Usage.OutputTokens);
            Assert.Equal(6, avatar.Usage.CacheCreationInputTokens);
            Assert.Equal(8, avatar.Usage.CacheReadInputTokens);
        }

        [Fact]
        [Trait("CORE-1375", "prefetch_branch_clone")]
        public async Task prefetch_branch_clone()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport
            {
                DefaultDialogueOutput = SixDialogueOptions,
            };
            var adapter = CreateAdapter(transport, sink);
            GameSession parent = CreateGameSession(adapter, JournalContext());

            GameSession branch = parent.Clone(
                adapter,
                GameRunConversationBranchKind.Prefetch,
                "prefetch-branch-001");
            TurnStart turn = await branch.StartTurnAsync();

            Assert.Equal(3, turn.Options.Length);
            Assert.Contains(sink.Invocations, record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.PrefetchBranchClone
                && record.Correlation.BranchId == "prefetch-branch-001");
        }

        [Fact]
        [Trait("CORE-1375", "speculative_branch_clone")]
        public async Task speculative_branch_clone()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport
            {
                DefaultDialogueOutput = SixDialogueOptions,
            };
            var adapter = CreateAdapter(transport, sink);
            GameSession parent = CreateGameSession(adapter, JournalContext());

            GameSession branch = parent.Clone(
                adapter,
                GameRunConversationBranchKind.Speculative,
                "speculative-branch-001");
            TurnStart turn = await branch.StartTurnAsync();

            Assert.Equal(3, turn.Options.Length);
            Assert.Contains(sink.Invocations, record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.SpeculativeBranchClone
                && record.Correlation.BranchId == "speculative-branch-001");
        }

        [Fact]
        [Trait("CORE-1375", "identical_prompt_retry")]
        public async Task identical_prompt_retry()
        {
            var sink = new RecordingJournalSink();
            var diagnostics = new ConcurrentQueue<OperationalDiagnosticEvent>();
            var transport = new ScriptedConversationTransport();
            transport.Queue(LlmPhase.EmotionalDirector, ValidDirectionJson());
            transport.Queue(LlmPhase.OpponentResponse, "   ");
            transport.Queue(LlmPhase.OpponentResponse, "Accepted after retry.");
            var adapter = CreateAdapter(transport, sink, maxRetries: 1, diagnostics: diagnostics);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            LlmInvocationRecord[] attempts = sink.Invocations.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance).ToArray();
            Assert.Equal("Accepted after retry.", result.Response.MessageText);
            Assert.Equal(new[] { 1, 2 }, attempts.Select(record => record.Correlation.AttemptOrdinal).ToArray());
            Assert.Equal(2, attempts.Select(record => record.Correlation.InvocationId).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(new[] { "attempt-1", "attempt-2" }, attempts.Select(record => record.Correlation.AttemptId).ToArray());
            Assert.Equal(attempts[0].InputDocuments.Select(document => document.Text), attempts[1].InputDocuments.Select(document => document.Text));
            foreach (LlmInvocationRecord attemptRecord in attempts)
            {
                AssertTerminalDiagnosticCallId(
                    diagnostics,
                    sink.Results.Single(record => record.Correlation.InvocationId == attemptRecord.Correlation.InvocationId),
                    LlmPhase.OpponentResponse,
                    DateePrivatePhasePerformance);
            }
            Assert.Contains(sink.Results, record => record.TerminalStatus == AgentJournalTerminalStatus.Rejected);
            Assert.Contains(sink.Results, record => record.TerminalStatus == AgentJournalTerminalStatus.Succeeded);
        }

        [Fact]
        [Trait("CORE-1387", "engine_level_datee_retry")]
        public async Task engine_level_same_turn_datee_retry_uses_unique_provider_invocation_ids()
        {
            var sink = new RecordingJournalSink();
            var diagnostics = new ConcurrentQueue<OperationalDiagnosticEvent>();
            var transport = new ScriptedConversationTransport();
            transport.Queue(LlmPhase.EmotionalDirector, ValidDirectionJson());
            transport.QueueException(LlmPhase.OpponentResponse, new LlmTransportException("first engine attempt failed"));
            transport.Queue(LlmPhase.EmotionalDirector, ValidDirectionJson());
            transport.Queue(LlmPhase.OpponentResponse, "Accepted after engine retry.");
            var adapter = CreateAdapter(transport, sink, diagnostics: diagnostics);
            DateeContext context = MakeDateeContext(JournalContext());

            await Assert.ThrowsAsync<LlmTransportException>(() => adapter.GetDateeResponseAsync(
                context,
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null));

            StatefulDateeResult accepted = await adapter.GetDateeResponseAsync(
                context,
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            LlmInvocationRecord[] performanceCalls = sink.Invocations.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance).ToArray();
            Assert.Equal("Accepted after engine retry.", accepted.Response.MessageText);
            Assert.Equal(2, performanceCalls.Length);
            Assert.All(performanceCalls, call => Assert.Equal(1, call.Correlation.AttemptOrdinal));
            Assert.All(performanceCalls, call => Assert.Equal("attempt-1", call.Correlation.AttemptId));
            Assert.Equal(2, performanceCalls.Select(call => call.Correlation.InvocationId).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(
                performanceCalls[0].InputDocuments.Select(document => document.Text),
                performanceCalls[1].InputDocuments.Select(document => document.Text));

            foreach (LlmInvocationRecord providerCall in performanceCalls)
            {
                LlmResultRecord durableMirror = sink.Results.Single(record =>
                    record.Correlation.InvocationId == providerCall.Correlation.InvocationId);
                AssertTerminalDiagnosticCallId(
                    diagnostics,
                    durableMirror,
                    LlmPhase.OpponentResponse,
                    DateePrivatePhasePerformance);
            }
        }

        [Fact]
        [Trait("CORE-1375", "validation_rejected")]
        public async Task validation_rejected()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.Queue(LlmPhase.DialogueOptions, "not a valid option contract");
            var adapter = CreateAdapter(transport, sink);

            await Assert.ThrowsAsync<LlmContractException>(() => adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                avatarSession: null));

            LlmResultRecord result = Assert.Single(sink.Results);
            Assert.Equal(AgentJournalTerminalStatus.Rejected, result.TerminalStatus);
            Assert.False(string.IsNullOrWhiteSpace(result.ValidationCode));
        }

        [Fact]
        [Trait("CORE-1375", "cancelled")]
        public async Task cancelled()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.QueueException(LlmPhase.DialogueOptions, new OperationCanceledException("provider cancelled"));
            var adapter = CreateAdapter(transport, sink);

            await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                avatarSession: null));

            LlmResultRecord result = Assert.Single(sink.Results);
            Assert.Equal(AgentJournalTerminalStatus.Cancelled, result.TerminalStatus);
        }

        [Fact]
        [Trait("CORE-1375", "provider_failed")]
        public async Task provider_failed()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.QueueException(LlmPhase.DialogueOptions, new InvalidOperationException("provider failed"));
            var adapter = CreateAdapter(transport, sink);

            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                avatarSession: null));

            LlmResultRecord result = Assert.Single(sink.Results);
            Assert.Equal(AgentJournalTerminalStatus.Failed, result.TerminalStatus);
            Assert.Equal(nameof(InvalidOperationException), result.ErrorCode);
        }

        [Fact]
        [Trait("CORE-1375", "director_branch_disposed")]
        public async Task director_branch_disposed()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            transport.Queue(LlmPhase.EmotionalDirector, ValidDirectionJson());
            transport.Queue(LlmPhase.OpponentResponse, "Visible reply after private disposal.");
            var adapter = CreateAdapter(transport, sink);

            await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            LlmResultRecord disposed = Assert.Single(sink.Results.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.DirectorBranchDisposed));
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, disposed.TerminalStatus);
            Assert.Equal(AgentJournalTerminalCodes.Accepted, disposed.ValidationCode);
            Assert.Equal("disposed", disposed.OutputText);
        }

        [Fact]
        [Trait("CORE-1375", "semantic_link_context_isolation")]
        public async Task semantic_link_context_isolation()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport();
            string privateDirection = ValidDirectionJson();
            transport.Queue(LlmPhase.EmotionalDirector, privateDirection);
            transport.Queue(LlmPhase.OpponentResponse, "Visible context-isolated reply.");
            var adapter = CreateAdapter(transport, sink);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeDateeContext(JournalContext()),
                Array.Empty<ConversationMessage>(),
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            LlmInvocationRecord director = Assert.Single(sink.Invocations.Where(record =>
                record.Correlation.OperationId == GameRunConversationJournalInventory.EmotionalDirector));
            Assert.Contains(sink.MessageLinks, link => link.InvocationId == director.Correlation.InvocationId);
            Assert.DoesNotContain(transport.PriorMessagesFor(LlmPhase.OpponentResponse), message =>
                message.Content.Contains(privateDirection, StringComparison.Ordinal));
            Assert.Equal(new[] { "visible delivered line", "Visible context-isolated reply." },
                result.NewHistoryEntries.Select(entry => entry.Content).ToArray());

            string fixture = File.ReadAllText(FindRepoFile(
                "tests/Pinder.LlmAdapters.Tests/Fixtures/AgentJournals/core-1375-semantic-link.snapshot.json"));
            Assert.Contains("\"private_director_link\": true", fixture, StringComparison.Ordinal);
            Assert.Contains("\"provider_context_messages_added\": 0", fixture, StringComparison.Ordinal);
        }

        [Fact]
        public async Task OneAdapter_ConcurrentGameRuns_KeepCorrelationDistinct()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport { DefaultDialogueOutput = DialogueOptions };
            var adapter = CreateAdapter(transport, sink);

            await Task.WhenAll(
                adapter.GetDialogueOptionsAsync(
                    MakeDialogueContext(JournalContext("game-run-a", "request-a")),
                    Array.Empty<ConversationMessage>(),
                    avatarSession: null),
                adapter.GetDialogueOptionsAsync(
                    MakeDialogueContext(JournalContext("game-run-b", "request-b")),
                    Array.Empty<ConversationMessage>(),
                    avatarSession: null));

            Assert.Equal(new[] { "game-run-a", "game-run-b" },
                sink.Invocations.Select(record => record.Correlation.GameRunId).OrderBy(value => value).ToArray());
            Assert.Equal(new[] { "request-a", "request-b" },
                sink.Invocations.Select(record => record.Correlation.RequestId).OrderBy(value => value).ToArray());
        }

        [Fact]
        public async Task GameSessionBoundary_GeneratesDistinctGameRunCorrelationWhenHostOmitsIt()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport { DefaultDialogueOutput = SixDialogueOptions };
            var adapter = CreateAdapter(transport, sink);
            GameSession first = CreateGameSession(adapter, journalContext: null);
            GameSession second = CreateGameSession(adapter, journalContext: null);

            await Task.WhenAll(first.StartTurnAsync(), second.StartTurnAsync());

            string[] gameRunIds = sink.Invocations
                .Where(record => record.Correlation.OperationId == GameRunConversationJournalInventory.AvatarReply)
                .Select(record => record.Correlation.GameRunId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(2, gameRunIds.Length);
            Assert.All(gameRunIds, id => Assert.StartsWith("game-run-", id, StringComparison.Ordinal));
        }

        [Fact]
        public async Task ConfiguredSinkWithoutPerRunContext_FailsClosed()
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport { DefaultDialogueOutput = DialogueOptions };
            var adapter = CreateAdapter(transport, sink);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adapter.GetDialogueOptionsAsync(
                    MakeDialogueContext(journalContext: null),
                    Array.Empty<ConversationMessage>(),
                    avatarSession: null));

            Assert.Contains("per-call GameRunAgentJournalContext", error.Message, StringComparison.Ordinal);
            Assert.Empty(sink.Invocations);
            Assert.Empty(sink.Results);
        }

        [Theory]
        [InlineData("sk-secret")]
        [InlineData("contains whitespace")]
        [InlineData("path/segment")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public async Task HostCorrelationIdentifiers_RejectUnsafeValuesBeforePersistence(string unsafeId)
        {
            var sink = new RecordingJournalSink();
            var transport = new ScriptedConversationTransport { DefaultDialogueOutput = DialogueOptions };
            var adapter = CreateAdapter(transport, sink);
            var context = new GameRunAgentJournalContext(
                unsafeId,
                "agent-session-core-1375",
                requestId: "request-core-1375",
                branchId: "main");

            await Assert.ThrowsAsync<ArgumentException>(() => adapter.GetDialogueOptionsAsync(
                MakeDialogueContext(context),
                Array.Empty<ConversationMessage>(),
                avatarSession: null));
            Assert.Empty(sink.Invocations);
            Assert.Empty(sink.Results);
        }

        [Fact]
        public void EveryCorrelationAndLinkIdentifier_UsesOpaqueCredentialPolicy()
        {
            const string unsafeId = "api_key-secret";
            var correlation = new AgentJournalCorrelationIds(
                unsafeId,
                unsafeId,
                unsafeId,
                unsafeId,
                1,
                attemptId: unsafeId,
                requestId: unsafeId,
                turnId: unsafeId,
                branchId: unsafeId);
            var invocation = new LlmInvocationRecord(
                correlation,
                "model",
                "phase",
                new[] { Document("document", "input") },
                "2026-08-16T12:00:00Z");
            AgentJournalValidationResult invocationValidation = AgentJournalValidator.Validate(invocation);

            Assert.Equal(8, invocationValidation.Errors.Count(error =>
                error.Code == AgentJournalValidator.CredentialShapedSourceIdentifier));

            AgentJournalValidationResult linkValidation = AgentJournalValidator.Validate(
                new MessageLinkRecord(unsafeId, unsafeId, unsafeId, unsafeId, unsafeId));
            Assert.Equal(5, linkValidation.Errors.Count(error =>
                error.Code == AgentJournalValidator.CredentialShapedSourceIdentifier));
        }

        [Fact]
        public void StaticApprovedInventory_IsClosedForConversationVerifier()
        {
            Assert.Equal(6, GameRunConversationJournalInventory.ApprovedCallPaths.Count);
            Assert.All(GameRunConversationJournalInventory.ApprovedCallPaths, id =>
                Assert.True(GameRunConversationJournalInventory.IsApproved(id), id));
        }

        private static AgentJournalInputDocument Document(string id, string text)
            => new AgentJournalInputDocument(
                id,
                AgentJournalInputRole.User,
                text,
                new[]
                {
                    new AgentJournalProvenanceRange(
                        id,
                        0,
                        text.Length,
                        AgentJournalRangeKind.RuntimeGenerated,
                        AgentJournalRedactionClass.None,
                        new AgentJournalSourceIdentity(
                            AgentJournalSourceKind.RuntimeGenerated,
                            "runtime",
                            id)),
                });

        private static PinderLlmAdapter CreateAdapter(
            ScriptedConversationTransport transport,
            RecordingJournalSink sink,
            int maxRetries = 0,
            ConcurrentQueue<OperationalDiagnosticEvent>? diagnostics = null)
            => new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = maxRetries,
                    ContractViolationBackoffMs = 1,
                    AgentJournalHostSink = sink,
                    AgentJournalClock = () => new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
                    OnDiagnostic = diagnostics == null ? (Action<OperationalDiagnosticEvent>?)null : diagnostics.Enqueue,
                });

        private static void AssertCompleteUsage(
            LlmResultRecord record,
            int inputTokens,
            int outputTokens,
            int cacheCreationInputTokens,
            int cacheReadInputTokens)
        {
            Assert.Equal(AgentJournalUsageStatus.Complete, record.UsageStatus);
            Assert.NotNull(record.Usage);
            Assert.Equal(inputTokens, record.Usage!.InputTokens);
            Assert.Equal(outputTokens, record.Usage.OutputTokens);
            Assert.Equal(inputTokens + outputTokens, record.Usage.TotalTokens);
            Assert.Equal(cacheCreationInputTokens, record.Usage.CacheCreationInputTokens);
            Assert.Equal(cacheReadInputTokens, record.Usage.CacheReadInputTokens);
        }

        private static void AssertTerminalDiagnosticCallId(
            ConcurrentQueue<OperationalDiagnosticEvent> diagnostics,
            LlmResultRecord result,
            string phase,
            string? privatePhase)
        {
            OperationalDiagnosticEvent terminal = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Terminal
                && diagnostic.PhaseCode == phase
                && diagnostic.CallId == result.Correlation.InvocationId
                && (!diagnostic.CorrelationHints.ContainsKey("datee_private_phase")
                    || diagnostic.CorrelationHints["datee_private_phase"] == (privatePhase ?? string.Empty))));
            Assert.Equal(result.Correlation.InvocationId, terminal.CallId);
        }

        private static GameRunAgentJournalContext JournalContext(
            string gameRunId = "game-run-core-1375",
            string requestId = "request-core-1375")
            => new GameRunAgentJournalContext(
                gameRunId,
                "agent-session-core-1375",
                requestId,
                branchId: "main");

        private static DialogueContext MakeDialogueContext(GameRunAgentJournalContext? journalContext)
            => new DialogueContext(
                playerAvatarPrompt: "You are the player avatar.",
                dateePrompt: "You are the datee.",
                conversationHistory: Array.Empty<(string, string)>(),
                dateeLastMessage: string.Empty,
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                availableStats: new[] { StatType.Charm, StatType.Wit, StatType.Honesty },
                agentJournalContext: journalContext);

        private static DateeContext MakeDateeContext(GameRunAgentJournalContext journalContext)
            => new DateeContext(
                dateePrompt: "You are the datee.",
                conversationHistory: new[] { ("Player", "older visible player line"), ("Datee", "older visible datee line") },
                dateeLastMessage: "older visible datee line",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "visible delivered line",
                interestBefore: 8,
                interestAfter: 12,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                deliveryTier: FailureTier.Success,
                interestBeforeState: InterestState.Lukewarm,
                interestAfterState: InterestState.Interested,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis()),
                agentJournalContext: journalContext);

        private static GameSession CreateGameSession(
            PinderLlmAdapter adapter,
            GameRunAgentJournalContext? journalContext)
        {
            CharacterProfile player = TestHelpers.MakeCharacterProfile(
                TestHelpers.MakeStatBlock(),
                "You are the player avatar.",
                "Player",
                new TimingProfile(5, 0, 0, "neutral"),
                1);
            CharacterProfile datee = TestHelpers.MakeCharacterProfile(
                TestHelpers.MakeStatBlock(),
                "You are the datee.",
                "Datee",
                new TimingProfile(5, 0, 0, "neutral"),
                1);
            return new GameSession(
                player,
                datee,
                adapter,
                new FixedDice(),
                new NullTrapRegistry(),
                new GameSessionConfig(
                    clock: TestHelpers.MakeClock(),
                    rules: TestHelpers.SessionRules,
                    maxDialogueOptions: 6,
                    agentJournalContext: journalContext));
        }

        private static string ValidDirectionJson()
            => "{" +
               "\"schema_version\":\"" + EmotionalDirectorContract.SchemaVersion + "\"," +
               "\"primary_emotion\":\"relieved but cautious\"," +
               "\"intensity\":\"moderate and steadily rising\"," +
               "\"underlying_feeling\":\"fear of being dismissed\"," +
               "\"interpretation\":\"reads the message as specific warmth that is probably meant for them\"," +
               "\"impulse\":\"leans in with a careful question\"," +
               "\"restraint\":\"keeps the reply tentative but available\"," +
               "\"response_posture\":\"turns warmer while still checking sincerity\"" +
               "}";

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
                var candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static string FindRepoFile(string relativePath)
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException(relativePath);
        }

        private sealed class RecordingJournalSink : IAgentJournalSink
        {
            private readonly ConcurrentQueue<AgentJournalSinkRecord> _records = new ConcurrentQueue<AgentJournalSinkRecord>();

            public IReadOnlyList<LlmInvocationRecord> Invocations => _records
                .Where(record => record.CustomType == AgentJournalSchemaNames.LlmInvocationV1)
                .Select(record => (LlmInvocationRecord)record.Record)
                .ToArray();

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

        private sealed class ScriptedConversationTransport : ILlmTransport, IConversationLlmTransport, ITokenUsageProvider
        {
            private readonly object _gate = new object();
            private readonly Queue<(string Phase, string? Output, Exception? Error, UsageStep Usage)> _outputs =
                new Queue<(string, string?, Exception?, UsageStep)>();
            private readonly ConcurrentDictionary<string, ConcurrentQueue<ConversationMessage>> _priorMessages =
                new ConcurrentDictionary<string, ConcurrentQueue<ConversationMessage>>(StringComparer.Ordinal);
            private int _inputTokens;
            private int _outputTokens;
            private int _cacheReadInputTokens;
            private int _cacheCreationInputTokens;
            private int _callCount;

            public bool SupportsConversationMessages => true;
            public string? DefaultDialogueOutput { get; set; }

            public void Queue(
                string phase,
                string output,
                int inputTokens = 11,
                int outputTokens = 7,
                int cacheReadInputTokens = 0,
                int cacheCreationInputTokens = 0,
                int callCount = 1)
            {
                lock (_gate)
                {
                    _outputs.Enqueue((
                        phase,
                        output,
                        null,
                        new UsageStep(inputTokens, outputTokens, cacheReadInputTokens, cacheCreationInputTokens, callCount)));
                }
            }

            public void QueueException(string phase, Exception error)
            {
                lock (_gate) _outputs.Enqueue((phase, null, error, UsageStep.Zero));
            }

            public IReadOnlyList<ConversationMessage> PriorMessagesFor(string phase)
                => _priorMessages.TryGetValue(phase, out ConcurrentQueue<ConversationMessage>? messages)
                    ? messages.ToArray()
                    : Array.Empty<ConversationMessage>();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
                => DequeueAsync(phase, ct);

            public Task<string> SendConversationAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken cancellationToken = default)
            {
                var captured = _priorMessages.GetOrAdd(phase ?? string.Empty, _ => new ConcurrentQueue<ConversationMessage>());
                foreach (ConversationMessage message in priorMessages) captured.Enqueue(message);
                return DequeueAsync(phase, cancellationToken);
            }

            private Task<string> DequeueAsync(string? phase, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string Phase, string? Output, Exception? Error, UsageStep Usage) next;
                lock (_gate)
                {
                    if (_outputs.Count == 0 && phase == LlmPhase.DialogueOptions && DefaultDialogueOutput != null)
                    {
                        AddUsage(new UsageStep(11, 7, 0, 0));
                        return Task.FromResult(DefaultDialogueOutput);
                    }
                    next = _outputs.Dequeue();
                }

                Assert.Equal(next.Phase, phase);
                if (next.Error != null) return Task.FromException<string>(next.Error);
                AddUsage(next.Usage);
                return Task.FromResult(next.Output!);
            }

            public SessionTokenUsage GetSessionUsage()
                => new SessionTokenUsage
                {
                    InputTokens = _inputTokens,
                    OutputTokens = _outputTokens,
                    CacheReadInputTokens = _cacheReadInputTokens,
                    CacheCreationInputTokens = _cacheCreationInputTokens,
                    CallCount = _callCount,
                };

            private void AddUsage(UsageStep usage)
            {
                _inputTokens += usage.InputTokens;
                _outputTokens += usage.OutputTokens;
                _cacheReadInputTokens += usage.CacheReadInputTokens;
                _cacheCreationInputTokens += usage.CacheCreationInputTokens;
                _callCount += usage.CallCount;
            }

            private readonly struct UsageStep
            {
                public static readonly UsageStep Zero = new UsageStep(0, 0, 0, 0, 0);

                public UsageStep(
                    int inputTokens,
                    int outputTokens,
                    int cacheReadInputTokens,
                    int cacheCreationInputTokens,
                    int callCount = 1)
                {
                    InputTokens = inputTokens;
                    OutputTokens = outputTokens;
                    CacheReadInputTokens = cacheReadInputTokens;
                    CacheCreationInputTokens = cacheCreationInputTokens;
                    CallCount = callCount;
                }

                public int InputTokens { get; }
                public int OutputTokens { get; }
                public int CacheReadInputTokens { get; }
                public int CacheCreationInputTokens { get; }
                public int CallCount { get; }
            }
        }

        private sealed class FixedDice : IDiceRoller
        {
            public int Roll(int sides) => Math.Min(5, sides);
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

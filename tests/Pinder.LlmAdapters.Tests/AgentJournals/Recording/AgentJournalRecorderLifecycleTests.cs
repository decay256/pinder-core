using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests.AgentJournals.Recording
{
    public sealed class AgentJournalRecorderLifecycleTests
    {
        [Fact]
        public async Task StartAndAcceptedCompletion_ProjectSameRecordInstancesToPiAndHost()
        {
            var pi = new RecordingProjectionSink();
            var host = new RecordingJournalSink();
            var attempt = await NewRecorder(pi, host).StartAsync();

            AgentJournalTerminalResult terminal = await attempt.CompleteAcceptedAsync(
                "assistant text",
                new AgentJournalUsage(10, 5, 15),
                semanticEntryId: "semantic-entry-001");

            Assert.Equal(3, pi.Records.Count);
            Assert.Equal(3, host.Records.Count);
            for (int i = 0; i < pi.Records.Count; i++)
            {
                Assert.Same(pi.Records[i].Record, host.Records[i].Record);
                Assert.Equal(pi.Records[i].RecordId, host.Records[i].RecordId);
                Assert.Equal(pi.Records[i].CustomType, host.Records[i].CustomType);
            }

            Assert.Same(attempt.InvocationRecord, pi.Records[0].Record);
            Assert.Same(terminal.ResultRecord, pi.Records[1].Record);
            Assert.Same(terminal.MessageLinkRecord, pi.Records[2].Record);
            Assert.Equal("agent-journal/game-run-001/agent-session-datee/invocation-001/attempt-001/invocation", pi.Records[0].RecordId);
            Assert.Equal("agent-journal/game-run-001/agent-session-datee/invocation-001/attempt-001/result", pi.Records[1].RecordId);
            Assert.Equal("agent-journal/game-run-001/agent-session-datee/invocation-001/attempt-001/message-link/semantic-entry-001", pi.Records[2].RecordId);
        }

        [Theory]
        [InlineData("accepted", AgentJournalTerminalStatus.Succeeded, "accepted", null)]
        [InlineData("validation_rejected", AgentJournalTerminalStatus.Rejected, "validation_rejected", null)]
        [InlineData("provider_failed", AgentJournalTerminalStatus.Failed, null, "provider_failed")]
        [InlineData("cancelled", AgentJournalTerminalStatus.Cancelled, null, "cancelled")]
        public async Task TerminalMatrix_EmitsExactlyOneTerminalResult(
            string terminalKind,
            AgentJournalTerminalStatus expectedStatus,
            string? expectedValidationCode,
            string? expectedErrorCode)
        {
            var host = new RecordingJournalSink();
            var attempt = await NewRecorder(hostSink: host).StartAsync();

            AgentJournalTerminalResult result = await CompleteAsync(attempt, terminalKind);
            AgentJournalTerminalResult duplicate = await CompleteAsync(attempt, "provider_failed");

            Assert.True(result.Recorded);
            Assert.False(duplicate.Recorded);
            Assert.True(duplicate.AlreadyCompleted);
            Assert.Same(result.ResultRecord, duplicate.ResultRecord);
            Assert.Equal(2, host.Records.Count);
            Assert.Equal(AgentJournalSchemaNames.LlmResultV1, host.Records[1].CustomType);
            Assert.Equal(expectedStatus, result.ResultRecord.TerminalStatus);
            Assert.Equal(expectedValidationCode, result.ResultRecord.ValidationCode);
            Assert.Equal(expectedErrorCode, result.ResultRecord.ErrorCode);
            if (expectedStatus == AgentJournalTerminalStatus.Rejected)
            {
                Assert.Null(result.ResultRecord.OutputText);
                Assert.True(AgentJournalValidator.Validate(result.ResultRecord).IsValid);
            }
        }

        [Fact]
        public async Task DisposeWithoutCompletion_EmitsAbandonedOnce()
        {
            var host = new RecordingJournalSink();
            AgentJournalAttempt attempt = await NewRecorder(hostSink: host).StartAsync();

            await attempt.DisposeAsync();
            await attempt.DisposeAsync();

            var result = Assert.IsType<LlmResultRecord>(host.Records.Last().Record);
            Assert.Equal(2, host.Records.Count);
            Assert.Equal(AgentJournalTerminalStatus.Failed, result.TerminalStatus);
            Assert.Equal(AgentJournalTerminalCodes.Abandoned, result.ErrorCode);
        }

        [Fact]
        public async Task StableRecordIds_MakeDuplicateHostDeliveryIdempotent()
        {
            var host = new RecordingJournalSink { IdempotentByRecordId = true };
            var context = NewContext(hostSink: host);
            await (await new AgentJournalRecorder(context).StartAsync()).CompleteProviderFailedAsync("provider_failed");
            await (await new AgentJournalRecorder(context).StartAsync()).CompleteProviderFailedAsync("provider_failed");

            Assert.Equal(2, host.Records.Count);
            Assert.Equal(2, host.DuplicateDeliveries);

            var nextContext = NewContext(hostSink: host, invocationId: "invocation-002", attemptId: "attempt-002", attemptOrdinal: 2);
            await (await new AgentJournalRecorder(nextContext).StartAsync()).CompleteProviderFailedAsync("provider_failed");

            Assert.Equal(4, host.Records.Count);
        }

        [Fact]
        public async Task BestEffortSinkFailure_EmitsDiagnosticAndPreservesTerminalState()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var pi = new RecordingProjectionSink();
            var host = new RecordingJournalSink();
            var attempt = await NewRecorder(
                piSink: pi,
                hostSink: host,
                failureMode: AgentJournalSinkFailureMode.BestEffort,
                diagnostics: diagnostics.Add).StartAsync();
            host.ThrowOnWrite = true;

            AgentJournalTerminalResult result = await attempt.CompleteAcceptedAsync("assistant text", null);

            Assert.True(result.Recorded);
            Assert.Equal(AgentJournalTerminalStatus.Succeeded, result.ResultRecord.TerminalStatus);
            Assert.NotEmpty(result.SinkFailures);
            Assert.Equal(2, pi.Records.Count);
            Assert.Contains(diagnostics, diagnostic =>
                diagnostic.EventName == AgentJournalOperationalDiagnostics.SinkPersistenceFailedEventName
                && diagnostic.CorrelationHints["record_id"].EndsWith("/result", StringComparison.Ordinal)
                && diagnostic.CorrelationHints["failure_mode"] == "BestEffort");
        }

        [Fact]
        public async Task FailClosedSinkFailure_ThrowsTypedPersistenceFailureBeforeAcceptance()
        {
            var host = new RecordingJournalSink { ThrowOnWrite = true };
            var recorder = NewRecorder(
                hostSink: host,
                failureMode: AgentJournalSinkFailureMode.FailClosed);

            AgentJournalSinkPersistenceException error = await Assert.ThrowsAsync<AgentJournalSinkPersistenceException>(
                async () => await recorder.StartAsync());

            Assert.EndsWith("/invocation", error.RecordId, StringComparison.Ordinal);
            Assert.Equal(AgentJournalSchemaNames.LlmInvocationV1, error.CustomType);
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }

        [Fact]
        public async Task InvocationStartPostPiHostFailure_RetryIsSingleFlightAndReturnsSameAttempt()
        {
            var pi = new RecordingProjectionSink();
            var host = new RecordingJournalSink { FailuresRemaining = 1 };
            var recorder = NewRecorder(
                piSink: pi,
                hostSink: host,
                failureMode: AgentJournalSinkFailureMode.FailClosed);

            AgentJournalSinkPersistenceException error = await Assert.ThrowsAsync<AgentJournalSinkPersistenceException>(
                async () => await recorder.StartAsync());
            var retryGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.NextWriteGate = retryGate;
            Task<AgentJournalAttempt> firstRetry = recorder.RetryStartAsync();
            Task<AgentJournalAttempt> concurrentRetry = recorder.RetryStartAsync();

            Assert.Same(firstRetry, concurrentRetry);
            Assert.False(firstRetry.IsCompleted);
            retryGate.SetResult(null);
            AgentJournalAttempt firstAttempt = await firstRetry;
            AgentJournalAttempt concurrentAttempt = await concurrentRetry;
            AgentJournalAttempt doubleRetryAttempt = await recorder.RetryStartAsync();

            Assert.EndsWith("/invocation", error.RecordId, StringComparison.Ordinal);
            Assert.Same(firstAttempt, concurrentAttempt);
            Assert.Same(firstAttempt, doubleRetryAttempt);
            Assert.Same(firstAttempt.InvocationRecord, pi.Records[0].Record);
            Assert.Single(pi.Records);
            Assert.Single(host.Records);
            Assert.Equal(2, host.Attempts.Count);
            Assert.Equal(error.RecordId, host.Attempts[0].RecordId);
            Assert.Equal(error.RecordId, host.Attempts[1].RecordId);
            Assert.Same(host.Attempts[0].Record, host.Attempts[1].Record);
            Assert.Same(pi.Records[0].Record, host.Records[0].Record);
        }

        [Fact]
        public async Task RetryStartWithoutPendingInvocation_FailsExplicitly()
        {
            var recorder = NewRecorder();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await recorder.RetryStartAsync());
        }

        [Fact]
        public async Task SinkPolicyAfterPiProjectionMatrix_FailClosedRetriesHostWithoutDuplicatePiProjection()
        {
            var pi = new RecordingProjectionSink();
            var host = new RecordingJournalSink();
            var attempt = await NewRecorder(
                piSink: pi,
                hostSink: host,
                failureMode: AgentJournalSinkFailureMode.FailClosed).StartAsync();
            host.FailuresRemaining = 1;

            AgentJournalSinkPersistenceException error = await Assert.ThrowsAsync<AgentJournalSinkPersistenceException>(
                async () => await attempt.CompleteAcceptedAsync("assistant text", null));
            AgentJournalTerminalResult retried = await attempt.CompleteAcceptedAsync("ignored retry text", null);

            Assert.EndsWith("/result", error.RecordId, StringComparison.Ordinal);
            Assert.True(retried.Recorded);
            Assert.Equal("assistant text", retried.ResultRecord.OutputText);
            Assert.Equal(2, pi.Records.Count);
            Assert.Equal(2, host.Records.Count);
            Assert.Equal(3, host.Attempts.Count);
            Assert.Equal(host.Attempts[1].RecordId, host.Attempts[2].RecordId);
            Assert.Same(host.Attempts[1].Record, host.Attempts[2].Record);
            Assert.Same(pi.Records[1].Record, host.Attempts[2].Record);
        }

        [Fact]
        public async Task FailedTerminalProjection_DoesNotCacheCompletionAndCanRetry()
        {
            var pi = new RecordingProjectionSink();
            var host = new RecordingJournalSink();
            var attempt = await NewRecorder(pi, host).StartAsync();
            pi.ThrowOnWrite = true;

            await Assert.ThrowsAsync<AgentJournalPiProjectionException>(
                async () => await attempt.CompleteValidationRejectedAsync("policy_rejected"));
            pi.ThrowOnWrite = false;
            AgentJournalTerminalResult retried = await attempt.CompleteValidationRejectedAsync("ignored_retry_code");

            Assert.True(retried.Recorded);
            Assert.Equal("policy_rejected", retried.ResultRecord.ValidationCode);
            Assert.Null(retried.ResultRecord.OutputText);
            Assert.True(AgentJournalValidator.Validate(retried.ResultRecord).IsValid);
            Assert.Equal(2, pi.Records.Count);
            Assert.Equal(2, host.Records.Count);
        }

        [Fact]
        public async Task InvalidTerminalPayload_DoesNotCacheCompletionBeforeValidationSucceeds()
        {
            var pi = new RecordingProjectionSink();
            var host = new RecordingJournalSink();
            var attempt = await NewRecorder(pi, host).StartAsync();

            await Assert.ThrowsAsync<ArgumentException>(
                async () => await attempt.CompleteAcceptedAsync(null!, null));
            AgentJournalTerminalResult recovered = await attempt.CompleteProviderFailedAsync("provider_failed");

            Assert.True(recovered.Recorded);
            Assert.Equal(AgentJournalTerminalStatus.Failed, recovered.ResultRecord.TerminalStatus);
            Assert.Equal("provider_failed", recovered.ResultRecord.ErrorCode);
            Assert.Equal(2, pi.Records.Count);
            Assert.Equal(2, host.Records.Count);
        }

        [Fact]
        public async Task InvocationSnapshot_IsolatedFromCallerDocumentAndRangeMutation()
        {
            var ranges = new List<AgentJournalProvenanceRange> { Document("doc.user", "hello").Ranges[0] };
            var callerDocument = new AgentJournalInputDocument("doc.user", AgentJournalInputRole.User, "hello", ranges);
            var documents = new List<AgentJournalInputDocument> { callerDocument };
            var attempt = await new AgentJournalRecorder(NewContext(inputDocuments: documents)).StartAsync();

            ranges.Clear();
            documents.Clear();
            documents.Add(Document("doc.changed", "changed"));

            AgentJournalInputDocument emitted = Assert.Single(attempt.InvocationRecord.InputDocuments);
            AgentJournalProvenanceRange emittedRange = Assert.Single(emitted.Ranges);
            Assert.Equal("doc.user", emitted.DocumentId);
            Assert.Equal("doc.user", emittedRange.DocumentId);
            Assert.NotSame(callerDocument, emitted);
            Assert.NotSame(ranges, emitted.Ranges);
        }

        [Fact]
        public async Task PiProjectionFailure_PreventsSuccessfulJournalCommit()
        {
            var pi = new RecordingProjectionSink { ThrowOnWrite = true };
            var host = new RecordingJournalSink();
            var recorder = NewRecorder(pi, host);

            AgentJournalPiProjectionException error = await Assert.ThrowsAsync<AgentJournalPiProjectionException>(
                async () => await recorder.StartAsync());

            Assert.EndsWith("/invocation", error.RecordId, StringComparison.Ordinal);
            Assert.Empty(host.Records);
        }

        [Fact]
        public async Task CancelledTerminalCleanup_UsesBoundedIndependentToken()
        {
            var host = new RecordingJournalSink { IgnoreCancellationAndNeverComplete = true };
            var attempt = await NewRecorder(
                hostSink: host,
                failureMode: AgentJournalSinkFailureMode.BestEffort,
                writeTimeout: TimeSpan.FromMilliseconds(40)).StartAsync();

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var watch = Stopwatch.StartNew();
                AgentJournalTerminalResult result = await attempt.CompleteCancelledAsync("cancelled", cancelled.Token);
                watch.Stop();

                Assert.True(result.Recorded);
                Assert.Equal(AgentJournalTerminalStatus.Cancelled, result.ResultRecord.TerminalStatus);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
                Assert.NotEmpty(result.SinkFailures);
            }
        }

        private static Task<AgentJournalTerminalResult> CompleteAsync(AgentJournalAttempt attempt, string terminalKind)
        {
            switch (terminalKind)
            {
                case "accepted":
                    return attempt.CompleteAcceptedAsync("assistant text", new AgentJournalUsage(1, 2, 3));
                case "validation_rejected":
                    return attempt.CompleteValidationRejectedAsync("validation_rejected");
                case "provider_failed":
                    return attempt.CompleteProviderFailedAsync("provider_failed");
                case "cancelled":
                    return attempt.CompleteCancelledAsync("cancelled", new CancellationToken(true));
                default:
                    throw new ArgumentOutOfRangeException(nameof(terminalKind), terminalKind, null);
            }
        }

        private static AgentJournalRecorder NewRecorder(
            IAgentJournalProjectionSink? piSink = null,
            IAgentJournalSink? hostSink = null,
            AgentJournalSinkFailureMode failureMode = AgentJournalSinkFailureMode.BestEffort,
            Action<OperationalDiagnosticEvent>? diagnostics = null,
            TimeSpan? writeTimeout = null)
            => new AgentJournalRecorder(NewContext(piSink, hostSink, failureMode, diagnostics, writeTimeout));

        private static AgentJournalRecorderContext NewContext(
            IAgentJournalProjectionSink? piSink = null,
            IAgentJournalSink? hostSink = null,
            AgentJournalSinkFailureMode failureMode = AgentJournalSinkFailureMode.BestEffort,
            Action<OperationalDiagnosticEvent>? diagnostics = null,
            TimeSpan? writeTimeout = null,
            string invocationId = "invocation-001",
            string attemptId = "attempt-001",
            int attemptOrdinal = 1,
            IReadOnlyList<AgentJournalInputDocument>? inputDocuments = null)
        {
            return new AgentJournalRecorderContext(
                new AgentJournalCorrelationIds(
                    "game-run-001",
                    "agent-session-datee",
                    invocationId,
                    "operation-dialogue-options",
                    attemptOrdinal,
                    attemptId: attemptId,
                    requestId: "request-001",
                    turnId: "turn-001",
                    branchId: "branch-main"),
                "test-model",
                "dialogue_options",
                inputDocuments ?? new[] { Document("doc.user", "hello") })
            {
                PiProjectionSink = piSink,
                HostSink = hostSink,
                SinkFailureMode = failureMode,
                OnDiagnostic = diagnostics,
                WriteTimeout = writeTimeout ?? TimeSpan.FromSeconds(1),
                Clock = () => new DateTimeOffset(2026, 8, 15, 22, 30, 0, TimeSpan.Zero),
            };
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
                        AgentJournalRedactionClass.SafeMetadata,
                        new AgentJournalSourceIdentity(
                            AgentJournalSourceKind.RuntimeGenerated,
                            "runtime.prompt",
                            "dialogue.user")),
                });

        private sealed class RecordingProjectionSink : IAgentJournalProjectionSink
        {
            public readonly List<AgentJournalSinkRecord> Records = new List<AgentJournalSinkRecord>();
            public bool ThrowOnWrite { get; set; }

            public Task ProjectAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
            {
                if (ThrowOnWrite)
                {
                    throw new InvalidOperationException("pi projection failed");
                }

                Records.Add(record);
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingJournalSink : IAgentJournalSink
        {
            private readonly HashSet<string> _seenIds = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<AgentJournalSinkRecord> Attempts = new List<AgentJournalSinkRecord>();
            public readonly List<AgentJournalSinkRecord> Records = new List<AgentJournalSinkRecord>();
            public bool ThrowOnWrite { get; set; }
            public int FailuresRemaining { get; set; }
            public bool IdempotentByRecordId { get; set; }
            public bool IgnoreCancellationAndNeverComplete { get; set; }
            public int DuplicateDeliveries { get; private set; }
            public TaskCompletionSource<object?>? NextWriteGate { get; set; }

            public Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
            {
                Attempts.Add(record);
                if (IgnoreCancellationAndNeverComplete)
                {
                    return new TaskCompletionSource<object?>().Task;
                }

                if (ThrowOnWrite || FailuresRemaining-- > 0)
                {
                    throw new InvalidOperationException("host sink failed");
                }

                if (NextWriteGate != null)
                {
                    Task gate = NextWriteGate.Task;
                    NextWriteGate = null;
                    return PersistAfterGateAsync(record, gate);
                }

                return Commit(record);
            }

            private async Task PersistAfterGateAsync(AgentJournalSinkRecord record, Task gate)
            {
                await gate.ConfigureAwait(false);
                await Commit(record).ConfigureAwait(false);
            }

            private Task Commit(AgentJournalSinkRecord record)
            {
                if (IdempotentByRecordId && !_seenIds.Add(record.RecordId))
                {
                    DuplicateDeliveries++;
                    return Task.CompletedTask;
                }

                Records.Add(record);
                return Task.CompletedTask;
            }
        }
    }
}

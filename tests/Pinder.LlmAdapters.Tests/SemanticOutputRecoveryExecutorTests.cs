using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class SemanticOutputRecoveryExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_FirstAttemptAccepted_ReturnsValueWithoutDelayOrRejection()
        {
            int attempts = 0;
            int delays = 0;
            int rejections = 0;

            var outcome = await SemanticOutputRecoveryExecutor.ExecuteAsync<string, string>(
                3,
                (attempt, cancellationToken) =>
                {
                    attempts++;
                    return Task.FromResult(
                        SemanticOutputRecoveryAttemptResult<string, string>.Accepted("accepted text"));
                },
                delayAfterRejectedAttempt: attempt =>
                {
                    delays++;
                    return TimeSpan.Zero;
                },
                onRejected: rejection => rejections++);

            Assert.True(outcome.IsAccepted);
            Assert.Equal("accepted text", outcome.AcceptedValue);
            Assert.Equal(1, attempts);
            Assert.Equal(0, delays);
            Assert.Equal(0, rejections);
        }

        [Fact]
        public async Task ExecuteAsync_RejectedAttempts_ObservesRejectionsAndReturnsExhaustionMetadata()
        {
            var observed = new List<SemanticOutputRecoveryRejection<string>>();
            var delays = new List<int>();

            var outcome = await SemanticOutputRecoveryExecutor.ExecuteAsync<int, string>(
                3,
                (attempt, cancellationToken) => Task.FromResult(
                    SemanticOutputRecoveryAttemptResult<int, string>.Rejected("rejection-" + attempt)),
                delayAfterRejectedAttempt: attempt =>
                {
                    delays.Add(attempt);
                    return TimeSpan.Zero;
                },
                onRejected: observed.Add);

            Assert.False(outcome.IsAccepted);
            Assert.Equal(3, outcome.Exhaustion.AttemptCount);
            Assert.Equal("rejection-3", outcome.Exhaustion.FinalRejection);
            Assert.Equal(new[] { 1, 2, 3 }, observed.ConvertAll(r => r.Attempt).ToArray());
            Assert.Equal(new[] { false, false, true }, observed.ConvertAll(r => r.IsFinalAttempt).ToArray());
            Assert.Equal(new[] { 1, 2 }, delays.ToArray());
        }

        [Fact]
        public async Task ExecuteAsync_LaterAcceptedAttempt_PreservesAttemptNumberAndSkipsRemainingDelay()
        {
            var delays = new List<int>();

            var outcome = await SemanticOutputRecoveryExecutor.ExecuteAsync<string, string>(
                4,
                (attempt, cancellationToken) => Task.FromResult(
                    attempt == 3
                        ? SemanticOutputRecoveryAttemptResult<string, string>.Accepted("repair")
                        : SemanticOutputRecoveryAttemptResult<string, string>.Rejected("bad")),
                delayAfterRejectedAttempt: attempt =>
                {
                    delays.Add(attempt);
                    return TimeSpan.Zero;
                });

            Assert.True(outcome.IsAccepted);
            Assert.Equal("repair", outcome.AcceptedValue);
            Assert.Equal(new[] { 1, 2 }, delays.ToArray());
        }

        [Fact]
        public async Task ExecuteAsync_UnexpectedAttemptException_PropagatesWithoutRetrying()
        {
            int attempts = 0;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SemanticOutputRecoveryExecutor.ExecuteAsync<string, string>(
                    3,
                    (attempt, cancellationToken) =>
                    {
                        attempts++;
                        throw new InvalidOperationException("validator bug");
                    }));

            Assert.Equal("validator bug", ex.Message);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task ExecuteAsync_DelayUsesCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(20));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SemanticOutputRecoveryExecutor.ExecuteAsync<string, string>(
                    2,
                    (attempt, cancellationToken) => Task.FromResult(
                        SemanticOutputRecoveryAttemptResult<string, string>.Rejected("bad")),
                    delayAfterRejectedAttempt: attempt => TimeSpan.FromMinutes(1),
                    cancellationToken: cts.Token));
        }

        [Fact]
        public async Task ExecuteAsync_AttemptReceivesCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(20));
            CancellationToken observedToken = default;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SemanticOutputRecoveryExecutor.ExecuteAsync<string, string>(
                    2,
                    async (attempt, cancellationToken) =>
                    {
                        observedToken = cancellationToken;
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        return SemanticOutputRecoveryAttemptResult<string, string>.Accepted("unreachable");
                    },
                    cancellationToken: cts.Token));

            Assert.Equal(cts.Token, observedToken);
        }
    }
}

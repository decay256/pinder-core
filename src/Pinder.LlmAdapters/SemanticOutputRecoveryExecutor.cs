using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Repeats semantic output validation attempts without owning transport,
    /// parser, prompt, or terminal exception policy.
    /// </summary>
    public static class SemanticOutputRecoveryExecutor
    {
        public static async Task<SemanticOutputRecoveryOutcome<TAccepted, TRejection>> ExecuteAsync<TAccepted, TRejection>(
            int totalAttempts,
            Func<int, CancellationToken, Task<SemanticOutputRecoveryAttemptResult<TAccepted, TRejection>>> attemptAsync,
            Func<int, TimeSpan>? delayAfterRejectedAttempt = null,
            Action<SemanticOutputRecoveryRejection<TRejection>>? onRejected = null,
            CancellationToken cancellationToken = default)
        {
            if (totalAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalAttempts), "totalAttempts must be at least 1.");
            if (attemptAsync == null)
                throw new ArgumentNullException(nameof(attemptAsync));

            TRejection finalRejection = default!;
            for (int attempt = 1; attempt <= totalAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await attemptAsync(attempt, cancellationToken).ConfigureAwait(false);
                if (result == null)
                    throw new InvalidOperationException("Semantic output recovery attempt returned null.");

                if (result.IsAccepted)
                    return SemanticOutputRecoveryOutcome<TAccepted, TRejection>.Accepted(result.AcceptedValue);

                finalRejection = result.Rejection;
                bool isFinalAttempt = attempt == totalAttempts;
                onRejected?.Invoke(new SemanticOutputRecoveryRejection<TRejection>(
                    attempt,
                    totalAttempts,
                    finalRejection,
                    isFinalAttempt));

                if (isFinalAttempt)
                {
                    return SemanticOutputRecoveryOutcome<TAccepted, TRejection>.Exhausted(
                        new SemanticOutputRecoveryExhaustion<TRejection>(attempt, finalRejection));
                }

                TimeSpan delay = delayAfterRejectedAttempt?.Invoke(attempt) ?? TimeSpan.Zero;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Semantic output recovery reached an unreachable state.");
        }
    }

    public sealed class SemanticOutputRecoveryAttemptResult<TAccepted, TRejection>
    {
        private readonly TAccepted _acceptedValue;
        private readonly TRejection _rejection;

        private SemanticOutputRecoveryAttemptResult(bool isAccepted, TAccepted acceptedValue, TRejection rejection)
        {
            IsAccepted = isAccepted;
            _acceptedValue = acceptedValue;
            _rejection = rejection;
        }

        public bool IsAccepted { get; }

        public TAccepted AcceptedValue
        {
            get
            {
                if (!IsAccepted)
                    throw new InvalidOperationException("The semantic output recovery attempt was rejected.");
                return _acceptedValue;
            }
        }

        public TRejection Rejection
        {
            get
            {
                if (IsAccepted)
                    throw new InvalidOperationException("The semantic output recovery attempt was accepted.");
                return _rejection;
            }
        }

        public static SemanticOutputRecoveryAttemptResult<TAccepted, TRejection> Accepted(TAccepted value)
        {
            return new SemanticOutputRecoveryAttemptResult<TAccepted, TRejection>(
                true,
                value,
                default!);
        }

        public static SemanticOutputRecoveryAttemptResult<TAccepted, TRejection> Rejected(TRejection rejection)
        {
            return new SemanticOutputRecoveryAttemptResult<TAccepted, TRejection>(
                false,
                default!,
                rejection);
        }
    }

    public sealed class SemanticOutputRecoveryOutcome<TAccepted, TRejection>
    {
        private readonly TAccepted _acceptedValue;
        private readonly SemanticOutputRecoveryExhaustion<TRejection> _exhaustion;

        private SemanticOutputRecoveryOutcome(
            bool isAccepted,
            TAccepted acceptedValue,
            SemanticOutputRecoveryExhaustion<TRejection> exhaustion)
        {
            IsAccepted = isAccepted;
            _acceptedValue = acceptedValue;
            _exhaustion = exhaustion;
        }

        public bool IsAccepted { get; }

        public TAccepted AcceptedValue
        {
            get
            {
                if (!IsAccepted)
                    throw new InvalidOperationException("Semantic output recovery was exhausted.");
                return _acceptedValue;
            }
        }

        public SemanticOutputRecoveryExhaustion<TRejection> Exhaustion
        {
            get
            {
                if (IsAccepted)
                    throw new InvalidOperationException("Semantic output recovery accepted an output.");
                return _exhaustion;
            }
        }

        public static SemanticOutputRecoveryOutcome<TAccepted, TRejection> Accepted(TAccepted value)
        {
            return new SemanticOutputRecoveryOutcome<TAccepted, TRejection>(
                true,
                value,
                default!);
        }

        public static SemanticOutputRecoveryOutcome<TAccepted, TRejection> Exhausted(
            SemanticOutputRecoveryExhaustion<TRejection> exhaustion)
        {
            if (exhaustion == null)
                throw new ArgumentNullException(nameof(exhaustion));

            return new SemanticOutputRecoveryOutcome<TAccepted, TRejection>(
                false,
                default!,
                exhaustion);
        }
    }

    public sealed class SemanticOutputRecoveryRejection<TRejection>
    {
        public SemanticOutputRecoveryRejection(
            int attempt,
            int totalAttempts,
            TRejection rejection,
            bool isFinalAttempt)
        {
            Attempt = attempt;
            TotalAttempts = totalAttempts;
            Rejection = rejection;
            IsFinalAttempt = isFinalAttempt;
        }

        public int Attempt { get; }

        public int TotalAttempts { get; }

        public TRejection Rejection { get; }

        public bool IsFinalAttempt { get; }
    }

    public sealed class SemanticOutputRecoveryExhaustion<TRejection>
    {
        public SemanticOutputRecoveryExhaustion(int attemptCount, TRejection finalRejection)
        {
            AttemptCount = attemptCount;
            FinalRejection = finalRejection;
        }

        public int AttemptCount { get; }

        public TRejection FinalRejection { get; }
    }
}

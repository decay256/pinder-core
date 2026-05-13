using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// Thrown after the wrapper's one-retry attempt against HTTP 429
    /// also returns 429. The retry policy is: first 429 → sleep
    /// <c>Retry-After</c> (default 1s) and retry once. Second 429 →
    /// throw this exception.
    /// </summary>
    public sealed class RemoteAssetRateLimitException : RemoteAssetException
    {
        /// <summary>
        /// The <c>Retry-After</c> value the server returned on the second
        /// 429, parsed into a <see cref="System.TimeSpan"/>. Falls back to
        /// <see cref="System.TimeSpan.Zero"/> when the header was missing
        /// or unparseable.
        /// </summary>
        public TimeSpan RetryAfter { get; }

        public RemoteAssetRateLimitException(string message, TimeSpan retryAfter, string? responseBody = null, Exception? inner = null)
            : base(message, statusCode: 429, responseBody: responseBody, inner: inner)
        {
            RetryAfter = retryAfter;
        }
    }
}

using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// HTTP 422 with <c>code=metadata_too_large</c> or
    /// <c>code=payload_too_large</c> — the metadata JSON exceeded 4 KiB
    /// or the payload exceeded the configured size cap (default 256 KiB).
    /// See <c>docs/specs/character-asset-vocabulary.md</c> § Publish.
    ///
    /// Stubbed in #853 (write-path only). Throw site lands in #855.
    /// </summary>
    public sealed class RemoteAssetTooLargeException : RemoteAssetException
    {
        /// <summary>
        /// Which side hit the cap: <c>"metadata"</c> or <c>"payload"</c>.
        /// </summary>
        public string Subject { get; }

        public RemoteAssetTooLargeException(
            string message,
            string subject,
            string? responseBody = null,
            Exception? inner = null)
            : base(message, statusCode: 422, responseBody: responseBody, inner: inner)
        {
            Subject = subject ?? string.Empty;
        }

        public override string ToString()
        {
            return $"[RemoteAssetTooLargeFailureDetails] Subject: {Subject}\n{base.ToString()}";
        }
    }
}

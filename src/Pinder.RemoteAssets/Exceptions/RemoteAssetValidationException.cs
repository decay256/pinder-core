using System;
using System.Collections.Generic;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// HTTP 422 — request was syntactically OK but failed server-side
    /// validation. v1 use cases: malformed asset_id in path, oversized
    /// metadata/payload parts (sub-PR #855), invalid cursor (sub-PR
    /// #854 surfaces this as <see cref="RemoteAssetInvalidCursorException"/>).
    ///
    /// Stubbed in #853 — read-path 422 is exceedingly rare in practice
    /// (a malformed UUIDv4 is the only path) and #853's read tests
    /// don't exercise it. Throw site is reserved for #854/#855.
    /// </summary>
    public sealed class RemoteAssetValidationException : RemoteAssetException
    {
        /// <summary>
        /// Server-provided per-field errors. May be empty when the server
        /// only returned a top-level message.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        public RemoteAssetValidationException(
            string message,
            IReadOnlyList<string>? errors = null,
            string? responseBody = null,
            Exception? inner = null)
            : base(message, statusCode: 422, responseBody: responseBody, inner: inner)
        {
            Errors = errors ?? Array.Empty<string>();
        }

        public override string ToString()
        {
            var errorsStr = Errors != null ? string.Join(", ", Errors) : "none";
            return $"[RemoteAssetValidationFailureDetails] Errors: [{errorsStr}]\n{base.ToString()}";
        }
    }
}

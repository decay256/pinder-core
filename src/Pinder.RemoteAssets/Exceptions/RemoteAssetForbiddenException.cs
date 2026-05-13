using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// HTTP 403 — caller is authenticated but is forbidden from this
    /// operation. v1 use cases:
    /// reserved-prefix tag violations (<c>auto-*</c> from any caller,
    /// <c>official-*</c> from a caller not on the allow-list — see
    /// <c>docs/specs/character-asset-vocabulary.md</c>); and write
    /// attempts on assets the caller does not own.
    ///
    /// Stubbed in #853 (read path doesn't reach this). The throw site
    /// lands in #854 (query path) and #855 (write path).
    /// </summary>
    public sealed class RemoteAssetForbiddenException : RemoteAssetException
    {
        public RemoteAssetForbiddenException(string message, string? responseBody = null, Exception? inner = null)
            : base(message, statusCode: 403, responseBody: responseBody, inner: inner)
        {
        }
    }
}

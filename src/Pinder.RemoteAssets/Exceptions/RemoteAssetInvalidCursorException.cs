using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// HTTP 422 with <c>error=invalid_cursor</c> — the cursor passed to
    /// <c>QueryAsync</c> is malformed or expired. See
    /// <c>docs/specs/character-asset-vocabulary.md</c> § Query semantics.
    ///
    /// Stubbed in #853 (no query path yet). Throw site lands in #854.
    /// </summary>
    public sealed class RemoteAssetInvalidCursorException : RemoteAssetException
    {
        public RemoteAssetInvalidCursorException(string message, string? responseBody = null, Exception? inner = null)
            : base(message, statusCode: 422, responseBody: responseBody, inner: inner)
        {
        }
    }
}

using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// Thrown when the server returned 200 OK but the
    /// <c>X-Asset-Metadata</c> response header was missing, not valid
    /// RFC 4648 padded base64, or decoded to bytes that were not valid
    /// metadata JSON. Conceptually a contract violation by the server;
    /// implementation chose a dedicated typed exception rather than
    /// reusing <see cref="RemoteAssetServerException"/> so callers can
    /// distinguish "the server crashed" from "the wire framing was
    /// wrong" without inspecting <see cref="RemoteAssetException.StatusCode"/>.
    ///
    /// The most likely cause is an implementer reaching for
    /// <c>WebEncoders.Base64UrlDecode</c> on either side of the wire —
    /// see the wire spec note in
    /// <c>docs/specs/character-asset-vocabulary.md</c> § Fetch.
    /// </summary>
    public sealed class RemoteAssetMalformedMetadataException : RemoteAssetException
    {
        public RemoteAssetMalformedMetadataException(string message, Exception? inner = null)
            : base(message, statusCode: 200, responseBody: null, inner: inner)
        {
        }
    }
}

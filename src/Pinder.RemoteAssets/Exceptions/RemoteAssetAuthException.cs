using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// Thrown when the server returns HTTP 401 (unauthorized): the bearer
    /// token is missing, expired, or otherwise rejected.
    /// </summary>
    public sealed class RemoteAssetAuthException : RemoteAssetException
    {
        public RemoteAssetAuthException(string message, string? responseBody = null, Exception? inner = null)
            : base(message, statusCode: 401, responseBody: responseBody, inner: inner)
        {
        }
    }
}

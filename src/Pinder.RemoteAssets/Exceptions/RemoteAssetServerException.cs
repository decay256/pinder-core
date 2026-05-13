using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// Thrown when the server returns a 5xx response. Carries the
    /// <see cref="RemoteAssetException.StatusCode"/> and
    /// <see cref="RemoteAssetException.ResponseBody"/> so callers can log
    /// + triage without re-issuing the request.
    /// </summary>
    public sealed class RemoteAssetServerException : RemoteAssetException
    {
        public RemoteAssetServerException(string message, int statusCode, string? responseBody = null, Exception? inner = null)
            : base(message, statusCode: statusCode, responseBody: responseBody, inner: inner)
        {
        }
    }
}

using System;

namespace Pinder.RemoteAssets.Exceptions
{
    /// <summary>
    /// Base class for all typed exceptions thrown by
    /// <c>Pinder.RemoteAssets</c> in response to a server-side error
    /// (a parseable HTTP response). Network-level errors (DNS, socket
    /// reset, timeout) propagate as <see cref="System.Net.Http.HttpRequestException"/>
    /// and are NOT wrapped — see <c>AGENTS.md</c> and the wire contract.
    /// </summary>
    public abstract class RemoteAssetException : Exception
    {
        /// <summary>
        /// HTTP status code from the server response, when the exception
        /// originated from a parseable HTTP response. Zero (default) when
        /// the exception was synthesized from a non-HTTP signal
        /// (e.g. a malformed metadata header).
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// The verbatim response body if one was available and small enough
        /// to attach to the exception. May be empty. Useful for logs and
        /// failure triage; callers SHOULD NOT pattern-match on it.
        /// </summary>
        public string ResponseBody { get; }

        protected RemoteAssetException(string message, int statusCode = 0, string? responseBody = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
        }

        public override string ToString()
        {
            var errorString = $"[RemoteAssetFailureDetails] StatusCode: {StatusCode}";
            if (!string.IsNullOrEmpty(ResponseBody))
            {
                errorString += $", ResponseBody: {ResponseBody}";
            }
            return $"{errorString}\n{base.ToString()}";
        }
    }
}

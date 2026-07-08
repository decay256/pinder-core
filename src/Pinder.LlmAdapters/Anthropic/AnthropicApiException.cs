using System;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Exception thrown when the Anthropic Messages API returns a non-success HTTP status code.
    /// </summary>
    public sealed class AnthropicApiException : Exception
    {
        /// <summary>
        /// The HTTP status code returned by the Anthropic API.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Redacted diagnostic excerpt of the response body (may be null if no body was returned).
        /// </summary>
        public string? ResponseBody { get; }

        internal string? RawResponseBody { get; }

        public AnthropicApiException(int statusCode, string? responseBody)
            : this(
                statusCode,
                responseBody,
                "Anthropic API request failed.")
        {
        }

        public AnthropicApiException(int statusCode, string? responseBody, string failure)
            : base(LlmDiagnosticFormatter.ProviderFailure(
                "anthropic",
                failure,
                statusCode: statusCode,
                body: responseBody))
        {
            StatusCode = statusCode;
            RawResponseBody = responseBody;
            ResponseBody = LlmDiagnosticFormatter.RedactedBodyExcerptOrEmpty(responseBody);
        }
    }
}

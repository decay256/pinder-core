using System;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Exception thrown when the Anthropic Messages API returns a non-success HTTP status code.
    /// </summary>
    public class AnthropicApiException : Exception
    {
        /// <summary>
        /// The HTTP status code returned by the Anthropic API.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// The raw response body returned by the Anthropic API.
        /// </summary>
        public string ResponseBody { get; }

        public AnthropicApiException(int statusCode, string responseBody)
            : base($"Anthropic API error {statusCode}: {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }
}

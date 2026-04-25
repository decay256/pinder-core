using System;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Thrown by streaming LLM transports (and by streaming consumers that
    /// wrap them) when the provider call fails for transport-level reasons:
    /// network errors, non-2xx HTTP, malformed SSE frames, provider errors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Streaming overloads in <c>Pinder.SessionSetup</c> use this type to
    /// signal failure to the caller (the web tier needs to set
    /// <c>setup_error</c> correctly). This is a deliberate departure from
    /// the non-streaming overloads, which swallow transport errors and
    /// return <c>null</c>/empty for parity with the legacy session-runner
    /// helpers.
    /// </para>
    /// <para>
    /// Cancellation is not a transport failure: streaming code must let
    /// <see cref="OperationCanceledException"/> propagate unchanged.
    /// </para>
    /// </remarks>
    public sealed class LlmTransportException : Exception
    {
        public LlmTransportException(string message) : base(message) { }

        public LlmTransportException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}

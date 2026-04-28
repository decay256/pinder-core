using System.Collections.Generic;
using System.Threading;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Streaming counterpart to <see cref="ILlmTransport"/>. Yields raw text
    /// fragments (tokens or token groups) as they arrive from the provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the transport-level contract for any pinder-core consumer that
    /// needs to surface tokens as they are produced (e.g. setup-phase
    /// streaming for matchup analysis and psychological stakes; per-turn
    /// streaming).
    /// </para>
    /// <para>
    /// <b>Error semantics.</b> Implementations MUST translate transport
    /// failures (network errors, non-2xx HTTP, malformed SSE frames, provider
    /// errors) into <see cref="LlmTransportException"/> thrown from the
    /// returned enumerator. Implementations MUST honour the supplied
    /// <see cref="CancellationToken"/> and propagate
    /// <see cref="System.OperationCanceledException"/> on cancellation.
    /// </para>
    /// </remarks>
    public interface IStreamingLlmTransport
    {
        /// <summary>
        /// Open a streaming completion against the provider and yield raw
        /// text fragments as they arrive.
        /// </summary>
        /// <param name="systemPrompt">System-level instructions for the LLM.</param>
        /// <param name="userMessage">User-turn message content.</param>
        /// <param name="temperature">Sampling temperature (default 0.9).</param>
        /// <param name="maxTokens">Maximum tokens for the response (default 1024).</param>
        /// <param name="cancellationToken">Cancellation token; cancelling stops the stream.</param>
        /// <param name="phase">
        /// Optional engine-phase label (see <see cref="LlmPhase"/>). Transports themselves
        /// should ignore the value; decorators (snapshot recorders, telemetry) read it to
        /// classify the exchange without inspecting prompt text. Defaults to <c>null</c>
        /// for backwards compatibility.
        /// </param>
        /// <returns>An async sequence of raw text fragments.</returns>
        /// <exception cref="LlmTransportException">Thrown from the enumerator on transport failure.</exception>
        IAsyncEnumerable<string> SendStreamAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            CancellationToken cancellationToken = default,
            string? phase = null);
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Low-level transport abstraction for LLM providers.
    /// Knows nothing about game logic — just sends a system prompt + user message
    /// and returns raw text. Game-level logic lives in PinderLlmAdapter.
    /// </summary>
    public interface ILlmTransport
    {
        /// <summary>
        /// Send a system prompt and user message to the LLM provider and return the raw text response.
        /// </summary>
        /// <param name="systemPrompt">The system-level context/instructions for the LLM.</param>
        /// <param name="userMessage">The user-turn message content.</param>
        /// <param name="temperature">Sampling temperature (default 0.9).</param>
        /// <param name="maxTokens">Maximum tokens for the response (default 1024).</param>
        /// <param name="phase">
        /// Optional engine-phase label (see <see cref="LlmPhase"/>). Transports themselves
        /// should ignore the value; decorators (snapshot recorders, telemetry) read it to
        /// classify the exchange without inspecting prompt text. Defaults to <c>null</c>
        /// for backwards compatibility — existing callers and ILlmTransport implementations
        /// do not need to change.
        /// </param>
        /// <param name="ct">
        /// Cancellation token (#794). Implementations MUST pass the token through to
        /// the underlying HTTP call so a mid-turn cancel from the engine halts the
        /// in-flight request and propagates <see cref="System.OperationCanceledException"/>.
        /// Defaults to <c>default</c> for backwards compatibility — existing callers
        /// that don't pass a token continue to work unchanged.
        /// </param>
        /// <returns>Raw text response from the LLM.</returns>
        Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default);
    }
}

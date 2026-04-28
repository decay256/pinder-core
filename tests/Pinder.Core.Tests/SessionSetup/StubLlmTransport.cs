using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Tests.SessionSetup
{
    /// <summary>
    /// Minimal non-streaming transport stub. Streaming tests only ever exercise
    /// <see cref="IStreamingLlmTransport"/>, so this stub exists purely to
    /// satisfy the constructor contract on the analyzer / generator.
    /// </summary>
    internal sealed class StubLlmTransport : ILlmTransport
    {
        public Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            string? phase = null) =>
            Task.FromResult(string.Empty);
    }
}

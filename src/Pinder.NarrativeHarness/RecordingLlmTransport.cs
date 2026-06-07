using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// <see cref="ILlmTransport"/> decorator that records every successful
    /// <c>SendAsync</c> call as a <see cref="RawLlmSession"/>, additive to the
    /// normal transport behaviour (the inner transport's response is returned
    /// unchanged).
    ///
    /// <para>
    /// <b>Speaker / Turn derivation:</b> the <c>phase</c> parameter is parsed by
    /// <see cref="RawLlmSession.ParsePhase"/> to determine who spoke and on which
    /// turn. See that method's documentation for the full mapping table.
    /// </para>
    ///
    /// <para>
    /// <b>Error behaviour:</b> if the inner transport throws, the exception is
    /// propagated and <em>no session is recorded</em> for that call. The
    /// HarnessRunner / PursuerActors already catch those exceptions and substitute
    /// an error string; only successful responses enter the sessions list.
    /// </para>
    ///
    /// <para>
    /// Thread safety: the decorator is designed for the sequential harness loop.
    /// The internal list is not synchronized; do not share across threads.
    /// </para>
    ///
    /// <para>
    /// netstandard2.0 / LangVersion 8.0 — normal sealed class, not a C# 9 record.
    /// </para>
    /// </summary>
    public sealed class RecordingLlmTransport : ILlmTransport
    {
        private readonly ILlmTransport _inner;
        private readonly string? _modelLabel;
        private readonly List<RawLlmSession> _sessions = new List<RawLlmSession>();

        /// <param name="inner">The real (or another decorator) transport to delegate to.</param>
        /// <param name="modelLabel">
        /// Optional model identifier to attach to every recorded session.
        /// Pass <c>null</c> if the model is unknown or irrelevant.
        /// </param>
        public RecordingLlmTransport(ILlmTransport inner, string? modelLabel = null)
        {
            _inner      = inner      ?? throw new ArgumentNullException(nameof(inner));
            _modelLabel = modelLabel;
        }

        /// <summary>Every session recorded so far, in call order.</summary>
        public IReadOnlyList<RawLlmSession> Sessions => _sessions;

        /// <inheritdoc />
        public async Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            string? phase = null,
            CancellationToken ct = default)
        {
            // Delegate first — let exceptions propagate (no recording on error).
            string response = await _inner.SendAsync(
                systemPrompt, userMessage, temperature, maxTokens, phase, ct)
                .ConfigureAwait(false);

            var (speaker, turn) = RawLlmSession.ParsePhase(phase);

            _sessions.Add(new RawLlmSession(
                turn:         turn,
                speaker:      speaker,
                model:        _modelLabel,
                systemPrompt: systemPrompt ?? string.Empty,
                userMessage:  userMessage  ?? string.Empty,
                temperature:  temperature,
                maxTokens:    maxTokens,
                rawResponse:  response     ?? string.Empty));

            return response!;
        }
    }
}

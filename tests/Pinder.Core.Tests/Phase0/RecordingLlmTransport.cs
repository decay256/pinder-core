using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// I1/I3: transport-level interceptor that captures every (system, user, response, phase)
    /// tuple sent through <see cref="ILlmTransport.SendAsync"/>. Tests assert structural
    /// properties of the recorded sequence — never reach into adapter internals.
    ///
    /// <para>
    /// I1 is BEHAVIOR-BASED (not storage-based): the test asserts that the
    /// opponent-response prompt sequence preserves continuity (assistant content
    /// from call N appears in call N+1's user message), which is the externally
    /// observable contract of stateful opponent history. Phase 1 (#788) will
    /// move that state into <c>GameSession</c>; this test continues to pass
    /// without modification because it observes only what crosses the transport
    /// wire, not internal field names.
    /// </para>
    /// <para>
    /// Responses are deterministic: the canned-response queue is keyed by
    /// <see cref="LlmPhase"/>. If a phase has no canned response, the transport
    /// returns <see cref="DefaultResponse"/> (or a per-phase default).
    /// </para>
    /// </summary>
    public sealed class RecordingLlmTransport : ILlmTransport
    {
        public sealed record LlmExchange(
            string Phase,
            string SystemPrompt,
            string UserMessage,
            string Response,
            double Temperature,
            int MaxTokens,
            int CallIndex);

        private readonly Dictionary<string, Queue<string>> _cannedResponses;
        private readonly List<LlmExchange> _exchanges = new List<LlmExchange>();
        private readonly Func<string?, string, string, string>? _defaultResponder;
        private int _callCounter;

        /// <summary>
        /// Default response when no canned response is queued for a phase. Tests
        /// that want strict mode can set this to throw.
        /// </summary>
        public string DefaultResponse { get; set; } = "";

        public RecordingLlmTransport()
            : this(defaultResponder: null) { }

        public RecordingLlmTransport(Func<string?, string, string, string>? defaultResponder)
        {
            _cannedResponses = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
            _defaultResponder = defaultResponder;
        }

        public IReadOnlyList<LlmExchange> Exchanges => _exchanges;

        public IReadOnlyList<LlmExchange> ExchangesByPhase(string phase)
        {
            var list = new List<LlmExchange>();
            foreach (var e in _exchanges)
                if (string.Equals(e.Phase, phase, StringComparison.Ordinal)) list.Add(e);
            return list;
        }

        /// <summary>Queue a response for a given phase. FIFO consumed.</summary>
        public RecordingLlmTransport Queue(string phase, string response)
        {
            if (!_cannedResponses.TryGetValue(phase, out var q))
            {
                q = new Queue<string>();
                _cannedResponses[phase] = q;
            }
            q.Enqueue(response);
            return this;
        }

        /// <summary>Convenience overload for the most common phases.</summary>
        public RecordingLlmTransport QueueDialogueOptions(string response)
            => Queue(LlmPhase.DialogueOptions, response);

        public RecordingLlmTransport QueueDelivery(string response)
            => Queue(LlmPhase.Delivery, response);

        public RecordingLlmTransport QueueOpponent(string response)
            => Queue(LlmPhase.OpponentResponse, response);

        public Task<string> SendAsync(
            string systemPrompt,
            string userMessage,
            double temperature = 0.9,
            int maxTokens = 1024,
            string? phase = null,
            System.Threading.CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            string ph = phase ?? LlmPhase.Unknown;

            string response;
            if (_cannedResponses.TryGetValue(ph, out var q) && q.Count > 0)
            {
                response = q.Dequeue();
            }
            else if (_defaultResponder != null)
            {
                response = _defaultResponder(phase, systemPrompt, userMessage) ?? DefaultResponse;
            }
            else
            {
                response = DefaultResponse;
            }

            var ex = new LlmExchange(
                Phase: ph,
                SystemPrompt: systemPrompt ?? "",
                UserMessage: userMessage ?? "",
                Response: response,
                Temperature: temperature,
                MaxTokens: maxTokens,
                CallIndex: _callCounter);
            _callCounter++;
            _exchanges.Add(ex);
            return Task.FromResult(response);
        }
    }
}

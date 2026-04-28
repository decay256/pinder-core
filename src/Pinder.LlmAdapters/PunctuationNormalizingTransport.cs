using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Issue #340: cosmetic post-processing decorator that normalises
    /// space-em-dash-space (` — `) into semicolon-space (`; `) on every
    /// LLM response. Wraps an underlying <see cref="ILlmTransport"/> (and
    /// optionally <see cref="IStreamingLlmTransport"/>) so all engine
    /// consumers — delivery, opponent reply, steering, horniness overlay,
    /// shadow corruption, matchup analysis, psychological stake, etc. —
    /// pick up the cleanup automatically without each call site having to
    /// remember.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces three variants:
    /// <list type="bullet">
    ///   <item><description>ASCII space + em-dash (U+2014) + ASCII space</description></item>
    ///   <item><description>Thin-space (U+2009) + em-dash (U+2014) + thin-space</description></item>
    ///   <item><description>Mixed thin/ascii whitespace + em-dash + thin/ascii whitespace</description></item>
    /// </list>
    /// All three become the canonical <c>"; "</c> (ASCII semicolon + ASCII
    /// space). The no-space form (<c>word—word</c>) is intentionally
    /// preserved — only the surround-with-spaces stylistic variant is
    /// converted, per the issue spec.
    /// </para>
    /// <para>
    /// En-dashes (U+2013) are left untouched: en-dashes carry semantic
    /// meaning (number ranges, "Mon–Fri") and converting them would be
    /// incorrect.
    /// </para>
    /// <para>
    /// The streaming overload normalises each emitted fragment
    /// independently. This is sufficient for the surround-with-spaces
    /// pattern since both spaces and the em-dash are typically tokenised
    /// together by upstream providers; if a future provider splits the
    /// pattern across fragments the worst case is "no normalisation
    /// happened for that one fragment", which matches today's behaviour
    /// without this decorator. We keep it simple — no cross-fragment
    /// buffering — to avoid changing streaming latency characteristics.
    /// </para>
    /// </remarks>
    public sealed class PunctuationNormalizingTransport : ILlmTransport, IStreamingLlmTransport
    {
        // U+2014 = em-dash, U+2009 = thin-space.
        private const char EmDash    = '\u2014';
        private const char ThinSpace = '\u2009';

        private readonly ILlmTransport _inner;
        private readonly IStreamingLlmTransport? _innerStreaming;

        public PunctuationNormalizingTransport(ILlmTransport inner)
            : this(inner, innerStreaming: null) { }

        public PunctuationNormalizingTransport(ILlmTransport inner, IStreamingLlmTransport? innerStreaming)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
            _innerStreaming = innerStreaming;
        }

        /// <summary>
        /// The wrapped non-streaming transport. Exposed so tests — and any
        /// future reflective tooling — can walk the decorator chain to
        /// inspect the underlying provider transport.
        /// </summary>
        public ILlmTransport Inner => _inner;

        /// <summary>
        /// The wrapped streaming transport, or null when this instance was
        /// constructed without one. Same purpose as <see cref="Inner"/>.
        /// </summary>
        public IStreamingLlmTransport? InnerStreaming => _innerStreaming;

        public async Task<string> SendAsync(
            string systemPrompt, string userMessage,
            double temperature = 0.9, int maxTokens = 1024, string? phase = null)
        {
            string raw = await _inner
                .SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase)
                .ConfigureAwait(false);
            return Normalize(raw);
        }

        public async IAsyncEnumerable<string> SendStreamAsync(
            string systemPrompt, string userMessage,
            double temperature = 0.9, int maxTokens = 1024,
            [EnumeratorCancellation] CancellationToken cancellationToken = default,
            string? phase = null)
        {
            if (_innerStreaming == null)
            {
                throw new System.InvalidOperationException(
                    "PunctuationNormalizingTransport was constructed without a streaming inner transport. " +
                    "Use the (ILlmTransport, IStreamingLlmTransport) overload to enable streaming.");
            }

            await foreach (var chunk in _innerStreaming
                .SendStreamAsync(systemPrompt, userMessage, temperature, maxTokens, cancellationToken, phase)
                .ConfigureAwait(false))
            {
                yield return Normalize(chunk);
            }
        }

        /// <summary>
        /// Replace ` — ` (any whitespace + em-dash + whitespace, where each
        /// side is an ASCII space or U+2009 thin-space) with `; `. Leaves
        /// no-space em-dashes (<c>word—word</c>) and en-dashes (U+2013) alone.
        /// </summary>
        public static string Normalize(string? input)
        {
            if (string.IsNullOrEmpty(input)) return input ?? string.Empty;

            // Fast bail: no em-dash anywhere → nothing to do.
            if (input!.IndexOf(EmDash) < 0) return input;

            var sb = new System.Text.StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == EmDash
                    && i > 0 && IsConvertibleSpace(input[i - 1])
                    && i + 1 < input.Length && IsConvertibleSpace(input[i + 1]))
                {
                    // Drop the prior space we already wrote, write '; '
                    if (sb.Length > 0 && IsConvertibleSpace(sb[sb.Length - 1]))
                        sb.Length -= 1;
                    sb.Append("; ");
                    i += 1; // skip the trailing space
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static bool IsConvertibleSpace(char c) => c == ' ' || c == ThinSpace;
    }
}

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Issue #831: post-processing decorator that strips a leading
    /// <c>&lt;thinking&gt;...&lt;/thinking&gt;</c> or
    /// <c>&lt;reasoning&gt;...&lt;/reasoning&gt;</c> block from every LLM
    /// response — both non-streaming and streaming — by wrapping the
    /// underlying transport(s).
    ///
    /// Replaces the per-callsite <see cref="InlineThinkingStripper.Strip"/>
    /// pattern: instead of every prose-only consumer (delivery, opponent
    /// reply, steering, horniness/shadow/trap overlays, stake, outfit,
    /// interest beat, …) remembering to call the stripper, the strip
    /// happens at the transport boundary so all consumers automatically
    /// pick up the cleanup. New prose-only call sites added later cannot
    /// silently leak thinking blocks into player-visible text or the
    /// persistent system prompt.
    ///
    /// Order in the decorator stack: this decorator should sit ABOVE
    /// (outermost on the transformation side) any other text-mutating
    /// decorators (e.g. <see cref="PunctuationNormalizingTransport"/>),
    /// and ABOVE any snapshot/audit recorder, so the recorded audit log
    /// captures the cleaned text the player actually sees and replay /
    /// resimulation reproduces it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Non-streaming.</b> The full response is available, so
    /// <see cref="InlineThinkingStripper.Strip"/> runs once on the
    /// returned string.
    /// </para>
    /// <para>
    /// <b>Streaming.</b> A leading thinking block can span multiple
    /// fragments (the opening <c>&lt;thinking&gt;</c> arrives in fragment N,
    /// the closing <c>&lt;/thinking&gt;</c> in fragment N+M). A naive
    /// per-fragment strip would not catch this. Instead we use
    /// <b>buffer-then-flush</b> semantics:
    /// </para>
    /// <list type="number">
    ///   <item>Accumulate fragments into a buffer while the buffer's
    ///         content might still be a leading thinking block (i.e. it
    ///         starts with whitespace + an opening tag prefix).</item>
    ///   <item>If the closing tag arrives, run
    ///         <see cref="InlineThinkingStripper.Strip"/> on the buffer
    ///         and yield the result; for the remaining fragments yield
    ///         them through unchanged (one block max, per stripper
    ///         contract).</item>
    ///   <item>If the buffer accumulates content that is definitively NOT
    ///         a leading thinking block (the leading characters are not
    ///         whitespace + <c>&lt;</c>), flush the buffer as-is and
    ///         pass through every subsequent fragment unchanged.</item>
    ///   <item>A safety cap (<see cref="MaxBufferChars"/>) prevents an
    ///         unbounded-thinking-block from holding the entire stream
    ///         hostage — at the cap we flush as-is and stop trying to
    ///         strip.</item>
    /// </list>
    /// <para>
    /// Trade-off: the leading fragments of a thinking-prefixed stream
    /// arrive at the consumer slightly later (after the closing tag is
    /// seen), but for non-thinking-prefixed responses there is no
    /// observable latency penalty — the buffer flushes on the first
    /// fragment whose content rules out a leading tag.
    /// </para>
    /// </remarks>
    public sealed class ThinkingStrippingLlmTransport : ILlmTransport, IStreamingLlmTransport
    {
        /// <summary>
        /// Maximum characters to buffer in the streaming code path while
        /// waiting for a closing thinking tag. If this cap is hit without
        /// a closing tag the buffer is flushed as-is (no strip applied)
        /// and subsequent fragments pass through unchanged. The cap is
        /// generous enough to cover realistic thinking blocks (we have
        /// observed up to ~10kB) while still bounding memory / latency
        /// in a runaway-model scenario.
        /// </summary>
        public const int MaxBufferChars = 64 * 1024;

        private readonly ILlmTransport _inner;
        private readonly IStreamingLlmTransport? _innerStreaming;

        public ThinkingStrippingLlmTransport(ILlmTransport inner)
            : this(inner, innerStreaming: null) { }

        public ThinkingStrippingLlmTransport(ILlmTransport inner, IStreamingLlmTransport? innerStreaming)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
            _innerStreaming = innerStreaming;
        }

        /// <summary>
        /// The wrapped non-streaming transport. Mirrors
        /// <see cref="PunctuationNormalizingTransport.Inner"/> — exposed
        /// for tests and any future reflective tooling that walks the
        /// decorator chain.
        /// </summary>
        public ILlmTransport Inner => _inner;

        /// <summary>
        /// The wrapped streaming transport, or null when this instance
        /// was constructed without one.
        /// </summary>
        public IStreamingLlmTransport? InnerStreaming => _innerStreaming;

        public async Task<string> SendAsync(
            string systemPrompt, string userMessage,
            double temperature = 0.9, int maxTokens = 1024, string? phase = null,
            CancellationToken ct = default)
        {
            string raw = await _inner
                .SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct)
                .ConfigureAwait(false);
            return InlineThinkingStripper.Strip(raw);
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
                    "ThinkingStrippingLlmTransport was constructed without a streaming inner transport. " +
                    "Use the (ILlmTransport, IStreamingLlmTransport) overload to enable streaming.");
            }

            // State machine for the streaming buffer:
            //   Buffering  — still might be a leading thinking block;
            //                accumulate fragments and don't yield yet.
            //   Passthrough — confirmed not (or no longer) a leading
            //                 thinking block; yield each fragment as it
            //                 arrives.
            var buffer = new System.Text.StringBuilder();
            bool buffering = true;

            await foreach (var chunk in _innerStreaming
                .SendStreamAsync(systemPrompt, userMessage, temperature, maxTokens, cancellationToken, phase)
                .ConfigureAwait(false))
            {
                if (!buffering)
                {
                    // Already past the leading-block decision point —
                    // pass through unchanged.
                    yield return chunk;
                    continue;
                }

                buffer.Append(chunk);

                // Decision: is the buffer still a candidate for "starts
                // with a thinking block"?
                StreamingDecision decision = ClassifyStreamingBuffer(buffer);

                switch (decision)
                {
                    case StreamingDecision.NotALeadingBlock:
                        // The leading characters rule out a thinking
                        // tag. Flush the entire buffer as-is and switch
                        // to passthrough.
                        if (buffer.Length > 0)
                            yield return buffer.ToString();
                        buffer.Clear();
                        buffering = false;
                        break;

                    case StreamingDecision.LeadingBlockClosed:
                        // The buffer contains a complete leading
                        // <thinking>...</thinking> (or <reasoning>...).
                        // Strip and flush the remainder; switch to
                        // passthrough.
                        string stripped = InlineThinkingStripper.Strip(buffer.ToString());
                        if (stripped.Length > 0)
                            yield return stripped;
                        buffer.Clear();
                        buffering = false;
                        break;

                    case StreamingDecision.MaybeLeadingBlock:
                        // Still ambiguous — opening tag may have been
                        // seen but no closing tag yet, or buffer is too
                        // short to decide. Keep buffering, but bail out
                        // if the buffer has grown past the safety cap.
                        if (buffer.Length >= MaxBufferChars)
                        {
                            yield return buffer.ToString();
                            buffer.Clear();
                            buffering = false;
                        }
                        break;
                }
            }

            // Stream ended while still buffering — flush whatever's
            // there. If the entire response was a thinking block with
            // no closing tag (malformed), flush as-is rather than
            // silently dropping content.
            if (buffer.Length > 0)
            {
                // One last strip attempt in case the closing tag did
                // arrive in the final fragment.
                string final = InlineThinkingStripper.Strip(buffer.ToString());
                if (final.Length > 0)
                    yield return final;
            }
        }

        /// <summary>
        /// Streaming buffer classification states. Exposed as
        /// <c>internal</c> so the decorator-tests assembly can target
        /// the classifier directly without driving the full async
        /// streaming path.
        /// </summary>
        internal enum StreamingDecision
        {
            /// <summary>Leading characters definitely don't start a
            /// thinking/reasoning tag — flush buffer, switch to
            /// passthrough.</summary>
            NotALeadingBlock,

            /// <summary>Leading thinking/reasoning block has a matching
            /// closing tag — strip and flush.</summary>
            LeadingBlockClosed,

            /// <summary>Buffer is still ambiguous (might be a leading
            /// tag, opening tag not yet complete, or closing tag not
            /// yet seen).</summary>
            MaybeLeadingBlock
        }

        // The longest opening tag prefix we care about, in lowercase
        // (compared against the buffer content with leading whitespace
        // already trimmed).
        private static readonly string[] OpeningTagNames = { "thinking", "reasoning" };

        /// <summary>
        /// Classify the current streaming buffer for the leading-block
        /// state machine. Internal to allow direct unit-testing.
        /// </summary>
        internal static StreamingDecision ClassifyStreamingBuffer(System.Text.StringBuilder buf)
        {
            // Skip leading whitespace — the stripper's regex tolerates
            // whitespace before the tag, so we should too.
            int i = 0;
            while (i < buf.Length && char.IsWhiteSpace(buf[i])) i++;

            // All-whitespace buffer so far → still ambiguous.
            if (i >= buf.Length) return StreamingDecision.MaybeLeadingBlock;

            // First non-whitespace character is not '<' → can't be a
            // leading thinking tag.
            if (buf[i] != '<') return StreamingDecision.NotALeadingBlock;

            // We have whitespace? + '<'. Try to extend to a known tag
            // name. The grammar at this point is: '<' \s* tagname \s* '>'
            // followed by body, followed by '<' \s* '/' \s* tagname \s*
            // '>'.
            int p = i + 1;
            // Optional whitespace inside the tag.
            while (p < buf.Length && char.IsWhiteSpace(buf[p])) p++;
            if (p >= buf.Length) return StreamingDecision.MaybeLeadingBlock;

            // Match one of the known tag names (case-insensitive).
            string? matchedTag = null;
            int afterName = -1;
            foreach (string name in OpeningTagNames)
            {
                if (BufferStartsWithIgnoreCase(buf, p, name))
                {
                    matchedTag = name;
                    afterName = p + name.Length;
                    break;
                }
                // Partial match (buffer ends mid-name)?
                if (PartialMatchIgnoreCase(buf, p, name))
                {
                    return StreamingDecision.MaybeLeadingBlock;
                }
            }
            if (matchedTag == null)
            {
                // First non-whitespace char was '<', but what follows
                // isn't a tag name we care about (and isn't a partial
                // prefix of one). Could be `<br>` or a literal
                // angle-bracket inside dialogue. Pass through.
                return StreamingDecision.NotALeadingBlock;
            }

            // After the tag name we expect optional whitespace then '>'.
            int q = afterName;
            while (q < buf.Length && char.IsWhiteSpace(buf[q])) q++;
            if (q >= buf.Length) return StreamingDecision.MaybeLeadingBlock;
            if (buf[q] != '>')
            {
                // The character after a known tag name isn't whitespace
                // or '>' (e.g. "<thinkingfoo"). Not a real opening tag.
                return StreamingDecision.NotALeadingBlock;
            }

            // Opening tag confirmed. Now scan for the matching closing
            // tag '</tagname>' (whitespace tolerant).
            int searchStart = q + 1;
            int closeIdx = FindClosingTag(buf, searchStart, matchedTag);
            return closeIdx >= 0
                ? StreamingDecision.LeadingBlockClosed
                : StreamingDecision.MaybeLeadingBlock;
        }

        /// <summary>
        /// Returns true if the buffer at position <paramref name="start"/>
        /// begins with <paramref name="needle"/> (case-insensitive,
        /// full-length). False if the buffer ends before the needle does.
        /// </summary>
        private static bool BufferStartsWithIgnoreCase(System.Text.StringBuilder buf, int start, string needle)
        {
            if (start + needle.Length > buf.Length) return false;
            for (int k = 0; k < needle.Length; k++)
            {
                if (char.ToLowerInvariant(buf[start + k]) != needle[k]) return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if the buffer at position <paramref name="start"/>
        /// is a strict prefix of <paramref name="needle"/> — i.e. the
        /// buffer ends inside the needle, every character so far matches.
        /// </summary>
        private static bool PartialMatchIgnoreCase(System.Text.StringBuilder buf, int start, string needle)
        {
            int avail = buf.Length - start;
            if (avail <= 0) return false;
            if (avail >= needle.Length) return false; // full match handled separately
            for (int k = 0; k < avail; k++)
            {
                if (char.ToLowerInvariant(buf[start + k]) != needle[k]) return false;
            }
            return true;
        }

        /// <summary>
        /// Find the index of the matching closing tag <c>&lt;/name&gt;</c>
        /// (whitespace-tolerant inside the tag) in the buffer, scanning
        /// from <paramref name="start"/>. Returns -1 if not found.
        /// </summary>
        private static int FindClosingTag(System.Text.StringBuilder buf, int start, string name)
        {
            // Closing tag grammar: '<' \s* '/' \s* name \s* '>'
            for (int idx = start; idx < buf.Length; idx++)
            {
                if (buf[idx] != '<') continue;
                int p = idx + 1;
                while (p < buf.Length && char.IsWhiteSpace(buf[p])) p++;
                if (p >= buf.Length) return -1;
                if (buf[p] != '/') continue;
                p++;
                while (p < buf.Length && char.IsWhiteSpace(buf[p])) p++;
                if (!BufferStartsWithIgnoreCase(buf, p, name)) continue;
                int q = p + name.Length;
                while (q < buf.Length && char.IsWhiteSpace(buf[q])) q++;
                if (q >= buf.Length) return -1;
                if (buf[q] != '>') continue;
                return q;
            }
            return -1;
        }
    }
}

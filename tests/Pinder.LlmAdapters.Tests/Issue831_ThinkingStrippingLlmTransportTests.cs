using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #831: regression tests for ThinkingStrippingLlmTransport — the
    /// decorator that strips a leading &lt;thinking&gt;/&lt;reasoning&gt;
    /// block from every LLM response (non-streaming and streaming) at the
    /// transport boundary so per-callsite calls are no longer needed.
    /// </summary>
    public class Issue831_ThinkingStrippingLlmTransportTests
    {
        // ── Non-streaming decorator behaviour ────────────────────────────

        [Fact]
        public async Task SendAsync_strips_leading_thinking_block()
        {
            var inner = new RecordingTransport("<thinking>plan</thinking>real reply");
            var sut = new ThinkingStrippingLlmTransport(inner);

            var result = await sut.SendAsync("sys", "user", phase: LlmPhase.Delivery);

            Assert.Equal("real reply", result);
            Assert.Equal("sys", inner.LastSystem);
            Assert.Equal("user", inner.LastUser);
            Assert.Equal(LlmPhase.Delivery, inner.LastPhase);
        }

        [Fact]
        public async Task SendAsync_strips_leading_reasoning_block()
        {
            var inner = new RecordingTransport("<reasoning>step 1\nstep 2</reasoning>\nokay sure.");
            var sut = new ThinkingStrippingLlmTransport(inner);

            var result = await sut.SendAsync("sys", "user");

            Assert.Equal("okay sure.", result);
        }

        [Fact]
        public async Task SendAsync_passes_through_response_without_block()
        {
            var inner = new RecordingTransport("just a normal response");
            var sut = new ThinkingStrippingLlmTransport(inner);

            var result = await sut.SendAsync("sys", "user");

            Assert.Equal("just a normal response", result);
        }

        [Fact]
        public async Task SendAsync_threads_phase_and_cancellation_token_through()
        {
            var inner = new RecordingTransport("<thinking>x</thinking>y");
            var sut = new ThinkingStrippingLlmTransport(inner);
            var cts = new CancellationTokenSource();

            await sut.SendAsync("sys", "user", phase: LlmPhase.Steering, ct: cts.Token);

            Assert.Equal(LlmPhase.Steering, inner.LastPhase);
        }

        // ── Streaming decorator behaviour ────────────────────────────────

        [Fact]
        public async Task SendStreamAsync_passes_fragments_through_when_no_thinking_block()
        {
            var fragments = new[] { "hello ", "world", "!" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            // Concatenation matches the original fragment stream content.
            // (The decorator may flush as a single buffered chunk for the
            // first fragment that rules out a leading tag — we only care
            // that no content was lost or mangled.)
            Assert.Equal("hello world!", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_strips_leading_block_when_closing_tag_in_first_fragment()
        {
            var fragments = new[] { "<thinking>planning</thinking>real reply" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            Assert.Equal("real reply", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_strips_leading_block_spanning_two_fragments()
        {
            var fragments = new[] { "<thinking>first half ", "second half</thinking>actual answer" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            Assert.Equal("actual answer", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_strips_leading_block_with_opening_tag_split_across_fragments()
        {
            var fragments = new[] { "<thi", "nking>plan</thinking>real reply" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            Assert.Equal("real reply", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_passes_through_when_first_char_rules_out_leading_tag()
        {
            // First fragment starts with 'H' — definitely not a thinking
            // tag. Decorator should flush immediately and switch to
            // passthrough; subsequent fragments arrive unchanged.
            var fragments = new[] { "Hello ", "<thinking>not-leading</thinking>", " world" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            // Mid-stream thinking tags are preserved (the stripper only
            // strips leading blocks, by design — see InlineThinkingStripper
            // class doc).
            Assert.Equal("Hello <thinking>not-leading</thinking> world", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_handles_unterminated_thinking_block_gracefully()
        {
            // Malformed: opening tag, no closing tag. Stream ends. The
            // decorator must flush whatever's there rather than dropping
            // it silently — under-stripping is preferable to swallowing
            // the entire response.
            var fragments = new[] { "<thinking>this never closes" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            // Unterminated → flush as-is. The Strip helper's regex won't
            // match (no closing tag), so the original buffer is returned.
            Assert.Equal("<thinking>this never closes", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_handles_leading_whitespace_before_thinking_block()
        {
            var fragments = new[] { "  \n<thinking>plan</thinking>greeting" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            var got = await CollectAsync(sut.SendStreamAsync("sys", "user"));

            Assert.Equal("greeting", string.Concat(got));
        }

        [Fact]
        public async Task SendStreamAsync_throws_when_no_streaming_inner_provided()
        {
            var inner = new RecordingTransport("ignored");
            var sut = new ThinkingStrippingLlmTransport(inner); // no streaming inner

            await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
            {
                await foreach (var _ in sut.SendStreamAsync("sys", "user")) { }
            });
        }

        [Fact]
        public void Dispose_disposes_same_inner_transport_once()
        {
            var inner = new RecordingTransport("ignored");
            var sut = new ThinkingStrippingLlmTransport(inner, inner);

            sut.Dispose();
            sut.Dispose();

            Assert.Equal(1, inner.DisposeCount);
        }

        [Fact]
        public void Dispose_disposes_separate_inner_transports_once()
        {
            var inner = new RecordingTransport("ignored");
            var streaming = new DisposableStreamingTransport();
            var sut = new ThinkingStrippingLlmTransport(inner, streaming);

            sut.Dispose();
            sut.Dispose();

            Assert.Equal(1, inner.DisposeCount);
            Assert.Equal(1, streaming.DisposeCount);
        }

        // ── Streaming buffer classifier (internal state machine) ─────────

        [Fact]
        public void Classifier_treats_empty_buffer_as_ambiguous()
        {
            var sb = new System.Text.StringBuilder();
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.MaybeLeadingBlock,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        [Fact]
        public void Classifier_treats_nonbracket_first_char_as_not_a_block()
        {
            var sb = new System.Text.StringBuilder("Hello");
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.NotALeadingBlock,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        [Fact]
        public void Classifier_treats_partial_opening_tag_as_ambiguous()
        {
            var sb = new System.Text.StringBuilder("<thi");
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.MaybeLeadingBlock,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        [Fact]
        public void Classifier_treats_unknown_tag_as_not_a_block()
        {
            // <br> isn't a thinking tag; pass through.
            var sb = new System.Text.StringBuilder("<br>hello");
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.NotALeadingBlock,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        [Fact]
        public void Classifier_treats_thinking_open_no_close_as_ambiguous()
        {
            var sb = new System.Text.StringBuilder("<thinking>still going");
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.MaybeLeadingBlock,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        [Fact]
        public void Classifier_recognises_complete_leading_thinking_block()
        {
            var sb = new System.Text.StringBuilder("<thinking>x</thinking>more");
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.LeadingBlockClosed,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        [Fact]
        public void Classifier_recognises_complete_leading_reasoning_block()
        {
            var sb = new System.Text.StringBuilder("<reasoning>r</reasoning>more");
            Assert.Equal(
                ThinkingStrippingLlmTransport.StreamingDecision.LeadingBlockClosed,
                ThinkingStrippingLlmTransport.ClassifyStreamingBuffer(sb));
        }

        // ── Test transport (mirror of #340 RecordingTransport) ───────────

        private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
        {
            var got = new List<string>();
            await foreach (var chunk in source)
                got.Add(chunk);
            return got;
        }

        private sealed class RecordingTransport : ILlmTransport, IStreamingLlmTransport, System.IDisposable
        {
            private readonly string _response;
            private readonly string[]? _fragments;

            public string? LastSystem { get; private set; }
            public string? LastUser { get; private set; }
            public string? LastPhase { get; private set; }
            public int DisposeCount { get; private set; }

            public RecordingTransport(string response, string[]? streamingFragments = null)
            {
                _response = response;
                _fragments = streamingFragments;
            }

            public Task<string> SendAsync(string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024, string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                LastSystem = systemPrompt;
                LastUser = userMessage;
                LastPhase = phase;
                return Task.FromResult(_response);
            }

#pragma warning disable CS1998 // async without await — yield-based async iterator
            public async IAsyncEnumerable<string> SendStreamAsync(
                string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024,
                [EnumeratorCancellation] CancellationToken cancellationToken = default,
                string? phase = null)
            {
                LastSystem = systemPrompt;
                LastUser = userMessage;
                LastPhase = phase;
                if (_fragments == null) yield break;
                foreach (var f in _fragments) yield return f;
            }
#pragma warning restore CS1998

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class DisposableStreamingTransport : IStreamingLlmTransport, System.IDisposable
        {
            public int DisposeCount { get; private set; }

#pragma warning disable CS1998 // async without await - yield-based async iterator
            public async IAsyncEnumerable<string> SendStreamAsync(
                string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024,
                [EnumeratorCancellation] CancellationToken cancellationToken = default,
                string? phase = null)
            {
                yield break;
            }
#pragma warning restore CS1998

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}

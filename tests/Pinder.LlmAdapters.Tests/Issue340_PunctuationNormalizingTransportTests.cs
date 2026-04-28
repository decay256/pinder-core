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
    /// Issue #340: regression tests for the punctuation-normalising
    /// decorator transport that maps space-em-dash-space patterns into
    /// "; " on every LLM response.
    /// </summary>
    public class Issue340_PunctuationNormalizingTransportTests
    {
        // ── Pure-function tests ──────────────────────────────────────────

        [Theory]
        [InlineData("hello \u2014 world",                "hello; world")] // ASCII space + em + ASCII space
        [InlineData("hello\u2009\u2014\u2009world",       "hello; world")] // thin-space + em + thin-space
        [InlineData("hello \u2014\u2009world",             "hello; world")] // mixed: ASCII left, thin right
        [InlineData("hello\u2009\u2014 world",             "hello; world")] // mixed: thin left, ASCII right
        [InlineData("a \u2014 b \u2014 c",                "a; b; c")]      // multiple occurrences
        public void Normalize_replaces_space_em_space(string input, string expected)
        {
            Assert.Equal(expected, PunctuationNormalizingTransport.Normalize(input));
        }

        [Theory]
        [InlineData("word\u2014word",        "word\u2014word")]   // no surrounding spaces \u2192 unchanged
        [InlineData("foo\u2014bar baz",       "foo\u2014bar baz")] // bare em-dash inside compound \u2192 unchanged
        [InlineData("Mon\u2013Fri",           "Mon\u2013Fri")]    // en-dash range \u2192 unchanged
        [InlineData("nothing to do",          "nothing to do")]   // no em-dash at all
        [InlineData("",                       "")]               // empty
        public void Normalize_leaves_unsurrounded_dashes_alone(string input, string expected)
        {
            Assert.Equal(expected, PunctuationNormalizingTransport.Normalize(input));
        }

        [Fact]
        public void Normalize_handles_null_as_empty()
        {
            Assert.Equal(string.Empty, PunctuationNormalizingTransport.Normalize(null));
        }

        // ── Decorator behaviour tests ────────────────────────────────────

        [Fact]
        public async Task SendAsync_wraps_inner_response()
        {
            var inner = new RecordingTransport("hello \u2014 world");
            var sut = new PunctuationNormalizingTransport(inner);

            var result = await sut.SendAsync("sys", "user", phase: LlmPhase.Delivery);

            Assert.Equal("hello; world", result);
            Assert.Equal("sys", inner.LastSystem);
            Assert.Equal("user", inner.LastUser);
            Assert.Equal(LlmPhase.Delivery, inner.LastPhase);
        }

        [Fact]
        public async Task SendStreamAsync_normalises_each_fragment()
        {
            var fragments = new[] { "hello \u2014 ", "world\u2009\u2014\u2009ok" };
            var inner = new RecordingTransport("ignored", streamingFragments: fragments);
            var sut = new PunctuationNormalizingTransport(inner, inner);

            var got = new List<string>();
            await foreach (var chunk in sut.SendStreamAsync("sys", "user"))
                got.Add(chunk);

            Assert.Equal(new[] { "hello; ", "world; ok" }, got);
        }

        // ── Test transport ───────────────────────────────────────────────

        private sealed class RecordingTransport : ILlmTransport, IStreamingLlmTransport
        {
            private readonly string _response;
            private readonly string[]? _fragments;

            public string? LastSystem { get; private set; }
            public string? LastUser { get; private set; }
            public string? LastPhase { get; private set; }

            public RecordingTransport(string response, string[]? streamingFragments = null)
            {
                _response = response;
                _fragments = streamingFragments;
            }

            public Task<string> SendAsync(string systemPrompt, string userMessage,
                double temperature = 0.9, int maxTokens = 1024, string? phase = null)
            {
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
        }
    }
}

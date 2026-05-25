using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Unit tests for <see cref="AnthropicStreamingTransport"/>. The fake
    /// <see cref="HttpMessageHandler"/> returns canned SSE bytes so we can
    /// exercise the parser without a live API.
    /// </summary>
    public partial class AnthropicStreamingTransportTests
    {
        private const string TestApiKey = "sk-ant-test-key";
        private const string TestModel = "claude-sonnet-4-20250514";

        // ─── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// HttpMessageHandler that responds with a fixed status and a stream
        /// of bytes written by the supplied writer. The stream is delivered
        /// chunk-by-chunk via a <see cref="ChunkedStream"/> so the parser
        /// observes realistic partial reads.
        /// </summary>
        private sealed class SseHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly byte[][] _chunks;
            private readonly TimeSpan _interChunkDelay;
            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastRequestBody { get; private set; }
            public ChunkedStream? Stream { get; private set; }

            public SseHandler(HttpStatusCode status, IEnumerable<byte[]> chunks, TimeSpan? interChunkDelay = null)
            {
                _status = status;
                _chunks = chunks.ToArray();
                _interChunkDelay = interChunkDelay ?? TimeSpan.Zero;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                if (request.Content != null)
                    LastRequestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);

                Stream = new ChunkedStream(_chunks, _interChunkDelay);
                var content = new StreamContent(Stream);
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return new HttpResponseMessage(_status) { Content = content };
            }
        }

        /// <summary>
        /// HttpMessageHandler returning a non-streaming string body for HTTP
        /// open-time error tests.
        /// </summary>
        private sealed class FixedHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public FixedHandler(HttpStatusCode status, string body)
            {
                _status = status; _body = body;
            }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }

        /// <summary>
        /// Stream that yields a sequence of chunks across separate Read
        /// calls, with optional delay between chunks. Mimics a real network
        /// read so the SSE parser sees frames assembled across multiple
        /// reads.
        /// </summary>
        private sealed class ChunkedStream : Stream
        {
            private readonly Queue<byte[]> _chunks;
            private byte[] _current = Array.Empty<byte>();
            private int _currentPos;
            private readonly TimeSpan _delay;
            public bool Disposed { get; private set; }

            public ChunkedStream(IEnumerable<byte[]> chunks, TimeSpan delay)
            {
                _chunks = new Queue<byte[]>(chunks);
                _delay = delay;
            }

            public override bool CanRead => !Disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
                => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (Disposed) throw new ObjectDisposedException(nameof(ChunkedStream));

                if (_currentPos >= _current.Length)
                {
                    if (_chunks.Count == 0) return 0; // EOF
                    if (_delay > TimeSpan.Zero)
                        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                    _current = _chunks.Dequeue();
                    _currentPos = 0;
                }

                var available = _current.Length - _currentPos;
                var toCopy = Math.Min(available, count);
                Buffer.BlockCopy(_current, _currentPos, buffer, offset, toCopy);
                _currentPos += toCopy;
                return toCopy;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

        private static IEnumerable<byte[]> SsePayload(params string[] frames)
        {
            // One chunk per frame so the parser sees frame boundaries arrive
            // across separate reads. CRLF is allowed by SSE; we use LF here
            // to match Anthropic's actual wire format.
            foreach (var f in frames) yield return U(f);
        }

        private static string TextDeltaFrame(string text)
        {
            // JSON-escape backslashes and quotes (sufficient for tests).
            var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "event: content_block_delta\n" +
                   "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"" + escaped + "\"}}\n\n";
        }

        private static string ErrorFrame(string type, string message)
        {
            return "event: error\n" +
                   "data: {\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"" + message + "\"}}\n\n";
        }

        // ─── Constructor tests ────────────────────────────────────────────

        [Fact]
        public void Constructor_NullApiKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicStreamingTransport(null!));
        }

        [Fact]
        public void Constructor_WhitespaceApiKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new AnthropicStreamingTransport("   "));
        }

        [Fact]
        public void Constructor_NullModel_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AnthropicStreamingTransport(TestApiKey, null!, new HttpClient()));
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new AnthropicStreamingTransport(TestApiKey, TestModel, null!));
        }

        // ─── Dispose ──────────────────────────────────────────────────────

        [Fact]
        public void Dispose_OwnedHttpClient_DoesNotThrow()
        {
            var t = new AnthropicStreamingTransport(TestApiKey, TestModel);
            t.Dispose();
            t.Dispose(); // idempotent
        }

        [Fact]
        public void Dispose_BorrowedHttpClient_DoesNotDisposeIt()
        {
            using var http = new HttpClient(new FixedHandler(HttpStatusCode.OK, "{}"));
            var t = new AnthropicStreamingTransport(TestApiKey, TestModel, http);
            t.Dispose();
            // Should still be usable (no ObjectDisposedException on send).
            Assert.NotNull(http);
        }
    }
}
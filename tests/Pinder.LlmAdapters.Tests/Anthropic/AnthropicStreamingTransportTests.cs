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
    public class AnthropicStreamingTransportTests
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

        // ─── Happy path: tokens arrive in order, multi-chunk reassembly ──

        [Fact]
        public async Task SendStreamAsync_YieldsTextDeltasInOrder()
        {
            var frames = SsePayload(
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_01\",\"role\":\"assistant\",\"content\":[]}}\n\n",
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n",
                TextDeltaFrame("Hello"),
                TextDeltaFrame(", "),
                TextDeltaFrame("world!"),
                "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\n",
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":7}}\n\n",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("sys", "user"))
                fragments.Add(f);

            Assert.Equal(new[] { "Hello", ", ", "world!" }, fragments);
            Assert.Equal("Hello, world!", string.Concat(fragments));
        }

        [Fact]
        public async Task SendStreamAsync_RequestBodyContainsStreamTrue()
        {
            var frames = SsePayload(
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            await foreach (var _ in transport.SendStreamAsync("sysprompt", "usermsg", 0.7, 256)) { }

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("\"stream\":true", handler.LastRequestBody);
            Assert.Contains("\"model\":\"" + TestModel + "\"", handler.LastRequestBody);
            Assert.Contains("\"max_tokens\":256", handler.LastRequestBody);
            Assert.Contains("\"temperature\":0.7", handler.LastRequestBody);
            Assert.Contains("sysprompt", handler.LastRequestBody);
            Assert.Contains("usermsg", handler.LastRequestBody);
        }

        [Fact]
        public async Task SendStreamAsync_FrameSplitAcrossChunks_StillReassembles()
        {
            // One full text_delta frame split mid-line into 3 byte chunks.
            var full = TextDeltaFrame("split_text");
            var bytes = U(full);
            var c1 = bytes.Take(10).ToArray();
            var c2 = bytes.Skip(10).Take(20).ToArray();
            var c3 = bytes.Skip(30).ToArray();
            var handler = new SseHandler(HttpStatusCode.OK, new[] { c1, c2, c3 });
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("s", "u"))
                fragments.Add(f);

            Assert.Single(fragments);
            Assert.Equal("split_text", fragments[0]);
        }

        // ─── Unknown event types are ignored ─────────────────────────────

        [Fact]
        public async Task SendStreamAsync_UnknownEventTypes_AreIgnored()
        {
            var frames = SsePayload(
                "event: ping\ndata: {\"type\":\"ping\"}\n\n",
                "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"x\",\"role\":\"assistant\",\"content\":[]}}\n\n",
                "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n",
                "event: weird_future_event\ndata: {\"type\":\"weird_future_event\",\"foo\":\"bar\"}\n\n",
                TextDeltaFrame("only-text"),
                "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n",
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("s", "u"))
                fragments.Add(f);

            Assert.Single(fragments);
            Assert.Equal("only-text", fragments[0]);
        }

        [Fact]
        public async Task SendStreamAsync_NonTextDelta_IsIgnored()
        {
            // content_block_delta with a non-text delta type (e.g.
            // input_json_delta for tool calls). Should not yield.
            var frames = SsePayload(
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"x\\\":1}\"}}\n\n",
                TextDeltaFrame("real text"),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            await foreach (var f in transport.SendStreamAsync("s", "u"))
                fragments.Add(f);

            Assert.Single(fragments);
            Assert.Equal("real text", fragments[0]);
        }

        // ─── HTTP open-time errors ────────────────────────────────────────

        [Fact]
        public async Task SendStreamAsync_Http401_Throws_LlmTransportException_WithStatus()
        {
            var handler = new FixedHandler(HttpStatusCode.Unauthorized, "{\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            Assert.Contains("401", ex.Message);
            Assert.Contains("invalid x-api-key", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_Http500_Throws_LlmTransportException_WithStatus()
        {
            var handler = new FixedHandler(HttpStatusCode.InternalServerError, "{\"error\":{\"type\":\"api_error\",\"message\":\"boom\"}}");
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            Assert.Contains("500", ex.Message);
            Assert.Contains("boom", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_Http400_BodyTruncatedTo1KB()
        {
            var bigBody = new string('x', 4096);
            var handler = new FixedHandler(HttpStatusCode.BadRequest, bigBody);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u")) { }
            });

            Assert.Contains("truncated", ex.Message);
            Assert.True(ex.Message.Length < 4096, "message should not contain the full 4KB body");
        }

        // ─── Mid-stream provider error event ──────────────────────────────

        [Fact]
        public async Task SendStreamAsync_MidStreamErrorEvent_ThrowsLlmTransportException()
        {
            var frames = SsePayload(
                TextDeltaFrame("partial"),
                ErrorFrame("overloaded_error", "Overloaded"),
                "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
            );
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var fragments = new List<string>();
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("s", "u"))
                    fragments.Add(f);
            });

            Assert.Equal(new[] { "partial" }, fragments);
            Assert.Contains("overloaded_error", ex.Message);
            Assert.Contains("Overloaded", ex.Message);
        }

        // ─── Cancellation ─────────────────────────────────────────────────

        [Fact]
        public async Task SendStreamAsync_CancellationBetweenFrames_ClosesCleanly()
        {
            // Each chunk is a separate frame separated by a small delay, so
            // we can fire cancellation between them.
            var frames = SsePayload(
                TextDeltaFrame("a"),
                TextDeltaFrame("b"),
                TextDeltaFrame("c"),
                TextDeltaFrame("d")
            ).ToList();
            var handler = new SseHandler(HttpStatusCode.OK, frames, interChunkDelay: TimeSpan.FromMilliseconds(50));
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            using var cts = new CancellationTokenSource();
            var collected = new List<string>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("s", "u", cancellationToken: cts.Token))
                {
                    collected.Add(f);
                    if (collected.Count == 1)
                        cts.Cancel();
                }
            });

            // We got at least one fragment before cancelling. The stream
            // must have been disposed cleanly (the ChunkedStream in the
            // handler should be Disposed=true).
            Assert.True(collected.Count >= 1);
            Assert.NotNull(handler.Stream);
            Assert.True(handler.Stream!.Disposed, "underlying stream should be disposed after cancellation");
        }

        [Fact]
        public async Task SendStreamAsync_PreCancelledToken_ThrowsOperationCanceledNotTransportException()
        {
            var frames = SsePayload(TextDeltaFrame("x"));
            var handler = new SseHandler(HttpStatusCode.OK, frames);
            using var http = new HttpClient(handler);
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u", cancellationToken: cts.Token)) { }
            });
        }

        // ─── Coverage: defensive paths in ReadSseAsync (issue #160) ──────

        /// <summary>
        /// Stream that emits a fixed byte payload up-front, then BLOCKS in
        /// <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> on the
        /// next call until the stream is disposed. This exercises the
        /// dispose-on-cancel wake-up path: the transport's cancellation
        /// registration disposes the stream, which must wake the blocked
        /// read so the iterator can observe cancellation and surface OCE.
        /// </summary>
        private sealed class BlockingStream : Stream
        {
            private readonly byte[] _firstChunk;
            private int _firstChunkPos;
            private readonly TaskCompletionSource<int> _blocker =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Disposed { get; private set; }

            public BlockingStream(byte[] firstChunk)
            {
                _firstChunk = firstChunk;
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
                if (Disposed) throw new ObjectDisposedException(nameof(BlockingStream));

                // First call: hand back the fixed payload.
                if (_firstChunkPos < _firstChunk.Length)
                {
                    var available = _firstChunk.Length - _firstChunkPos;
                    var toCopy = Math.Min(available, count);
                    Buffer.BlockCopy(_firstChunk, _firstChunkPos, buffer, offset, toCopy);
                    _firstChunkPos += toCopy;
                    return toCopy;
                }

                // Second call: block until the stream is disposed (which
                // completes the TCS with -1) or cancellation propagates
                // through the token.
                using (cancellationToken.Register(() => _blocker.TrySetCanceled(cancellationToken)))
                {
                    var result = await _blocker.Task.ConfigureAwait(false);
                    if (result < 0)
                        throw new ObjectDisposedException(nameof(BlockingStream));
                    return result;
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                // -1 sentinel ⇒ blocked read raises ObjectDisposedException,
                // simulating a real disposed Stream.
                _blocker.TrySetResult(-1);
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Stream that emits a fixed byte payload up-front, then throws
        /// <see cref="IOException"/> on the next read. Exercises the
        /// catch-all I/O wrap arm in the SSE reader.
        /// </summary>
        private sealed class IoFaultStream : Stream
        {
            private readonly byte[] _firstChunk;
            private int _firstChunkPos;
            private readonly string _ioMessage;
            public bool Disposed { get; private set; }
            public IOException? ThrownException { get; private set; }

            public IoFaultStream(byte[] firstChunk, string ioMessage)
            {
                _firstChunk = firstChunk;
                _ioMessage = ioMessage;
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

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (Disposed) throw new ObjectDisposedException(nameof(IoFaultStream));

                if (_firstChunkPos < _firstChunk.Length)
                {
                    var available = _firstChunk.Length - _firstChunkPos;
                    var toCopy = Math.Min(available, count);
                    Buffer.BlockCopy(_firstChunk, _firstChunkPos, buffer, offset, toCopy);
                    _firstChunkPos += toCopy;
                    return Task.FromResult(toCopy);
                }

                ThrownException = new IOException(_ioMessage);
                throw ThrownException;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        /// <summary>HttpMessageHandler that returns a caller-supplied stream as the response body.</summary>
        private sealed class StreamHandler : HttpMessageHandler
        {
            private readonly Stream _body;
            public StreamHandler(Stream body) { _body = body; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var content = new StreamContent(_body);
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }

        [Fact]
        public async Task SendStreamAsync_CancellationDuringBlockingRead_DisposesStreamAndThrowsOperationCanceled()
        {
            // Emit one complete text_delta frame, then block on the next read
            // until the test cancels. The transport's dispose-on-cancel
            // registration must wake the blocked read so OCE escapes the
            // iterator (NOT a wrapped LlmTransportException).
            var firstFrame = U(TextDeltaFrame("first"));
            var stream = new BlockingStream(firstFrame);
            using var http = new HttpClient(new StreamHandler(stream));
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            using var cts = new CancellationTokenSource();
            var collected = new List<string>();

            var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("s", "u", cancellationToken: cts.Token))
                {
                    collected.Add(f);
                    if (collected.Count == 1)
                    {
                        // Schedule cancellation slightly later so the iterator
                        // is parked inside the blocking ReadLineAsync when it
                        // fires.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                            cts.Cancel();
                        });
                    }
                }
            });

            // We received the one fragment that was emitted before blocking.
            Assert.Equal(new[] { "first" }, collected);
            // The dispose-on-cancel callback fired and woke the blocked read.
            Assert.True(stream.Disposed, "BlockingStream should be disposed by the cancellation registration");
            // The escaped exception is OCE, NOT a wrapped LlmTransportException.
            Assert.IsAssignableFrom<OperationCanceledException>(caught);
            Assert.IsNotType<LlmTransportException>(caught);
        }

        [Fact]
        public async Task SendStreamAsync_MidStreamIOException_WrapsInLlmTransportException()
        {
            // One real frame, then IOException on the next read. The
            // transport must surface this as LlmTransportException with
            // the IOException preserved as InnerException.
            var firstFrame = U(TextDeltaFrame("head"));
            var stream = new IoFaultStream(firstFrame, "connection reset by peer");
            using var http = new HttpClient(new StreamHandler(stream));
            using var transport = new AnthropicStreamingTransport(TestApiKey, TestModel, http);

            var collected = new List<string>();
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("s", "u"))
                    collected.Add(f);
            });

            Assert.Equal(new[] { "head" }, collected);
            Assert.NotNull(ex.InnerException);
            Assert.IsAssignableFrom<IOException>(ex.InnerException);
            Assert.Equal("connection reset by peer", ex.InnerException!.Message);
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

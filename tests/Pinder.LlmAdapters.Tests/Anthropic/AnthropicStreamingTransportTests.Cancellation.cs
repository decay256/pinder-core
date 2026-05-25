using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public partial class AnthropicStreamingTransportTests
    {
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
    }
}
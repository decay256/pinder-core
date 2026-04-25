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
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests.OpenAi
{
    public class OpenAiStreamingTransportTests
    {
        // ------------------------------------------------------------------
        // Happy paths
        // ------------------------------------------------------------------

        [Fact]
        public async Task SendStreamAsync_YieldsContentFragmentsInOrder()
        {
            // role-only first frame, then three content deltas, then [DONE]
            var sse = BuildSse(new[]
            {
                ChunkRole(),
                ChunkContent("Hello"),
                ChunkContent(", "),
                ChunkContent("world!"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "Hello", ", ", "world!" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_RoleOnlyFirstFrame_IsIgnored()
        {
            var sse = BuildSse(new[]
            {
                ChunkRole(),       // role-only delta
                ChunkContent("ok"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "ok" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_DoneSentinel_TerminatesStreamCleanly()
        {
            // Anything after [DONE] must be ignored.
            var sse =
                "data: " + ChunkContent("a") + "\n\n" +
                "data: [DONE]\n\n" +
                "data: " + ChunkContent("b") + "\n\n";

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "a" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_FinishReasonChunkWithEmptyDelta_DoesNotYield()
        {
            // Final non-content chunk (finish_reason set, delta empty).
            var sse = BuildSse(new[]
            {
                ChunkContent("hi"),
                "{\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}",
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "hi" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_ToolCallDeltas_AreIgnored()
        {
            // Some providers emit tool_calls deltas with no content; we ignore them.
            var sse = BuildSse(new[]
            {
                "{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\"}]}}]}",
                ChunkContent("after"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "after" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_EmptyContentDelta_NotYielded()
        {
            var sse = BuildSse(new[]
            {
                ChunkContent(""),     // empty content -> ignore
                ChunkContent("real"),
                "[DONE]",
            });

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "real" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_TolleratesSseCommentsAndOtherFields()
        {
            // ":" comments (Groq sometimes sends keepalive comments) and unknown
            // SSE fields (event:, id:) must be ignored.
            var sse =
                ": ping\n" +
                "event: message\n" +
                "id: 1\n" +
                "data: " + ChunkContent("x") + "\n\n" +
                ": keepalive\n\n" +
                "data: [DONE]\n\n";

            using var transport = NewTransport(sse);
            var fragments = await CollectAsync(transport.SendStreamAsync("sys", "user"));

            Assert.Equal(new[] { "x" }, fragments);
        }

        [Fact]
        public async Task SendStreamAsync_PostsRequestBodyWithStreamTrue()
        {
            var sse = BuildSse(new[] { ChunkContent("ok"), "[DONE]" });
            string? capturedBody = null;
            string? capturedAuth = null;
            string? capturedUrl = null;

            var handler = new CannedSseHandler(sse, capture: req =>
            {
                capturedUrl = req.RequestUri!.ToString();
                if (req.Headers.TryGetValues("Authorization", out var auth))
                    capturedAuth = string.Join(",", auth);
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            });
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);

            _ = await CollectAsync(transport.SendStreamAsync("SYSPROMPT", "USERMSG", temperature: 0.42, maxTokens: 77));

            Assert.Equal("https://example.test/v1/chat/completions", capturedUrl);
            Assert.Equal("Bearer sk-test", capturedAuth);
            Assert.NotNull(capturedBody);
            Assert.Contains("\"stream\":true", capturedBody);
            Assert.Contains("\"model\":\"test-model\"", capturedBody);
            Assert.Contains("\"max_tokens\":77", capturedBody);
            Assert.Contains("\"temperature\":0.42", capturedBody);
            Assert.Contains("SYSPROMPT", capturedBody);
            Assert.Contains("USERMSG", capturedBody);
        }

        // ------------------------------------------------------------------
        // Error mapping
        // ------------------------------------------------------------------

        [Fact]
        public async Task SendStreamAsync_HttpNon2xx_ThrowsLlmTransportException()
        {
            var handler = new CannedSseHandler(
                bodyText: "{\"error\":{\"message\":\"unauthorized\"}}",
                statusCode: HttpStatusCode.Unauthorized);
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("sys", "user")) { }
            });
            Assert.Contains("401", ex.Message);
            Assert.Contains("unauthorized", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_MidStreamErrorFrame_ThrowsLlmTransportException()
        {
            var sse = BuildSse(new[]
            {
                ChunkContent("partial"),
                "{\"error\":{\"message\":\"upstream blew up\",\"type\":\"server_error\"}}",
                "[DONE]",
            });
            using var transport = NewTransport(sse);

            var collected = new List<string>();
            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("sys", "user"))
                    collected.Add(f);
            });
            Assert.Equal(new[] { "partial" }, collected);
            Assert.Contains("upstream blew up", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_MalformedJsonFrame_ThrowsLlmTransportException()
        {
            var sse =
                "data: " + ChunkContent("ok") + "\n\n" +
                "data: {not valid json\n\n" +
                "data: [DONE]\n\n";
            using var transport = NewTransport(sse);

            await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("sys", "user")) { }
            });
        }

        // ------------------------------------------------------------------
        // Cancellation
        // ------------------------------------------------------------------

        [Fact]
        public async Task SendStreamAsync_CancellationDuringStream_StopsAndThrowsOperationCanceled()
        {
            // Long stream so cancellation hits before all frames are drained.
            var chunks = new List<string>();
            for (int i = 0; i < 50; i++) chunks.Add(ChunkContent("tok" + i));
            chunks.Add("[DONE]");
            var sse = BuildSse(chunks);

            // Slow handler: yields the body byte-by-byte with async delays so
            // the consumer can cancel mid-stream.
            var handler = new SlowSseHandler(sse);
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);

            using var cts = new CancellationTokenSource();
            var collected = new List<string>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("s", "u", cancellationToken: cts.Token))
                {
                    collected.Add(f);
                    if (collected.Count == 2) cts.Cancel();
                }
            });

            // We saw at least some output before cancellation took effect, but never the whole stream.
            Assert.True(collected.Count >= 1);
            Assert.True(collected.Count < chunks.Count);
        }

        [Fact]
        public async Task SendStreamAsync_CancelledTokenBeforeStart_Throws()
        {
            var sse = BuildSse(new[] { ChunkContent("ok"), "[DONE]" });
            using var transport = NewTransport(sse);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("s", "u", cancellationToken: cts.Token)) { }
            });
        }

        // ------------------------------------------------------------------
        // Coverage: defensive paths in ParseSseAsync (issue #160)
        // ------------------------------------------------------------------

        [Fact]
        public async Task SendStreamAsync_CancellationDuringBlockingRead_DisposesStreamAndThrowsOperationCanceled()
        {
            // Emit one valid frame, then BLOCK on the next read until the test
            // cancels. The transport's dispose-on-cancel registration must
            // wake the blocked read so the iterator surfaces OCE
            // (NOT a wrapped LlmTransportException).
            var firstFrame = Encoding.UTF8.GetBytes("data: " + ChunkContent("first") + "\n\n");
            var stream = new BlockingStream(firstFrame);
            var handler = new StreamHandler(stream);
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);

            using var cts = new CancellationTokenSource();
            var collected = new List<string>();

            var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var f in transport.SendStreamAsync("s", "u", cancellationToken: cts.Token))
                {
                    collected.Add(f);
                    if (collected.Count == 1)
                    {
                        // Schedule cancellation so the iterator is parked
                        // inside ReadLineAsync when the token fires.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                            cts.Cancel();
                        });
                    }
                }
            });

            Assert.Equal(new[] { "first" }, collected);
            Assert.True(stream.Disposed,
                "BlockingStream should be disposed by the cancellation registration");
            Assert.IsAssignableFrom<OperationCanceledException>(caught);
            Assert.IsNotType<LlmTransportException>(caught);
        }

        [Fact]
        public async Task SendStreamAsync_MidStreamIOException_WrapsInLlmTransportException()
        {
            // One real frame, then IOException on the next read. The
            // transport must surface this as LlmTransportException with
            // the IOException preserved as InnerException.
            var firstFrame = Encoding.UTF8.GetBytes("data: " + ChunkContent("head") + "\n\n");
            var stream = new IoFaultStream(firstFrame, "connection reset by peer");
            var handler = new StreamHandler(stream);
            using var http = new HttpClient(handler);
            using var transport = new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);

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

        // ------------------------------------------------------------------
        // Constructor guard rails
        // ------------------------------------------------------------------

        [Fact]
        public void Constructor_NullOrEmptyApiKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new OpenAiStreamingTransport(null!, "u", "m"));
            Assert.Throws<ArgumentException>(() => new OpenAiStreamingTransport("", "u", "m"));
            Assert.Throws<ArgumentException>(() => new OpenAiStreamingTransport("   ", "u", "m"));
        }

        [Fact]
        public void Constructor_NullModel_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OpenAiStreamingTransport("k", "u", null!));
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OpenAiStreamingTransport("k", "u", "m", null!));
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private static OpenAiStreamingTransport NewTransport(string sseBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            var handler = new CannedSseHandler(sseBody, statusCode: status);
            var http = new HttpClient(handler);
            // Caller owns http via transport.Dispose -> we DO NOT pass ownership flag,
            // but the test transports are short-lived and disposed in test scope.
            return new OpenAiStreamingTransport("sk-test", "https://example.test", "test-model", http);
        }

        private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
        {
            var result = new List<string>();
            await foreach (var item in source) result.Add(item);
            return result;
        }

        /// <summary>Build a multi-frame SSE body from a sequence of "data:" payloads.</summary>
        private static string BuildSse(IEnumerable<string> dataPayloads)
        {
            var sb = new StringBuilder();
            foreach (var p in dataPayloads)
            {
                sb.Append("data: ").Append(p).Append("\n\n");
            }
            return sb.ToString();
        }

        private static string ChunkContent(string content)
        {
            // Escape only the bare minimum we need for these test payloads.
            var escaped = content
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            return "{\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + escaped + "\"}}]}";
        }

        private static string ChunkRole()
        {
            return "{\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"}}]}";
        }

        /// <summary>Handler that returns a fixed body (string) and optional status code.</summary>
        private sealed class CannedSseHandler : HttpMessageHandler
        {
            private readonly byte[] _body;
            private readonly HttpStatusCode _status;
            private readonly Action<HttpRequestMessage>? _capture;

            public CannedSseHandler(
                string bodyText,
                HttpStatusCode statusCode = HttpStatusCode.OK,
                Action<HttpRequestMessage>? capture = null)
            {
                _body = Encoding.UTF8.GetBytes(bodyText);
                _status = statusCode;
                _capture = capture;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _capture?.Invoke(request);
                var resp = new HttpResponseMessage(_status)
                {
                    Content = new ByteArrayContent(_body),
                };
                if (_status == HttpStatusCode.OK)
                    resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                else
                    resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                return Task.FromResult(resp);
            }
        }

        /// <summary>
        /// Handler that returns the body via a custom stream which yields bytes
        /// asynchronously with a small delay, so a consumer can cancel mid-stream.
        /// </summary>
        private sealed class SlowSseHandler : HttpMessageHandler
        {
            private readonly byte[] _body;

            public SlowSseHandler(string bodyText)
            {
                _body = Encoding.UTF8.GetBytes(bodyText);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new SlowStream(_body)),
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(resp);
            }
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

        /// <summary>
        /// Stream that emits a fixed byte payload up-front, then BLOCKS in
        /// <see cref="ReadAsync(byte[], int, int, CancellationToken)"/> on the
        /// next call until the stream is disposed. Used to verify the
        /// transport's dispose-on-cancel wake-up path.
        /// </summary>
        private sealed class BlockingStream : Stream
        {
            private readonly byte[] _firstChunk;
            private int _firstChunkPos;
            private readonly TaskCompletionSource<int> _blocker =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Disposed { get; private set; }

            public BlockingStream(byte[] firstChunk) { _firstChunk = firstChunk; }

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

                if (_firstChunkPos < _firstChunk.Length)
                {
                    var available = _firstChunk.Length - _firstChunkPos;
                    var toCopy = Math.Min(available, count);
                    Buffer.BlockCopy(_firstChunk, _firstChunkPos, buffer, offset, toCopy);
                    _firstChunkPos += toCopy;
                    return toCopy;
                }

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
                _blocker.TrySetResult(-1);
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Stream that emits a fixed payload up-front, then throws
        /// <see cref="IOException"/> on the next read. Used to verify the
        /// I/O wrap behaviour mid-stream.
        /// </summary>
        private sealed class IoFaultStream : Stream
        {
            private readonly byte[] _firstChunk;
            private int _firstChunkPos;
            private readonly string _ioMessage;
            public bool Disposed { get; private set; }

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

                throw new IOException(_ioMessage);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private sealed class SlowStream : Stream
        {
            private readonly byte[] _body;
            private int _pos;

            public SlowStream(byte[] body) { _body = body; }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _body.Length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _body.Length) return 0;
                buffer[offset] = _body[_pos++];
                return 1;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                if (_pos >= _body.Length) return 0;
                buffer[offset] = _body[_pos++];
                return 1;
            }
        }
    }
}

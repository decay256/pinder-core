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
    public partial class OpenAiStreamingTransportTests
    {
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

        private static string ChunkReasoning(string reasoning)
        {
            var escaped = JsonEscape(reasoning);
            return "{\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"\",\"reasoning\":\"" + escaped + "\"}}]}";
        }

        private static string ChunkContentAndReasoning(string content, string reasoning)
        {
            var ec = JsonEscape(content);
            var er = JsonEscape(reasoning);
            return "{\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + ec + "\",\"reasoning\":\"" + er + "\"}}]}";
        }

        private static string ChunkReasoningDetailsSummaries(string[] summaries)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"\",\"reasoning_details\":[");
            for (int i = 0; i < summaries.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"type\":\"reasoning.summary\",\"summary\":\"")
                  .Append(JsonEscape(summaries[i]))
                  .Append("\"}");
            }
            sb.Append("]}}]}");
            return sb.ToString();
        }

        private static string JsonEscape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
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

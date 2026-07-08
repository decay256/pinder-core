using System;
using System.Collections.Generic;
using System.IO;
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
            Assert.DoesNotContain("unauthorized", ex.Message);
            Assert.DoesNotContain("unauthorized", ex.ToString());
            Assert.Contains("provider=openai-compatible-streaming", ex.Message);
            Assert.Contains("model=test-model", ex.Message);
            Assert.Contains("body_length=", ex.Message);
            Assert.Contains("body_sha256=", ex.Message);
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
            Assert.DoesNotContain("upstream blew up", ex.Message);
            Assert.DoesNotContain("upstream blew up", ex.ToString());
            Assert.Contains("error_length=", ex.Message);
            Assert.Contains("error_sha256=", ex.Message);
        }

        [Fact]
        public async Task SendStreamAsync_MalformedJsonFrame_ThrowsLlmTransportException()
        {
            var sse =
                "data: " + ChunkContent("ok") + "\n\n" +
                "data: {not valid json\n\n" +
                "data: [DONE]\n\n";
            using var transport = NewTransport(sse);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(async () =>
            {
                await foreach (var _ in transport.SendStreamAsync("sys", "user")) { }
            });
            Assert.DoesNotContain("{not valid json", ex.Message);
            Assert.DoesNotContain("{not valid json", ex.ToString());
            Assert.Contains("chunk_length=", ex.Message);
            Assert.Contains("chunk_sha256=", ex.Message);
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
    }
}

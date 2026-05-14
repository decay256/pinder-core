using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.RemoteAssets;
using Xunit;

namespace Pinder.RemoteAssets.Tests
{
    /// <summary>
    /// Regression tests for the <see cref="EigencoreCharacterStore"/>
    /// <c>HttpClient.MaxResponseContentBufferSize</c> guard (issue #860).
    ///
    /// The guard caps the response buffer proportionally to
    /// <see cref="Configuration.PayloadSizeCapBytes"/> so a compromised
    /// eigencore cannot blow up the caller process via a multi-GB response.
    ///
    /// <para>
    /// <b>Test-design note:</b> <c>MaxResponseContentBufferSize</c> is
    /// enforced by <c>HttpClientHandler</c> (the real network stack), not
    /// by <c>HttpContent</c> classes. A <c>FakeHttpMessageHandler</c>
    /// bypasses real HTTP and therefore cannot exercise the buffer cap
    /// directly. These tests instead verify (a) the property is
    /// configured, (b) the store handles responses at the cap, and (c)
    /// <c>HttpRequestException</c> from a buffer-overflow propagates
    /// uncaught — using a custom content to simulate the real-handler
    /// fault.
    /// </para>
    /// </summary>
    public class EigencoreCharacterStoreBufferCapTests
    {
        private const string AssetId = "59aa20f2-46d6-4adc-89c1-6ea17f815020";
        private const string MetadataHeader = "X-Asset-Metadata";
        private const int PayloadSizeCap = 1024;

        /// <summary>
        /// Buffer cap computed from the formula in the ctor:
        /// <c>Math.Max(PayloadSizeCapBytes, 1 MiB) * 4</c>.
        /// With PayloadSizeCapBytes = 1024, the floor is 4 MiB.
        /// </summary>
        private static readonly long ExpectedBufferCap = Math.Max(PayloadSizeCap, 1024 * 1024) * 4;

        private static readonly CharacterDefinition StubResult = new(
            schemaVersion: 1,
            characterId: Guid.Parse(AssetId),
            name: "Stub",
            genderIdentity: "they/them",
            bio: "stub",
            level: 1,
            items: Array.Empty<string>(),
            anatomy: new Dictionary<string, string>(),
            allocation: new AllocationBlock(
                new Dictionary<StatType, int>(),
                0,
                new Dictionary<ShadowStatType, int>()));

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        private static byte[] EncodeMetadata(string assetId = AssetId)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("asset_kind", "character/v1");
                w.WriteString("asset_id", assetId);
                w.WriteString("owner_id", "user:test");
                w.WriteBoolean("is_public", true);
                w.WriteStartArray("tags");
                w.WriteEndArray();
                w.WriteString("created_at", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).ToString("o"));
                w.WriteString("updated_at", new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero).ToString("o"));
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        private static string EncodeMetadataHeaderValue(byte[] metaJsonBytes)
        {
            return Convert.ToBase64String(metaJsonBytes);
        }

        private static (EigencoreCharacterStore store, FakeHttpMessageHandler handler) Make(
            int payloadSizeCapBytes = PayloadSizeCap)
        {
            var handler = new FakeHttpMessageHandler();
            var config = new Configuration(
                baseUrl: new Uri("https://eigencore.test/api/v1"),
                httpMessageHandler: handler,
                authTokenProvider: ct => Task.FromResult("test-token"),
                payloadParser: _ => StubResult,
                defaultRetryAfter: TimeSpan.FromMilliseconds(1),
                payloadSizeCapBytes: payloadSizeCapBytes);
            return (new EigencoreCharacterStore(config), handler);
        }

        /// <summary>
        /// Uses reflection to read the private <c>_http</c> field and
        /// return its <c>MaxResponseContentBufferSize</c>.
        /// </summary>
        private static long GetActualBufferCap(EigencoreCharacterStore store)
        {
            var httpField = typeof(EigencoreCharacterStore)
                .GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(httpField);
            var http = (HttpClient)httpField!.GetValue(store)!;
            return http.MaxResponseContentBufferSize;
        }

        // -----------------------------------------------------------------
        // tests
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_Sets_MaxResponseContentBufferSize()
        {
            var (store, _) = Make();
            long actual = GetActualBufferCap(store);

            Assert.Equal(ExpectedBufferCap, actual);
        }

        [Fact]
        public async Task ResponseAtCap_Succeeds()
        {
            // Body exactly at the buffer cap (4 MiB with the current
            // formula). The stub parser ignores the bytes, so content can
            // be any 4 MiB blob.
            var (store, handler) = Make();
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[ExpectedBufferCap]),
            };
            resp.Headers.TryAddWithoutValidation(
                MetadataHeader, EncodeMetadataHeaderValue(EncodeMetadata()));
            handler.Enqueue(resp);

            CharacterDefinition? loaded = await store.LoadAsync(AssetId);

            Assert.NotNull(loaded);
            Assert.Equal(StubResult.CharacterId, loaded!.CharacterId);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task ResponseExceedingCap_ThrowsHttpRequestException()
        {
            // Simulate a buffer-cap-too-large fault by returning a custom
            // HttpContent whose SerializeToStreamAsync throws the same
            // HttpRequestException the real HttpClientHandler would throw.
            // This verifies the store does NOT swallow the exception —
            // the propagation rule from the AC.
            const long simulatedCap = 1024;
            var (store, handler) = Make();
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new BufferCapExceededContent(simulatedCap),
            };
            resp.Headers.TryAddWithoutValidation(
                MetadataHeader, EncodeMetadataHeaderValue(EncodeMetadata()));
            handler.Enqueue(resp);

            HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
                () => store.LoadAsync(AssetId));

            Assert.Contains("maximum buffer size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ErrorResponse_BufferCapExceeded_ThrowsHttpRequestException()
        {
            // Regression test for the bare-catch fix (rung 1). When a
            // non-2xx response body exceeds the buffer cap, SafeReadBodyAsync
            // must let HttpRequestException propagate — not swallow it into
            // an empty-body ServerException. Uses QueryAsync which reads
            // the error body via SafeReadBodyAsync on the 5xx path.
            const long simulatedCap = 1024;
            var (store, handler) = Make();
            var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new BufferCapExceededContent(simulatedCap),
            };
            handler.Enqueue(resp);

            HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
                () => store.QueryAsync(new CharacterAssetQuery()));

            Assert.Contains("maximum buffer size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------
        // custom content — simulates a real-handler buffer-cap fault
        // -----------------------------------------------------------------

        /// <summary>
        /// Custom <see cref="HttpContent"/> that throws
        /// <see cref="HttpRequestException"/> from
        /// <see cref="SerializeToStreamAsync"/>, matching the exception
        /// the real <c>HttpClientHandler</c> throws when
        /// <c>MaxResponseContentBufferSize</c> is exceeded.
        /// </summary>
        private sealed class BufferCapExceededContent : HttpContent
        {
            private readonly long _cap;

            public BufferCapExceededContent(long cap)
            {
                _cap = cap;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return Task.FromException(new HttpRequestException(
                    $"Cannot write more bytes to the buffer than the configured maximum buffer size: {_cap}."));
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _cap + 1;
                return true;
            }
        }
    }
}

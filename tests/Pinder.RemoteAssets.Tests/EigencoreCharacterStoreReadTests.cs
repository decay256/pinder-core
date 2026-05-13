using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.RemoteAssets;
using Pinder.RemoteAssets.Exceptions;
using Xunit;

namespace Pinder.RemoteAssets.Tests
{
    /// <summary>
    /// Read-path tests for <see cref="EigencoreCharacterStore"/> (issue
    /// #853). Each test pins a single bullet from the ticket's "Tests
    /// required" list — the test names track the bullet wording.
    /// </summary>
    public class EigencoreCharacterStoreReadTests
    {
        private const string AssetId = "59aa20f2-46d6-4adc-89c1-6ea17f815020";
        private const string MetadataHeader = "X-Asset-Metadata";

        // -- helpers -----------------------------------------------------

        private static byte[] EncodeMetadata(
            string assetId = AssetId,
            string assetKind = "character/v1",
            string ownerId = "user:test",
            bool isPublic = true,
            string[]? tags = null,
            DateTimeOffset? createdAt = null,
            DateTimeOffset? updatedAt = null)
        {
            using var ms = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("asset_kind", assetKind);
                w.WriteString("asset_id", assetId);
                w.WriteString("owner_id", ownerId);
                w.WriteBoolean("is_public", isPublic);
                w.WriteStartArray("tags");
                foreach (var t in tags ?? Array.Empty<string>()) w.WriteStringValue(t);
                w.WriteEndArray();
                w.WriteString("created_at", (createdAt ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)).ToString("o"));
                w.WriteString("updated_at", (updatedAt ?? new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero)).ToString("o"));
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        private static string EncodeMetadataHeaderValue(byte[] metaJsonBytes)
        {
            // RFC 4648 standard padded base64 — the wire spec contract.
            return Convert.ToBase64String(metaJsonBytes);
        }

        /// <summary>
        /// Default payload parser used by tests that don't care about the
        /// payload (Exists, GetMetadata, error tests). Builds a minimal
        /// valid <see cref="CharacterDefinition"/>; the bytes don't have
        /// to be real JSON because this parser ignores them.
        /// </summary>
        private static CharacterDefinition StubParse(byte[] _)
        {
            return new CharacterDefinition(
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
        }

        private static (EigencoreCharacterStore store, FakeHttpMessageHandler handler) Make(
            CharacterPayloadParser? parser = null,
            Func<CancellationToken, Task<string>>? authProvider = null,
            TimeSpan? defaultRetryAfter = null)
        {
            var handler = new FakeHttpMessageHandler();
            var config = new Configuration(
                baseUrl: new Uri("https://eigencore.test/api/v1"),
                httpMessageHandler: handler,
                authTokenProvider: authProvider ?? (ct => Task.FromResult("test-token")),
                payloadParser: parser ?? StubParse,
                defaultRetryAfter: defaultRetryAfter ?? TimeSpan.FromMilliseconds(1));
            return (new EigencoreCharacterStore(config), handler);
        }

        private static HttpResponseMessage Ok(byte[] payload, byte[] metaJsonBytes)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
            resp.Headers.TryAddWithoutValidation(MetadataHeader, EncodeMetadataHeaderValue(metaJsonBytes));
            return resp;
        }

        // -- tests -------------------------------------------------------

        [Fact]
        public async Task LoadAsync_HappyPath_Returns_Parsed_Character_With_CharacterId_From_AssetId()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"schema_version\":1,\"character_id\":\"" + AssetId + "\"}");
            byte[] metaJson = EncodeMetadata(assetId: AssetId);

            byte[]? sawPayload = null;
            CharacterPayloadParser parser = bytes =>
            {
                sawPayload = bytes;
                return new CharacterDefinition(
                    schemaVersion: 1,
                    characterId: Guid.Parse(AssetId),
                    name: "Happy",
                    genderIdentity: "they/them",
                    bio: "happy",
                    level: 3,
                    items: Array.Empty<string>(),
                    anatomy: new Dictionary<string, string>(),
                    allocation: new AllocationBlock(
                        new Dictionary<StatType, int>(),
                        0,
                        new Dictionary<ShadowStatType, int>()));
            };

            var (store, handler) = Make(parser: parser);
            handler.Enqueue(Ok(payload, metaJson));

            CharacterDefinition? loaded = await store.LoadAsync(AssetId);

            // Returned POCO matches what the parser produced.
            Assert.NotNull(loaded);
            Assert.Equal(Guid.Parse(AssetId), loaded!.CharacterId);
            Assert.Equal("Happy", loaded.Name);

            // Parser saw the raw response body bytes.
            Assert.NotNull(sawPayload);
            Assert.Equal(payload, sawPayload!);

            // Request shape.
            Assert.Single(handler.Requests);
            var req = handler.Requests[0];
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal($"https://eigencore.test/api/v1/assets/{AssetId}", req.RequestUri!.AbsoluteUri);
            Assert.NotNull(req.Headers.Authorization);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", req.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task LoadAsync_404_Returns_Null()
        {
            var (store, handler) = Make();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"detail\":\"not_found\"}"),
            });

            CharacterDefinition? loaded = await store.LoadAsync(AssetId);

            Assert.Null(loaded);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task LoadAsync_401_Throws_RemoteAssetAuthException()
        {
            var (store, handler) = Make();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"detail\":\"token expired\"}"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetAuthException>(() => store.LoadAsync(AssetId));
            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("token expired", ex.ResponseBody);
        }

        [Fact]
        public async Task LoadAsync_500_Throws_RemoteAssetServerException_With_Status_And_Body()
        {
            var (store, handler) = Make();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("kaboom"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetServerException>(() => store.LoadAsync(AssetId));
            Assert.Equal(500, ex.StatusCode);
            Assert.Equal("kaboom", ex.ResponseBody);
        }

        [Fact]
        public async Task LoadAsync_503_Also_Throws_RemoteAssetServerException()
        {
            var (store, handler) = Make();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("upstream down"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetServerException>(() => store.LoadAsync(AssetId));
            Assert.Equal(503, ex.StatusCode);
        }

        [Fact]
        public async Task LoadAsync_429_Then_200_Retries_And_Succeeds()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"schema_version\":1}");
            byte[] metaJson = EncodeMetadata();

            var (store, handler) = Make(defaultRetryAfter: TimeSpan.FromMilliseconds(50));

            // First call: 429 with Retry-After: 0 (let us measure delay precisely while keeping it small).
            handler.Enqueue(req =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate"),
                };
                r.Headers.TryAddWithoutValidation("Retry-After", "0");
                return r;
            });
            handler.Enqueue(_ => Ok(payload, metaJson));

            var sw = Stopwatch.StartNew();
            CharacterDefinition? loaded = await store.LoadAsync(AssetId);
            sw.Stop();

            Assert.NotNull(loaded);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        }

        [Fact]
        public async Task LoadAsync_429_Then_429_Throws_RemoteAssetRateLimitException()
        {
            var (store, handler) = Make(defaultRetryAfter: TimeSpan.FromMilliseconds(1));

            handler.Enqueue(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate"),
                };
                r.Headers.TryAddWithoutValidation("Retry-After", "0");
                return r;
            });
            handler.Enqueue(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate-2"),
                };
                r.Headers.TryAddWithoutValidation("Retry-After", "2");
                return r;
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetRateLimitException>(() => store.LoadAsync(AssetId));
            Assert.Equal(429, ex.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(2), ex.RetryAfter);
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task LoadAsync_Missing_XAssetMetadata_Header_Throws_Typed()
        {
            // Implementer's choice (documented in PR body):
            // RemoteAssetMalformedMetadataException — a typed subclass of
            // RemoteAssetException — so callers can distinguish "server
            // crashed" (5xx) from "wire framing wrong".
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                // no X-Asset-Metadata header
            });

            await Assert.ThrowsAsync<RemoteAssetMalformedMetadataException>(() => store.LoadAsync(AssetId));
        }

        [Fact]
        public async Task LoadAsync_XAssetMetadata_Header_Bad_Base64_Throws_Malformed()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                };
                // Bytes that are not a valid base64 string at all.
                r.Headers.TryAddWithoutValidation(MetadataHeader, "!!!not-base64$$$");
                return r;
            });

            await Assert.ThrowsAsync<RemoteAssetMalformedMetadataException>(() => store.LoadAsync(AssetId));
        }

        [Fact]
        public async Task LoadAsync_XAssetMetadata_Header_Decodes_But_Not_JSON_Throws_Malformed()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                };
                // Valid base64 of "not-json-at-all".
                r.Headers.TryAddWithoutValidation(MetadataHeader, Convert.ToBase64String(Encoding.UTF8.GetBytes("not-json-at-all")));
                return r;
            });

            await Assert.ThrowsAsync<RemoteAssetMalformedMetadataException>(() => store.LoadAsync(AssetId));
        }

        /// <summary>
        /// Regression test for the #1 implementer mistake: reaching for
        /// <c>WebEncoders.Base64UrlDecode</c> (or the Python equivalent
        /// <c>base64.urlsafe_b64decode</c>) instead of standard RFC 4648
        /// padded base64. The wire is unambiguously standard base64. A
        /// header whose value uses the base64url alphabet (<c>-</c>/<c>_</c>
        /// instead of <c>+</c>/<c>/</c>) MUST fail to decode loudly here.
        /// </summary>
        [Fact]
        public async Task LoadAsync_XAssetMetadata_Header_With_Base64Url_Alphabet_Fails_Loudly()
        {
            // Construct a header value that is unambiguously
            // base64url (uses '-' / '_'), so a server that wrongly
            // reaches for WebEncoders.Base64UrlDecode would happily
            // decode it. Standard RFC 4648 base64
            // (Convert.FromBase64String) MUST refuse it.
            //
            // Strategy: pick a byte sequence whose standard-base64
            // encoding contains '+' AND '/', then swap to produce
            // the base64url form. The bytes (0xFB 0xFF 0xBF) encode
            // as "+/+/" in standard base64. Swapping yields "-_-_"
            // which is a valid base64url string and a syntactically
            // invalid standard-base64 string.
            byte[] sampleBytes = new byte[] { 0xFB, 0xFF, 0xBF };
            string standardB64 = Convert.ToBase64String(sampleBytes);
            Assert.Equal("+/+/", standardB64);
            string base64Url = standardB64.Replace('+', '-').Replace('/', '_');
            Assert.Equal("-_-_", base64Url);

            var (store, handler) = Make();
            handler.Enqueue(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3 }),
                };
                r.Headers.TryAddWithoutValidation(MetadataHeader, base64Url);
                return r;
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetMalformedMetadataException>(() => store.LoadAsync(AssetId));
            // Message should mention base64url so the failure is
            // self-diagnosing in CI logs.
            Assert.Contains("base64url", ex.Message);
        }

        [Fact]
        public async Task GetMetadataAsync_HappyPath_Returns_Populated_POCO()
        {
            byte[] metaJson = EncodeMetadata(
                assetId: AssetId,
                ownerId: "user:42",
                tags: new[] { "starter", "official-pack" },
                createdAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
                updatedAt: new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));

            var (store, handler) = Make();
            handler.Enqueue(_ => Ok(new byte[] { 0, 0, 0 }, metaJson));

            CharacterAssetMetadata? meta = await store.GetMetadataAsync(AssetId);

            Assert.NotNull(meta);
            // The wire spelled "asset_id"; the POCO renames to CharacterId.
            Assert.Equal(AssetId, meta!.CharacterId);
            Assert.Equal("user:42", meta.OwnerId);
            Assert.True(meta.IsPublic);
            Assert.Equal(new[] { "starter", "official-pack" }, meta.Tags);
            Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), meta.CreatedAt);
            Assert.Equal(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero), meta.UpdatedAt);
            Assert.Equal("character/v1", meta.AssetKind);
        }

        [Fact]
        public async Task GetMetadataAsync_404_Returns_Null()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(""),
            });

            CharacterAssetMetadata? meta = await store.GetMetadataAsync(AssetId);
            Assert.Null(meta);
        }

        [Fact]
        public async Task ExistsAsync_200_Returns_True()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => Ok(new byte[] { 1 }, EncodeMetadata()));

            bool exists = await store.ExistsAsync(AssetId);
            Assert.True(exists);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task ExistsAsync_404_Returns_False()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(""),
            });

            bool exists = await store.ExistsAsync(AssetId);
            Assert.False(exists);
        }

        [Fact]
        public async Task AuthTokenProvider_Returning_Empty_Omits_Authorization_Header()
        {
            var (store, handler) = Make(authProvider: _ => Task.FromResult(""));
            handler.Enqueue(_ => Ok(new byte[] { 1 }, EncodeMetadata()));

            await store.ExistsAsync(AssetId);

            Assert.Null(handler.Requests[0].Headers.Authorization);
        }

        [Fact]
        public async Task ListIdsAsync_Throws_NotSupported_In_853()
        {
            var (store, _) = Make();
            await Assert.ThrowsAsync<NotSupportedException>(() => store.ListIdsAsync());
        }

        // QueryAsync_Throws_NotSupported_In_853 was removed in #854 —
        // the query path is now implemented (see
        // EigencoreCharacterStoreQueryTests).
        // SaveAsync_Throws_NotSupported_In_853 was removed in #855 —
        // the write path is now implemented (see
        // EigencoreCharacterStoreWriteTests). ListIdsAsync stays
        // NotSupported per the v1 wire contract (no list-all endpoint).
    }
}

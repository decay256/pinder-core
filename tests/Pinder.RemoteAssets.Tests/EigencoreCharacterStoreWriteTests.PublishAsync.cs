using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.RemoteAssets;
using Pinder.RemoteAssets.Exceptions;
using Xunit;

namespace Pinder.RemoteAssets.Tests
{
    public partial class EigencoreCharacterStoreWriteTests
    {
        [Fact]
        public async Task PublishAsync_HappyPath_201_Returns_Server_Stamped_Metadata()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => OkPublish());

            var metadata = MakeMetadata();
            CharacterAssetMetadata returned = await store.PublishAsync(StubDef(), metadata);

            // Returned POCO carries the server's stamped fields, with
            // the asset_id → CharacterId rename applied.
            Assert.Equal(AssetId, returned.CharacterId);
            Assert.Equal("user:42", returned.OwnerId);
            Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), returned.CreatedAt);
            Assert.Equal(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero), returned.UpdatedAt);

            // Request shape: POST to assets, with bearer auth.
            Assert.Single(handler.Requests);
            var req = handler.Requests[0];
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://eigencore.test/api/v1/assets", req.RequestUri!.AbsoluteUri);
            Assert.NotNull(req.Headers.Authorization);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", req.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task PublishAsync_Request_Is_Multipart_With_Exactly_Two_Parts_Named_metadata_And_payload()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => OkPublish());

            await store.PublishAsync(StubDef(), MakeMetadata());

            var parts = ReadMultipartParts(handler);

            // Exactly two parts, exact names.
            Assert.Equal(2, parts.Count);
            Assert.True(parts.ContainsKey("metadata"), "missing 'metadata' part");
            Assert.True(parts.ContainsKey("payload"), "missing 'payload' part");

            // metadata Content-Type = application/json.
            Assert.StartsWith("application/json", parts["metadata"].contentType, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task PublishAsync_Metadata_Json_Uses_asset_id_Never_character_id()
        {
            // The single highest-risk regression: the wire wants
            // "asset_id"; the POCO calls the field CharacterId. The
            // boundary rename MUST happen exactly once, here, in the
            // outbound serializer. If anyone ever lets a literal
            // "character_id" through the wire, the eigencore backend
            // accepts the upload but indexes the WRONG key — silent
            // corruption.
            var (store, handler) = Make();
            handler.Enqueue(_ => OkPublish());

            await store.PublishAsync(StubDef(), MakeMetadata());

            var parts = ReadMultipartParts(handler);
            string metaJson = Encoding.UTF8.GetString(parts["metadata"].body);

            Assert.Contains("\"asset_id\"", metaJson);
            Assert.DoesNotContain("\"character_id\"", metaJson);

            // Sanity: the asset_id value is the one we passed in.
            Assert.Contains(AssetId, metaJson);
        }

        [Fact]
        public async Task PublishAsync_Oversize_Metadata_Throws_Before_Any_Http_Call()
        {
            var (store, handler) = Make(metadataCap: 4 * 1024);

            // Build a tags array fat enough to blow the 4 KiB cap. Each
            // tag is ~64 chars; 80 tags ≈ 5 KiB of JSON.
            string[] hugeTags = Enumerable.Range(0, 80)
                .Select(i => "filler-tag-" + new string('x', 60) + "-" + i)
                .ToArray();
            var metadata = MakeMetadata(tags: hugeTags);

            var ex = await Assert.ThrowsAsync<RemoteAssetTooLargeException>(() =>
                store.PublishAsync(StubDef(), metadata));
            Assert.Equal("metadata", ex.Subject);

            // The wire was never touched.
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task PublishAsync_Oversize_Payload_Throws_Before_Any_Http_Call()
        {
            // 300 KiB payload, default 256 KiB cap.
            byte[] huge = new byte[300 * 1024];
            var (store, handler) = Make(payloadSerializer: _ => huge);

            var ex = await Assert.ThrowsAsync<RemoteAssetTooLargeException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata()));
            Assert.Equal("payload", ex.Subject);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task PublishAsync_Overwrite_Returns_200_Is_Success()
        {
            // Second publish of same asset_id: eigencore returns 200
            // (overwrote) instead of 201 (created). The wrapper must
            // treat both as success.
            var (store, handler) = Make();
            handler.Enqueue(_ => OkPublish(
                CannedPublishResponseBody(updatedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
                HttpStatusCode.OK));

            CharacterAssetMetadata returned = await store.PublishAsync(StubDef(), MakeMetadata());
            Assert.Equal(AssetId, returned.CharacterId);
            Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), returned.UpdatedAt);
        }

        [Fact]
        public async Task PublishAsync_Reserved_Prefix_auto_Returns_403_Throws_Forbidden_With_Prefix()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"code\":\"permission_denied\",\"detail\":\"reserved tag prefix 'auto-' is not allowed\",\"tag\":\"auto-foo\"}",
                    Encoding.UTF8, "application/json"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetForbiddenException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata(tags: new[] { "auto-foo" })));
            Assert.Equal(403, ex.StatusCode);
            Assert.Contains("auto-", ex.Message);
        }

        [Fact]
        public async Task PublishAsync_Reserved_Prefix_official_From_NonAllowListed_Returns_403_Throws_Forbidden()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"code\":\"permission_denied\",\"detail\":\"tag prefix 'official-' is not allowed for this client\",\"prefix\":\"official-\"}",
                    Encoding.UTF8, "application/json"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetForbiddenException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata(tags: new[] { "official-foo" })));
            Assert.Equal(403, ex.StatusCode);
            Assert.Contains("official-", ex.Message);
        }

        [Fact]
        public async Task PublishAsync_401_Throws_RemoteAssetAuthException()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"detail\":\"bad token\"}"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetAuthException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata()));
            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("bad token", ex.ResponseBody);
        }

        [Fact]
        public async Task PublishAsync_500_Throws_RemoteAssetServerException()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("kaboom"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetServerException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata()));
            Assert.Equal(500, ex.StatusCode);
            Assert.Equal("kaboom", ex.ResponseBody);
        }

        [Fact]
        public async Task PublishAsync_422_invalid_multipart_Throws_RemoteAssetValidationException()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    "{\"code\":\"invalid_multipart\",\"errors\":[\"unknown form field 'extra'\"]}",
                    Encoding.UTF8, "application/json"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetValidationException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata()));
            Assert.Equal(422, ex.StatusCode);
            Assert.Contains("unknown form field 'extra'", ex.Errors);
        }

        [Fact]
        public async Task PublishAsync_422_metadata_too_large_From_Server_Throws_TooLarge_Metadata()
        {
            // Pre-validation should normally catch this; this test
            // covers the rare case of a server with a tighter cap than
            // the client's MetadataSizeCapBytes (env override).
            var (store, handler) = Make(metadataCap: 1024 * 1024);  // 1 MiB locally so we don't pre-trip
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    "{\"code\":\"metadata_too_large\",\"detail\":\"metadata exceeds 4096 bytes\"}",
                    Encoding.UTF8, "application/json"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetTooLargeException>(() =>
                store.PublishAsync(StubDef(), MakeMetadata()));
            Assert.Equal("metadata", ex.Subject);
            Assert.Equal(422, ex.StatusCode);
        }
    }
}

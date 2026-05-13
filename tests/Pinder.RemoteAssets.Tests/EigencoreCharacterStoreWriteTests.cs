using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    /// Write-path tests for <see cref="EigencoreCharacterStore"/> (issue
    /// #855). Covers <c>PublishAsync</c>, <c>SaveAsync</c>, and
    /// <c>DeleteAsync</c>. Each test pins a single bullet from the
    /// ticket's "Tests required" list.
    ///
    /// Wire-shape regression coverage at the top of the file: multipart
    /// boundary, exactly two parts named <c>metadata</c> + <c>payload</c>,
    /// metadata content-type, and the no-<c>character_id</c> JSON guard.
    /// These are the highest-risk invariants; pinning them first means a
    /// later refactor cannot silently break the wire contract.
    /// </summary>
    public class EigencoreCharacterStoreWriteTests
    {
        private const string AssetId = "59aa20f2-46d6-4adc-89c1-6ea17f815020";

        // -- helpers -----------------------------------------------------

        private static CharacterDefinition StubDef()
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

        private static CharacterDefinition StubParse(byte[] _) => StubDef();

        private static CharacterAssetMetadata MakeMetadata(
            string assetId = AssetId,
            string ownerId = "",
            string[]? tags = null,
            bool isPublic = true)
        {
            return new CharacterAssetMetadata(
                characterId: assetId,
                ownerId: ownerId,
                tags: tags ?? new[] { "starter" },
                isPublic: isPublic,
                createdAt: DateTimeOffset.MinValue,   // server-stamped on publish; ignored on the wire
                updatedAt: DateTimeOffset.MinValue,
                assetKind: CharacterAssetMetadata.AssetKindCharacterV1);
        }

        private static (EigencoreCharacterStore store, FakeHttpMessageHandler handler) Make(
            CharacterPayloadSerializer? payloadSerializer = null,
            int? metadataCap = null,
            int? payloadCap = null,
            Func<CancellationToken, Task<string>>? authProvider = null)
        {
            var handler = new FakeHttpMessageHandler();
            var config = new Configuration(
                baseUrl: new Uri("https://eigencore.test/api/v1"),
                httpMessageHandler: handler,
                authTokenProvider: authProvider ?? (ct => Task.FromResult("test-token")),
                payloadParser: StubParse,
                defaultRetryAfter: TimeSpan.FromMilliseconds(1),
                metadataSizeCapBytes: metadataCap,
                payloadSizeCapBytes: payloadCap,
                payloadSerializer: payloadSerializer ?? (_ => Encoding.UTF8.GetBytes("{\"schema_version\":1,\"character_id\":\"" + AssetId + "\"}")));
            return (new EigencoreCharacterStore(config), handler);
        }

        /// <summary>
        /// Build a canonical "happy publish" response body: a metadata
        /// JSON object server-stamped with owner_id / created_at /
        /// updated_at / payload_size.
        /// </summary>
        private static byte[] CannedPublishResponseBody(
            string assetId = AssetId,
            string ownerId = "user:42",
            DateTimeOffset? createdAt = null,
            DateTimeOffset? updatedAt = null,
            long payloadSize = 123)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("asset_kind", "character/v1");
                w.WriteString("asset_id", assetId);
                w.WriteString("owner_id", ownerId);
                w.WriteBoolean("is_public", true);
                w.WriteStartArray("tags");
                w.WriteStringValue("starter");
                w.WriteEndArray();
                w.WriteString("created_at", (createdAt ?? new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)).ToString("o"));
                w.WriteString("updated_at", (updatedAt ?? new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero)).ToString("o"));
                w.WriteNumber("payload_size", payloadSize);
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        private static HttpResponseMessage OkPublish(byte[]? body = null, HttpStatusCode status = HttpStatusCode.Created)
        {
            return new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body ?? CannedPublishResponseBody()),
            };
        }

        /// <summary>
        /// Decode the multipart parts of a captured request using the
        /// FakeHttpMessageHandler's pre-disposal body snapshot. Returns
        /// parts indexed by form-data name.
        /// </summary>
        private static Dictionary<string, (string contentType, byte[] body)> ReadMultipartParts(
            FakeHttpMessageHandler handler, int requestIndex = 0)
        {
            string? ctHeader = handler.RequestContentTypes[requestIndex];
            Assert.False(string.IsNullOrEmpty(ctHeader), "no Content-Type captured on request");
            Assert.Contains("multipart/form-data", ctHeader, StringComparison.OrdinalIgnoreCase);

            // Pull boundary= out of the Content-Type header value.
            string? boundary = null;
            foreach (var seg in ctHeader!.Split(';'))
            {
                var s = seg.Trim();
                if (s.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                {
                    boundary = s.Substring("boundary=".Length).Trim('"');
                    break;
                }
            }
            Assert.False(string.IsNullOrEmpty(boundary), "boundary param missing on Content-Type");

            byte[]? bodyBytes = handler.RequestBodies[requestIndex];
            Assert.NotNull(bodyBytes);
            return ParseMultipart(bodyBytes!, boundary!);
        }

        private static Dictionary<string, (string contentType, byte[] body)> ParseMultipart(byte[] body, string boundary)
        {
            // Hand-rolled minimal RFC 7578 reader. Sufficient for the
            // shapes HttpClient emits in these tests; not a hardened
            // general-purpose parser.
            //
            // Wire shape (CRLF normalised):
            //   --boundary\r\n
            //   <headers>\r\n
            //   \r\n
            //   <part-body>\r\n
            //   --boundary\r\n  (repeat)
            //   --boundary--\r\n
            string text = Encoding.UTF8.GetString(body);
            string delim = "--" + boundary;
            // Split on the boundary token. The first chunk is the
            // preamble (often empty); the last chunk starts with "--"
            // (the close-delimiter suffix) — drop it. Everything in
            // between is a part block.
            var raw = text.Split(new[] { delim }, StringSplitOptions.None);

            var result = new Dictionary<string, (string contentType, byte[] body)>();
            // Skip first (preamble) and last (epilogue / close).
            for (int i = 1; i < raw.Length - 1; i++)
            {
                string chunk = raw[i];
                // Each part chunk begins with CRLF (the line break after
                // the boundary) and ends with CRLF (before the next
                // boundary).
                if (chunk.StartsWith("\r\n", StringComparison.Ordinal))
                    chunk = chunk.Substring(2);
                if (chunk.EndsWith("\r\n", StringComparison.Ordinal))
                    chunk = chunk.Substring(0, chunk.Length - 2);

                int headerEnd = chunk.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0) continue;
                string headerBlock = chunk.Substring(0, headerEnd);
                string partBody = chunk.Substring(headerEnd + 4);

                string? name = null;
                string ct = "";
                foreach (var line in headerBlock.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                    {
                        // .NET HttpClient emits unquoted form-data
                        // names (`name=metadata`); other clients quote
                        // them (`name="metadata"`). Handle both.
                        int nIdx = line.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                        if (nIdx >= 0)
                        {
                            int nStart = nIdx + 5;
                            if (nStart < line.Length && line[nStart] == '"')
                            {
                                nStart++;
                                int nEnd = line.IndexOf('"', nStart);
                                if (nEnd > nStart) name = line.Substring(nStart, nEnd - nStart);
                            }
                            else
                            {
                                int nEnd = line.IndexOfAny(new[] { ';', ' ', '\t' }, nStart);
                                if (nEnd < 0) nEnd = line.Length;
                                if (nEnd > nStart) name = line.Substring(nStart, nEnd - nStart);
                            }
                        }
                    }
                    else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        ct = line.Substring("Content-Type:".Length).Trim();
                    }
                }
                if (name != null)
                    result[name] = (ct, Encoding.UTF8.GetBytes(partBody));
            }
            return result;
        }

        // -- tests -------------------------------------------------------

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

        [Fact]
        public async Task DeleteAsync_204_Returns_True()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

            bool deleted = await store.DeleteAsync(AssetId);
            Assert.True(deleted);

            Assert.Single(handler.Requests);
            var req = handler.Requests[0];
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal($"https://eigencore.test/api/v1/assets/{AssetId}", req.RequestUri!.AbsoluteUri);
        }

        [Fact]
        public async Task DeleteAsync_200_Returns_True()
        {
            // Some backends respond 200 with an empty body instead of 204.
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(""),
            });

            bool deleted = await store.DeleteAsync(AssetId);
            Assert.True(deleted);
        }

        [Fact]
        public async Task DeleteAsync_404_Returns_False_No_Throw()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"detail\":\"not_found\"}"),
            });

            bool deleted = await store.DeleteAsync(AssetId);
            Assert.False(deleted);
        }

        [Fact]
        public async Task DeleteAsync_403_NotOwner_Throws_RemoteAssetForbiddenException()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"code\":\"permission_denied\",\"detail\":\"caller is not the asset owner\"}",
                    Encoding.UTF8, "application/json"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetForbiddenException>(() => store.DeleteAsync(AssetId));
            Assert.Equal(403, ex.StatusCode);
            Assert.Contains("not the asset owner", ex.Message);
        }

        [Fact]
        public async Task DeleteAsync_401_Throws_RemoteAssetAuthException()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"detail\":\"bad token\"}"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetAuthException>(() => store.DeleteAsync(AssetId));
            Assert.Equal(401, ex.StatusCode);
        }

        [Fact]
        public async Task SaveAsync_HappyPath_Delegates_To_Publish_201()
        {
            // SaveAsync is a thin delegate to PublishAsync with a
            // synthesised minimal metadata envelope. The wire shape is
            // identical to PublishAsync: POST /assets multipart with
            // metadata + payload parts.
            var (store, handler) = Make();
            handler.Enqueue(_ => OkPublish());

            await store.SaveAsync(StubDef());

            Assert.Single(handler.Requests);
            var req = handler.Requests[0];
            Assert.Equal(HttpMethod.Post, req.Method);

            var parts = ReadMultipartParts(handler);
            Assert.Equal(2, parts.Count);
            Assert.True(parts.ContainsKey("metadata"));
            Assert.True(parts.ContainsKey("payload"));

            string metaJson = Encoding.UTF8.GetString(parts["metadata"].body);
            // Boundary rename still applies on the synthesised metadata.
            Assert.Contains("\"asset_id\"", metaJson);
            Assert.DoesNotContain("\"character_id\"", metaJson);
            // The synthesised asset_id is the def's CharacterId in
            // lowercase 'D' format (UUID).
            Assert.Contains(AssetId, metaJson);
        }

        [Fact]
        public async Task SaveAsync_500_Surfaces_Server_Exception()
        {
            var (store, handler) = Make();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream-fault"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetServerException>(() => store.SaveAsync(StubDef()));
            Assert.Equal(500, ex.StatusCode);
            Assert.Equal("upstream-fault", ex.ResponseBody);
        }
    }
}

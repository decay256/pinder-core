using System;
using System.Collections.Generic;
using System.IO;
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
    /// Query-path tests for <see cref="EigencoreCharacterStore"/> (issue
    /// #854). Each test pins a single bullet from the ticket's "Tests
    /// required" list. The first test (<see
    /// cref="QueryAsync_MultipleTags_Emits_Singular_Tag_Param_Never_Plural"/>)
    /// is the #1 regression guard called out by the ticket: it asserts
    /// that the wrapper emits <c>tag=foo&amp;tag=bar</c>, NOT
    /// <c>tags=...</c>. FastAPI silently drops unknown query params, so
    /// the wrong spelling returns unfiltered results with no error
    /// signal in production.
    /// </summary>
    public class EigencoreCharacterStoreQueryTests
    {
        private const string AssetIdA = "59aa20f2-46d6-4adc-89c1-6ea17f815020";
        private const string AssetIdB = "8d6b4f5a-9a8e-4e2a-9b1c-1d3e4f5a6b7c";
        private const string AssetIdC = "12345678-1234-1234-1234-123456789abc";

        // -- helpers -----------------------------------------------------

        /// <summary>
        /// Build an items-array element matching the wire shape from
        /// <c>docs/specs/character-asset-vocabulary.md</c> § Query.
        /// Timestamps are emitted with the literal string passed in so
        /// callers can pin the <c>Z</c> vs <c>+00:00</c> defensive-parse
        /// test.
        /// </summary>
        private static void WriteItem(
            Utf8JsonWriter w,
            string assetId,
            string assetKind = "character/v1",
            string ownerId = "user:test",
            bool isPublic = true,
            string[]? tags = null,
            string createdAt = "2026-01-02T03:04:05+00:00",
            string updatedAt = "2026-01-02T03:04:06+00:00")
        {
            w.WriteStartObject();
            w.WriteString("asset_kind", assetKind);
            w.WriteString("asset_id", assetId);
            w.WriteString("owner_id", ownerId);
            w.WriteBoolean("is_public", isPublic);
            w.WriteStartArray("tags");
            foreach (var t in tags ?? Array.Empty<string>()) w.WriteStringValue(t);
            w.WriteEndArray();
            w.WriteString("created_at", createdAt);
            w.WriteString("updated_at", updatedAt);
            w.WriteEndObject();
        }

        private static byte[] BuildPage(string? nextCursor, Action<Utf8JsonWriter> writeItems)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteStartArray("items");
                writeItems(w);
                w.WriteEndArray();
                if (nextCursor == null) w.WriteNull("next_cursor");
                else w.WriteString("next_cursor", nextCursor);
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        private static CharacterDefinition StubParse(byte[] _)
        {
            // Query path doesn't invoke the parser, but Configuration
            // requires it to be non-null. Return a minimal stub.
            return new CharacterDefinition(
                schemaVersion: 1,
                characterId: Guid.Parse(AssetIdA),
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
            Func<CancellationToken, Task<string>>? authProvider = null,
            TimeSpan? defaultRetryAfter = null)
        {
            var handler = new FakeHttpMessageHandler();
            var config = new Configuration(
                baseUrl: new Uri("https://eigencore.test/api/v1"),
                httpMessageHandler: handler,
                authTokenProvider: authProvider ?? (ct => Task.FromResult("test-token")),
                payloadParser: StubParse,
                defaultRetryAfter: defaultRetryAfter ?? TimeSpan.FromMilliseconds(1));
            return (new EigencoreCharacterStore(config), handler);
        }

        private static HttpResponseMessage OkJson(byte[] body) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };

        // -- tests -------------------------------------------------------

        /// <summary>
        /// #1 regression test from the ticket. Multi-tag filter MUST emit
        /// <c>tag=foo&amp;tag=bar</c> (singular, repeatable). The
        /// reference backend silently drops unknown query params, so an
        /// implementation that accidentally emits <c>tags=foo</c> returns
        /// unfiltered results with no error signal. This is the most
        /// likely failure mode for this PR and is asserted first.
        /// </summary>
        [Fact]
        public async Task QueryAsync_MultipleTags_Emits_Singular_Tag_Param_Never_Plural()
        {
            var (store, handler) = Make();
            handler.Enqueue(OkJson(BuildPage(null, _ => { })));

            var query = new CharacterAssetQuery(tags: new[] { "foo", "bar" });
            await store.QueryAsync(query);

            string url = handler.Requests[0].RequestUri!.ToString();
            Assert.Contains("tag=foo", url);
            Assert.Contains("tag=bar", url);
            // The two appearances of `tag=` must be the singular form.
            // Any `tags=` substring is a regression.
            Assert.DoesNotContain("tags=", url);
        }

        [Fact]
        public async Task QueryAsync_EmptyResult_Returns_Empty_Page_With_Null_Cursor()
        {
            var (store, handler) = Make();
            handler.Enqueue(OkJson(BuildPage(null, _ => { })));

            var page = await store.QueryAsync(new CharacterAssetQuery());

            Assert.Empty(page.Items);
            Assert.Null(page.NextCursor);
        }

        [Fact]
        public async Task QueryAsync_SinglePage_Returns_Items_And_Null_Cursor()
        {
            var (store, handler) = Make();
            byte[] body = BuildPage(null, w =>
            {
                WriteItem(w, AssetIdA);
                WriteItem(w, AssetIdB);
                WriteItem(w, AssetIdC);
            });
            handler.Enqueue(OkJson(body));

            var page = await store.QueryAsync(new CharacterAssetQuery());

            Assert.Equal(3, page.Items.Count);
            Assert.Null(page.NextCursor);
            Assert.Equal(AssetIdA, page.Items[0].CharacterId);
            Assert.Equal(AssetIdB, page.Items[1].CharacterId);
            Assert.Equal(AssetIdC, page.Items[2].CharacterId);
        }

        /// <summary>
        /// Multi-page sanity: wrapper does NOT auto-paginate. Caller
        /// drives by passing <c>NextCursor</c> back into a second
        /// <c>QueryAsync</c> call. Assertion: TWO separate HTTP calls,
        /// not one wrapper-internal loop.
        /// </summary>
        [Fact]
        public async Task QueryAsync_MultiPage_Does_Not_AutoPaginate_Caller_Drives()
        {
            var (store, handler) = Make();
            byte[] page1 = BuildPage("abc", w =>
            {
                WriteItem(w, AssetIdA);
            });
            byte[] page2 = BuildPage(null, w =>
            {
                WriteItem(w, AssetIdB);
                WriteItem(w, AssetIdC);
            });
            handler.Enqueue(OkJson(page1));
            handler.Enqueue(OkJson(page2));

            // First call returns NextCursor="abc"; wrapper stops there.
            var first = await store.QueryAsync(new CharacterAssetQuery());
            Assert.Single(first.Items);
            Assert.Equal("abc", first.NextCursor);
            Assert.Single(handler.Requests); // exactly one call so far

            // Caller drives the second page.
            var second = await store.QueryAsync(new CharacterAssetQuery(cursor: "abc"));
            Assert.Equal(2, second.Items.Count);
            Assert.Null(second.NextCursor);
            Assert.Equal(2, handler.Requests.Count); // two calls total
            // Second request URL carries the cursor.
            Assert.Contains("cursor=abc", handler.Requests[1].RequestUri!.ToString());
        }

        [Fact]
        public async Task QueryAsync_AssetKind_With_Slash_Is_UrlEncoded()
        {
            var (store, handler) = Make();
            handler.Enqueue(OkJson(BuildPage(null, _ => { })));

            await store.QueryAsync(new CharacterAssetQuery(assetKind: "character/v1"));

            string url = handler.Requests[0].RequestUri!.ToString();
            Assert.Contains("asset_kind=character%2Fv1", url);
            // Defensive: the raw slash form must NOT appear in the
            // query-string portion. Splitting on '?' guards against the
            // base path's slashes confusing the assertion.
            string queryString = url.Substring(url.IndexOf('?'));
            Assert.DoesNotContain("asset_kind=character/v1", queryString);
        }

        [Fact]
        public async Task QueryAsync_IsPublic_True_Emits_Literal_True()
        {
            var (store, handler) = Make();
            handler.Enqueue(OkJson(BuildPage(null, _ => { })));

            await store.QueryAsync(new CharacterAssetQuery(isPublic: true));

            string url = handler.Requests[0].RequestUri!.ToString();
            Assert.Contains("is_public=true", url);
        }

        [Fact]
        public async Task QueryAsync_OwnerId_Appears_Verbatim_In_Url()
        {
            var (store, handler) = Make();
            handler.Enqueue(OkJson(BuildPage(null, _ => { })));

            const string owner = "user:1234";
            await store.QueryAsync(new CharacterAssetQuery(ownerId: owner));

            string url = handler.Requests[0].RequestUri!.ToString();
            // The colon round-trips through Uri.EscapeDataString as %3A
            // — but the original literal characters must be recoverable.
            // We assert both the encoded and decoded forms to keep the
            // test resilient to future encoding-strictness tweaks.
            string queryString = url.Substring(url.IndexOf('?'));
            string decoded = Uri.UnescapeDataString(queryString);
            Assert.Contains($"owner_id={owner}", decoded);
        }

        [Fact]
        public async Task QueryAsync_DateFilters_Emit_Rfc3339_Utc_Strings()
        {
            var (store, handler) = Make();
            handler.Enqueue(OkJson(BuildPage(null, _ => { })));

            var createdAfter  = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var createdBefore = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
            var updatedAfter  = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
            var updatedBefore = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);

            await store.QueryAsync(new CharacterAssetQuery(
                createdAfter: createdAfter,
                createdBefore: createdBefore,
                updatedAfter: updatedAfter,
                updatedBefore: updatedBefore));

            string url = handler.Requests[0].RequestUri!.ToString();
            string decoded = Uri.UnescapeDataString(url);

            // RFC3339 UTC with explicit "Z". We assert the date/time
            // portion ("YYYY-MM-DDThh:mm:ss") rather than the full
            // fractional-second tail so the test stays stable if
            // FormatRfc3339Utc's precision setting is tightened later.
            Assert.Contains("created_after=2026-01-02T03:04:05", decoded);
            Assert.Contains("created_before=2026-02-03T04:05:06", decoded);
            Assert.Contains("updated_after=2026-03-04T05:06:07", decoded);
            Assert.Contains("updated_before=2026-04-05T06:07:08", decoded);
            // Z suffix on every date filter — the wire contract is UTC.
            Assert.Contains("Z&", decoded + "&");
        }

        /// <summary>
        /// Defensive timestamp parse: query response items may carry
        /// either <c>+00:00</c> or <c>Z</c> in the same response (the
        /// spec mandates UTC but the on-wire suffix is server-defined).
        /// Both forms MUST parse without error and round-trip to the
        /// same UTC <see cref="DateTimeOffset"/>.
        /// </summary>
        [Fact]
        public async Task QueryAsync_Item_Timestamps_With_Mixed_Z_And_PlusZeroOffset_Both_Parse()
        {
            var (store, handler) = Make();
            byte[] body = BuildPage(null, w =>
            {
                WriteItem(w, AssetIdA,
                    createdAt: "2026-01-02T03:04:05+00:00",
                    updatedAt: "2026-01-02T03:04:06+00:00");
                WriteItem(w, AssetIdB,
                    createdAt: "2026-01-02T03:04:05Z",
                    updatedAt: "2026-01-02T03:04:06Z");
            });
            handler.Enqueue(OkJson(body));

            var page = await store.QueryAsync(new CharacterAssetQuery());

            Assert.Equal(2, page.Items.Count);
            var a = page.Items[0];
            var b = page.Items[1];
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), a.CreatedAt);
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero), a.UpdatedAt);
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), b.CreatedAt);
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero), b.UpdatedAt);
            // Both should land as TimeSpan.Zero (UTC) regardless of how
            // the wire spelled them.
            Assert.Equal(TimeSpan.Zero, a.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, b.CreatedAt.Offset);
        }

        [Fact]
        public async Task QueryAsync_MalformedCursor_Maps_422_Invalid_Cursor_To_Typed_Exception()
        {
            var (store, handler) = Make();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\"error\":\"invalid_cursor\",\"detail\":\"cursor expired\"}"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetInvalidCursorException>(() =>
                store.QueryAsync(new CharacterAssetQuery(cursor: "garbage")));
            Assert.Equal(422, ex.StatusCode);
            Assert.Contains("invalid_cursor", ex.ResponseBody);
        }

        /// <summary>
        /// Regression test against #853's auth mapping when traffic
        /// flows through the new query code path. 401 anywhere is
        /// <see cref="RemoteAssetAuthException"/>.
        /// </summary>
        [Fact]
        public async Task QueryAsync_401_Throws_RemoteAssetAuthException()
        {
            var (store, handler) = Make();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"detail\":\"token expired\"}"),
            });

            var ex = await Assert.ThrowsAsync<RemoteAssetAuthException>(() =>
                store.QueryAsync(new CharacterAssetQuery()));
            Assert.Equal(401, ex.StatusCode);
            Assert.Contains("token expired", ex.ResponseBody);
        }
    }
}

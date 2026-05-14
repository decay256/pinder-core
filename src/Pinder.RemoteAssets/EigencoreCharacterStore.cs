using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.RemoteAssets.Exceptions;

namespace Pinder.RemoteAssets
{
    /// <summary>
    /// Eigencore-backed <see cref="IRemoteCharacterStore"/>. Talks raw HTTP
    /// against the asset-backend contract documented in
    /// <c>docs/specs/character-asset-vocabulary.md</c>.
    ///
    /// This is the ONLY assembly in the pinder-core repo that is allowed
    /// to know an eigencore-shaped backend exists. Pinder.Core, the engine,
    /// and the session runner do not reference this type. See AGENTS.md
    /// ("Eigencore is a THIRD-PARTY APP from Pinder's perspective") and
    /// lesson §35 of pinder-web/LESSONS_LEARNED.md.
    ///
    /// Sub-PR scope:
    /// #853 — read path (<see cref="LoadAsync"/>, <see cref="GetMetadataAsync"/>,
    ///        <see cref="ExistsAsync"/>).
    /// #854 — query / paging path (<see cref="QueryAsync"/>).
    /// #855 — write path (<see cref="PublishAsync"/>,
    ///        <see cref="SaveAsync"/>, <see cref="DeleteAsync"/>).
    /// <see cref="ListIdsAsync"/> remains <see cref="NotSupportedException"/>:
    /// the v1 wire contract has no list-all endpoint; discovery happens
    /// via <see cref="QueryAsync"/>.
    /// </summary>
    public sealed class EigencoreCharacterStore : IRemoteCharacterStore
    {
        private const string AssetMetadataHeader = "X-Asset-Metadata";

        private readonly Configuration _config;
        private readonly HttpClient _http;

        public EigencoreCharacterStore(Configuration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // disposeHandler:false — the caller owns the lifetime of the
            // injected HttpMessageHandler (socket-exhaustion guidance).
            _http = new HttpClient(_config.HttpMessageHandler, disposeHandler: false)
            {
                Timeout = _config.RequestTimeout,
            };

            // Normalize BaseUrl to end with a single slash so relative
            // "assets/{id}" appends cleanly.
            var baseStr = _config.BaseUrl.AbsoluteUri;
            if (!baseStr.EndsWith("/", StringComparison.Ordinal))
                baseStr += "/";
            _http.BaseAddress = new Uri(baseStr, UriKind.Absolute);

            // Cap response buffer size proportional to PayloadSizeCapBytes
            // (defence-in-depth against a compromised or misconfigured
            // eigencore — the contract allows at most PayloadSizeCapBytes
            // plus metadata envelope + HTTP framing; *4 gives generous
            // headroom without permitting unbounded responses).
            _http.MaxResponseContentBufferSize = Math.Max(_config.PayloadSizeCapBytes, 1024 * 1024) * 4;
        }

        // ---- IRemoteCharacterStore read path (this PR) ---------------------

        /// <summary>
        /// <c>GET {baseUrl}/assets/{asset_id}</c>. Returns the parsed
        /// payload or <c>null</c> on 404.
        /// </summary>
        public async Task<CharacterDefinition?> LoadAsync(string characterId, CancellationToken ct = default)
        {
            ValidateId(characterId);

            (byte[] payload, CharacterAssetMetadata _, bool found) = await FetchAsync(characterId, ct).ConfigureAwait(false);
            if (!found) return null;
            // Run the injected parser. Exceptions from the parser
            // propagate to the caller verbatim (per ICharacterStore's
            // FormatException contract).
            return _config.PayloadParser(payload);
        }

        /// <summary>
        /// <c>GET {baseUrl}/assets/{asset_id}</c>. Returns the metadata
        /// only (drops the payload bytes) or <c>null</c> on 404.
        ///
        /// Implementation note: the reference backend ships the metadata
        /// in an <c>X-Asset-Metadata</c> response header on the same GET
        /// endpoint as <see cref="LoadAsync"/>. There is no separate
        /// metadata-only endpoint in v1, so this method issues the same
        /// GET and discards the payload body. Sharing GET keeps the wire
        /// surface flat at the cost of one extra body read; a future
        /// optimization could switch to HTTP HEAD if the backend grows
        /// HEAD support.
        /// </summary>
        public async Task<CharacterAssetMetadata?> GetMetadataAsync(string characterId, CancellationToken ct = default)
        {
            ValidateId(characterId);

            (byte[] _, CharacterAssetMetadata meta, bool found) = await FetchAsync(characterId, ct).ConfigureAwait(false);
            return found ? meta : null;
        }

        /// <summary>
        /// <c>GET {baseUrl}/assets/{asset_id}</c>. Returns <c>true</c> on
        /// 200, <c>false</c> on 404. v1 has no HEAD endpoint on the
        /// reference backend, so this is a full GET that discards the
        /// response; sub-PR #855 may switch to HEAD if that's added.
        /// </summary>
        public async Task<bool> ExistsAsync(string characterId, CancellationToken ct = default)
        {
            ValidateId(characterId);

            (byte[] _, CharacterAssetMetadata __, bool found) = await FetchAsync(characterId, ct).ConfigureAwait(false);
            return found;
        }

        // ---- Sub-PR boundary: not implemented in #853 ----------------------

        public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException(
                "ListIdsAsync is not part of the v1 eigencore wire contract. " +
                "Discovery happens via QueryAsync (sub-PR #854) which returns metadata pages.");

        // ---- IRemoteCharacterStore query path (#854) ---------------------

        /// <summary>
        /// <c>GET {baseUrl}/assets?&lt;encoded filters&gt;</c>. Returns one
        /// page of metadata matching <paramref name="query"/>. The wrapper
        /// does NOT auto-paginate; the caller drives by passing the
        /// returned <see cref="CharacterAssetPage.NextCursor"/> back in a
        /// subsequent <see cref="CharacterAssetQuery.Cursor"/> until it
        /// comes back null.
        ///
        /// Wire contract: see <c>docs/specs/character-asset-vocabulary.md</c>
        /// § Query semantics + § Wire format / Query. Key invariants:
        /// <list type="bullet">
        ///   <item>Tag filter param is <c>tag</c> (singular, repeatable),
        ///         NOT <c>tags</c>. The reference backend silently drops
        ///         unknown params, so getting this wrong returns
        ///         unfiltered results with no error signal.</item>
        ///   <item><c>asset_kind=character/v1</c> URL-encodes the slash
        ///         to <c>character%2Fv1</c>.</item>
        ///   <item>Cursor is opaque pass-through. 422 with
        ///         <c>error=invalid_cursor</c> in the body throws
        ///         <see cref="RemoteAssetInvalidCursorException"/>.</item>
        ///   <item>Date filters are RFC3339 UTC. Wire response timestamps
        ///         are parsed defensively (both <c>Z</c> and
        ///         <c>+00:00</c> suffixes accepted) via the shared
        ///         <see cref="CharacterAssetMetadataParser"/>.</item>
        /// </list>
        /// </summary>
        public async Task<CharacterAssetPage> QueryAsync(CharacterAssetQuery query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            bool retried = false;

            while (true)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, BuildQueryUri(query)))
                {
                    await AttachAuthAsync(req, ct).ConfigureAwait(false);

                    HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    try
                    {
                        int status = (int)resp.StatusCode;

                        if (status == 200)
                        {
                            byte[] body = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return ParseQueryResponse(body);
                        }

                        if (status == 401)
                        {
                            string body401 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetAuthException(
                                "Eigencore returned 401 for GET assets query.",
                                responseBody: body401);
                        }

                        if (status == 422)
                        {
                            string body422 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            (string? errorCode, IReadOnlyList<string> errors) = ParseValidationBody(body422);
                            if (string.Equals(errorCode, "invalid_cursor", StringComparison.Ordinal))
                            {
                                throw new RemoteAssetInvalidCursorException(
                                    "Eigencore returned 422 invalid_cursor for GET assets query.",
                                    responseBody: body422);
                            }
                            throw new RemoteAssetValidationException(
                                "Eigencore returned 422 for GET assets query.",
                                errors: errors,
                                responseBody: body422);
                        }

                        if (status == 429)
                        {
                            TimeSpan delay = ParseRetryAfter(resp) ?? _config.DefaultRetryAfter;
                            string body429 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            if (retried)
                            {
                                throw new RemoteAssetRateLimitException(
                                    "Eigencore returned 429 for GET assets query after one retry.",
                                    retryAfter: delay,
                                    responseBody: body429);
                            }
                            retried = true;
                            resp.Dispose();
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (status >= 500 && status <= 599)
                        {
                            string body5xx = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetServerException(
                                $"Eigencore returned {status} for GET assets query.",
                                statusCode: status,
                                responseBody: body5xx);
                        }

                        string bodyOther = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                        throw new RemoteAssetServerException(
                            $"Eigencore returned unexpected status {status} for GET assets query.",
                            statusCode: status,
                            responseBody: bodyOther);
                    }
                    finally
                    {
                        resp.Dispose();
                    }
                }
            }
        }

        // ---- IRemoteCharacterStore write path (#855) ---------------------

        /// <summary>
        /// <c>POST {baseUrl}/assets</c> as <c>multipart/form-data</c> with
        /// exactly two parts named <c>metadata</c> and <c>payload</c>.
        ///
        /// Pre-validation: both parts are serialised and size-checked
        /// BEFORE the HTTP request is sent. An oversized metadata
        /// (>4 KiB by default) or payload (>256 KiB by default) throws
        /// <see cref="RemoteAssetTooLargeException"/> with no network
        /// round-trip. The reference backend would return 422 anyway,
        /// but the local check is the better contract — callers get a
        /// typed exception immediately.
        ///
        /// POST is upsert: publishing the same <c>asset_id</c> twice
        /// overwrites. Both 201 (created) and 200 (overwritten) are
        /// treated as success.
        ///
        /// Wire contract: see <c>docs/specs/character-asset-vocabulary.md</c>
        /// § Publish. The serialised metadata uses <c>asset_id</c>, never
        /// <c>character_id</c> — see
        /// <see cref="CharacterAssetMetadataSerializer"/>.
        /// </summary>
        public async Task<CharacterAssetMetadata> PublishAsync(
            CharacterDefinition def,
            CharacterAssetMetadata metadata,
            CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            // --- 1. Serialise both sides and pre-validate caps ---------
            byte[] metaBytes = CharacterAssetMetadataSerializer.SerializeBytes(metadata);
            if (metaBytes.Length > _config.MetadataSizeCapBytes)
            {
                throw new RemoteAssetTooLargeException(
                    $"Serialised metadata is {metaBytes.Length} bytes, exceeds cap {_config.MetadataSizeCapBytes} bytes. " +
                    "No HTTP request was sent (pre-validation).",
                    subject: "metadata");
            }

            byte[] payloadBytes = SerializePayload(def);
            if (payloadBytes.Length > _config.PayloadSizeCapBytes)
            {
                throw new RemoteAssetTooLargeException(
                    $"Serialised payload is {payloadBytes.Length} bytes, exceeds cap {_config.PayloadSizeCapBytes} bytes. " +
                    "No HTTP request was sent (pre-validation).",
                    subject: "payload");
            }

            // --- 2. Build + send the multipart request -----------------
            bool retried = false;

            while (true)
            {
                // Build a FRESH multipart per attempt; HttpContent is
                // consumed on send and is not safely reusable for retry.
                using (var content = BuildPublishMultipart(metaBytes, payloadBytes))
                using (var req = new HttpRequestMessage(HttpMethod.Post, new Uri("assets", UriKind.Relative))
                {
                    Content = content,
                })
                {
                    await AttachAuthAsync(req, ct).ConfigureAwait(false);

                    HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    try
                    {
                        int status = (int)resp.StatusCode;

                        // Upsert: 201 (newly created) and 200 (overwrote
                        // an existing asset with the same asset_id) are
                        // both success.
                        if (status == 200 || status == 201)
                        {
                            byte[] body = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return ParsePublishResponse(body);
                        }

                        if (status == 401)
                        {
                            string body401 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetAuthException(
                                "Eigencore returned 401 for POST assets.",
                                responseBody: body401);
                        }

                        if (status == 403)
                        {
                            string body403 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw BuildForbiddenException(body403, "POST assets");
                        }

                        if (status == 422)
                        {
                            string body422 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            (string? errorCode, IReadOnlyList<string> errors) = ParseValidationBody(body422);
                            if (string.Equals(errorCode, "metadata_too_large", StringComparison.Ordinal))
                            {
                                throw new RemoteAssetTooLargeException(
                                    "Eigencore returned 422 metadata_too_large for POST assets.",
                                    subject: "metadata",
                                    responseBody: body422);
                            }
                            if (string.Equals(errorCode, "payload_too_large", StringComparison.Ordinal))
                            {
                                throw new RemoteAssetTooLargeException(
                                    "Eigencore returned 422 payload_too_large for POST assets.",
                                    subject: "payload",
                                    responseBody: body422);
                            }
                            // invalid_multipart and all other 422 codes
                            // surface as a generic validation exception.
                            throw new RemoteAssetValidationException(
                                $"Eigencore returned 422 for POST assets (code={errorCode ?? "<none>"}).",
                                errors: errors,
                                responseBody: body422);
                        }

                        if (status == 429)
                        {
                            TimeSpan delay = ParseRetryAfter(resp) ?? _config.DefaultRetryAfter;
                            string body429 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            if (retried)
                            {
                                throw new RemoteAssetRateLimitException(
                                    "Eigencore returned 429 for POST assets after one retry.",
                                    retryAfter: delay,
                                    responseBody: body429);
                            }
                            retried = true;
                            resp.Dispose();
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (status >= 500 && status <= 599)
                        {
                            string body5xx = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetServerException(
                                $"Eigencore returned {status} for POST assets.",
                                statusCode: status,
                                responseBody: body5xx);
                        }

                        string bodyOther = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                        throw new RemoteAssetServerException(
                            $"Eigencore returned unexpected status {status} for POST assets.",
                            statusCode: status,
                            responseBody: bodyOther);
                    }
                    finally
                    {
                        resp.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Save = publish. Per the wire contract, <c>POST /assets</c> is
        /// the upsert: two publishes with the same <c>asset_id</c>
        /// overwrite. The base <see cref="ICharacterStore.SaveAsync"/>
        /// surface takes only the <see cref="CharacterDefinition"/>; for
        /// the remote store the metadata envelope is required, so this
        /// method synthesises a minimal <see cref="CharacterAssetMetadata"/>
        /// from the def (asset_id from CharacterId, empty owner, no tags,
        /// is_public=false, timestamps stamped server-side) and delegates
        /// to <see cref="PublishAsync"/>. Callers that need to control
        /// tags / is_public / owner go through <see cref="PublishAsync"/>
        /// directly.
        /// </summary>
        public Task SaveAsync(CharacterDefinition def, CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            // The wire wants asset_id in UUID 'D' form (lowercase,
            // hyphenated). Guid.ToString("d") produces exactly that.
            string assetId = def.CharacterId.ToString("d");
            var metadata = new CharacterAssetMetadata(
                characterId: assetId,
                ownerId: string.Empty,
                tags: Array.Empty<string>(),
                isPublic: false,
                // CreatedAt / UpdatedAt are server-stamped; the values
                // here are NOT serialised (the metadata serialiser drops
                // server-controlled fields). They satisfy the POCO ctor
                // which forbids defaulted DateTimeOffset only by being
                // present.
                createdAt: DateTimeOffset.MinValue,
                updatedAt: DateTimeOffset.MinValue,
                assetKind: CharacterAssetMetadata.AssetKindCharacterV1);
            return PublishAsync(def, metadata, ct);
        }

        /// <summary>
        /// <c>DELETE {baseUrl}/assets/{asset_id}</c>. Returns <c>true</c>
        /// when the server confirms the delete (200 or 204) and
        /// <c>false</c> when the asset was already gone (404 — idempotent
        /// delete is not an error). 403 means the caller is not the
        /// asset owner; 401 means bad creds. Other status codes map per
        /// the shared error policy.
        /// </summary>
        public async Task<bool> DeleteAsync(string characterId, CancellationToken ct = default)
        {
            ValidateId(characterId);

            bool retried = false;

            while (true)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Delete, BuildAssetUri(characterId)))
                {
                    await AttachAuthAsync(req, ct).ConfigureAwait(false);

                    HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    try
                    {
                        int status = (int)resp.StatusCode;

                        if (status == 200 || status == 204)
                        {
                            // Drain to allow keep-alive reuse.
                            if (resp.Content != null)
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return true;
                        }

                        if (status == 404)
                        {
                            // Idempotent delete: already gone is success-ish.
                            if (resp.Content != null)
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return false;
                        }

                        if (status == 401)
                        {
                            string body401 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetAuthException(
                                $"Eigencore returned 401 for DELETE assets/{characterId}.",
                                responseBody: body401);
                        }

                        if (status == 403)
                        {
                            string body403 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            // On DELETE the only forbidden case is
                            // not-owner; build a focused exception
                            // message rather than the generic
                            // BuildForbiddenException (which assumes a
                            // reserved-prefix-or-not-owner discriminator
                            // on the publish path).
                            throw new RemoteAssetForbiddenException(
                                $"Eigencore returned 403 for DELETE assets/{characterId} (caller is not the asset owner).",
                                responseBody: body403);
                        }

                        if (status == 422)
                        {
                            string body422 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            (_, IReadOnlyList<string> errors) = ParseValidationBody(body422);
                            throw new RemoteAssetValidationException(
                                $"Eigencore returned 422 for DELETE assets/{characterId}.",
                                errors: errors,
                                responseBody: body422);
                        }

                        if (status == 429)
                        {
                            TimeSpan delay = ParseRetryAfter(resp) ?? _config.DefaultRetryAfter;
                            string body429 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            if (retried)
                            {
                                throw new RemoteAssetRateLimitException(
                                    $"Eigencore returned 429 for DELETE assets/{characterId} after one retry.",
                                    retryAfter: delay,
                                    responseBody: body429);
                            }
                            retried = true;
                            resp.Dispose();
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (status >= 500 && status <= 599)
                        {
                            string body5xx = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetServerException(
                                $"Eigencore returned {status} for DELETE assets/{characterId}.",
                                statusCode: status,
                                responseBody: body5xx);
                        }

                        string bodyOther = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                        throw new RemoteAssetServerException(
                            $"Eigencore returned unexpected status {status} for DELETE assets/{characterId}.",
                            statusCode: status,
                            responseBody: bodyOther);
                    }
                    finally
                    {
                        resp.Dispose();
                    }
                }
            }
        }

        // ---- write-path internals -----------------------------------------

        /// <summary>
        /// Serialise the def to payload bytes, using the injected
        /// <see cref="CharacterPayloadSerializer"/> if configured, otherwise
        /// falling back to <see cref="CharacterDefinitionWriter.Write"/>
        /// (UTF-8). Tests inject a stub to control the byte count.
        /// </summary>
        private byte[] SerializePayload(CharacterDefinition def)
        {
            if (_config.PayloadSerializer != null)
                return _config.PayloadSerializer(def) ?? Array.Empty<byte>();
            string json = CharacterDefinitionWriter.Write(def);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        }

        /// <summary>
        /// Construct a two-part multipart/form-data body with parts
        /// exactly named <c>metadata</c> (application/json) and
        /// <c>payload</c> (application/octet-stream). The boundary is
        /// HttpClient-generated. The wire contract is strict: any other
        /// part name, additional parts, or missing parts surface
        /// server-side as 422 <c>invalid_multipart</c>.
        /// </summary>
        private static MultipartFormDataContent BuildPublishMultipart(byte[] metaBytes, byte[] payloadBytes)
        {
            var multipart = new MultipartFormDataContent();

            var metaPart = new ByteArrayContent(metaBytes);
            metaPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            // Form-data name MUST be exactly "metadata" (per spec). Pass
            // unquoted; HttpClient quotes it on the wire as required by
            // RFC 7578.
            multipart.Add(metaPart, "metadata");

            var payloadPart = new ByteArrayContent(payloadBytes);
            // Spec calls the payload "any content-type, opaque bytes";
            // application/octet-stream is the safest neutral choice.
            payloadPart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(payloadPart, "payload");

            return multipart;
        }

        /// <summary>
        /// Parse the POST /assets success body into a populated
        /// <see cref="CharacterAssetMetadata"/>. The body is a single
        /// metadata object (same shape as a query response item or the
        /// X-Asset-Metadata header value), so we reuse the parser.
        /// </summary>
        private static CharacterAssetMetadata ParsePublishResponse(byte[] body)
        {
            if (body == null || body.Length == 0)
                throw new RemoteAssetMalformedMetadataException(
                    "Publish response body is empty.");
            return CharacterAssetMetadataParser.ParseBytes(body);
        }

        /// <summary>
        /// Build a focused <see cref="RemoteAssetForbiddenException"/>
        /// for a 403 on the publish path. Eigencore returns
        /// <c>code=permission_denied</c> for both reserved-prefix
        /// violations (with the offending tag in the message or a
        /// dedicated field) and not-owner overwrite attempts. We do a
        /// best-effort body parse to surface the prefix when present
        /// (the test asserts the offending prefix is in the message).
        /// </summary>
        private static RemoteAssetForbiddenException BuildForbiddenException(string body403, string opLabel)
        {
            // Try to dig the offending tag/prefix out of the body.
            string? offendingPrefix = ExtractForbiddenPrefix(body403);
            string msg = offendingPrefix != null
                ? $"Eigencore returned 403 permission_denied for {opLabel}: reserved tag prefix '{offendingPrefix}' is not allowed for this caller."
                : $"Eigencore returned 403 permission_denied for {opLabel}.";
            return new RemoteAssetForbiddenException(msg, responseBody: body403);
        }

        /// <summary>
        /// Best-effort extraction of the offending tag/prefix from a
        /// 403 body. The spec text says the server returns
        /// <c>code=permission_denied</c> plus enough context to identify
        /// the prefix; the exact body shape isn't pinned by the spec.
        /// We accept a few common shapes:
        /// <list type="bullet">
        ///   <item><c>{"detail": "reserved prefix 'auto-' is not allowed"}</c></item>
        ///   <item><c>{"prefix": "auto-"}</c></item>
        ///   <item><c>{"tag": "auto-foo"}</c> — we derive the prefix.</item>
        /// </list>
        /// Returns null when nothing matches — the exception message then
        /// falls back to a generic form (the test for reserved-prefix
        /// also asserts <see cref="RemoteAssetException.ResponseBody"/>,
        /// which always contains the full body).
        /// </summary>
        private static string? ExtractForbiddenPrefix(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;

                    // Direct "prefix" field.
                    if (root.TryGetProperty("prefix", out var pEl)
                        && pEl.ValueKind == JsonValueKind.String)
                    {
                        var p = pEl.GetString();
                        if (!string.IsNullOrEmpty(p)) return p;
                    }

                    // "tag" field — derive prefix up to the first '-' (inclusive).
                    if (root.TryGetProperty("tag", out var tEl)
                        && tEl.ValueKind == JsonValueKind.String)
                    {
                        var tag = tEl.GetString();
                        if (!string.IsNullOrEmpty(tag))
                        {
                            int dash = tag!.IndexOf('-');
                            if (dash > 0)
                                return tag.Substring(0, dash + 1);
                            return tag;
                        }
                    }

                    // Scan "detail" / "message" for the reserved prefixes
                    // we know about (auto- / official-).
                    foreach (var key in new[] { "detail", "message", "error" })
                    {
                        if (root.TryGetProperty(key, out var mEl)
                            && mEl.ValueKind == JsonValueKind.String)
                        {
                            var s = mEl.GetString() ?? string.Empty;
                            foreach (var candidate in new[] { "auto-", "official-" })
                            {
                                if (s.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                                    return candidate;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // body wasn't JSON — fall through.
            }

            // Last-ditch: look for the literal prefixes in the raw body.
            foreach (var candidate in new[] { "auto-", "official-" })
            {
                if (body.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            }
            return null;
        }

        // ---- internals -----------------------------------------------------

        private static void ValidateId(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
        }

        /// <summary>
        /// Core GET-and-map. Returns <c>(payload, meta, true)</c> on 200
        /// or <c>(empty, null, false)</c> on 404. All other status codes
        /// throw the typed exception per the read-path error mapping.
        /// </summary>
        private async Task<(byte[] payload, CharacterAssetMetadata meta, bool found)> FetchAsync(
            string characterId,
            CancellationToken ct)
        {
            // Single retry budget for 429.
            bool retried = false;

            while (true)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, BuildAssetUri(characterId)))
                {
                    await AttachAuthAsync(req, ct).ConfigureAwait(false);

                    HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    try
                    {
                        int status = (int)resp.StatusCode;

                        if (status == 200)
                        {
                            string? headerValue = ExtractSingleHeader(resp, AssetMetadataHeader);
                            byte[] body = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            byte[] metaBytes = CharacterAssetMetadataParser.DecodeHeader(headerValue);
                            CharacterAssetMetadata meta = CharacterAssetMetadataParser.ParseBytes(metaBytes);
                            return (body, meta, true);
                        }

                        if (status == 404)
                        {
                            // Drain to allow connection reuse, then return
                            // the negative signal.
                            await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return (Array.Empty<byte>(), null!, false);
                        }

                        if (status == 401)
                        {
                            string body401 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetAuthException(
                                $"Eigencore returned 401 for GET assets/{characterId}.",
                                responseBody: body401);
                        }

                        if (status == 429)
                        {
                            TimeSpan delay = ParseRetryAfter(resp) ?? _config.DefaultRetryAfter;
                            string body429 = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            if (retried)
                            {
                                throw new RemoteAssetRateLimitException(
                                    $"Eigencore returned 429 for GET assets/{characterId} after one retry.",
                                    retryAfter: delay,
                                    responseBody: body429);
                            }
                            retried = true;
                            // Dispose now; we'll issue a fresh request.
                            resp.Dispose();
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue; // retry loop
                        }

                        if (status >= 500 && status <= 599)
                        {
                            string body5xx = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetServerException(
                                $"Eigencore returned {status} for GET assets/{characterId}.",
                                statusCode: status,
                                responseBody: body5xx);
                        }

                        // Any other status code (including 4xx the read
                        // path doesn't otherwise enumerate, like 403 / 422)
                        // surfaces as a server exception with the status
                        // attached. The stubs for 403/422 typed exceptions
                        // are reserved for the write-path sub-PRs that
                        // actually trigger them.
                        string bodyOther = await SafeReadBodyAsync(resp).ConfigureAwait(false);
                        throw new RemoteAssetServerException(
                            $"Eigencore returned unexpected status {status} for GET assets/{characterId}.",
                            statusCode: status,
                            responseBody: bodyOther);
                    }
                    finally
                    {
                        resp.Dispose();
                    }
                }
            }
        }

        private Uri BuildAssetUri(string characterId)
        {
            // Relative path; HttpClient.BaseAddress is set to BaseUrl + "/"
            // in the ctor.
            return new Uri($"assets/{Uri.EscapeDataString(characterId)}", UriKind.Relative);
        }

        /// <summary>
        /// Build the relative <c>assets?...</c> URI for a query. Each
        /// scalar filter contributes at most one <c>key=value</c> pair;
        /// <c>tag</c> is emitted as a repeated <c>tag=...</c> param
        /// (one per value, AND semantics). Values are URL-encoded with
        /// <see cref="Uri.EscapeDataString"/>, which encodes <c>/</c>
        /// to <c>%2F</c> as required for <c>asset_kind=character/v1</c>.
        /// </summary>
        private static Uri BuildQueryUri(CharacterAssetQuery query)
        {
            // Pre-built so the implementation reads top-to-bottom.
            var sb = new StringBuilder("assets");
            bool first = true;

            void Append(string key, string value)
            {
                sb.Append(first ? '?' : '&');
                first = false;
                sb.Append(key);
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(value));
            }

            if (!string.IsNullOrEmpty(query.AssetKind))
                Append("asset_kind", query.AssetKind!);

            // OwnerId distinguishes null ("any owner") from empty
            // string ("explicit ownerless / service-owned"); the spec
            // calls this out. Empty string is a legitimate filter value.
            if (query.OwnerId != null)
                Append("owner_id", query.OwnerId);

            if (query.IsPublic.HasValue)
                Append("is_public", query.IsPublic.Value ? "true" : "false");

            // tag (singular, repeatable). Emitting `tags=a,b` or
            // `tags=a` is the canonical bug this PR pins against; the
            // backend silently drops `tags=` and returns unfiltered
            // results. Code-review + the regression grep both watchdog
            // this.
            if (query.Tags != null)
            {
                foreach (var t in query.Tags)
                {
                    if (!string.IsNullOrEmpty(t))
                        Append("tag", t);
                }
            }

            // Limit always emitted — the wire contract gives the server
            // freedom to choose a different default, and CharacterAssetQuery
            // already clamps to a sensible client default.
            Append("limit", query.Limit.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(query.Cursor))
                Append("cursor", query.Cursor!);

            if (query.CreatedAfter.HasValue)
                Append("created_after", FormatRfc3339Utc(query.CreatedAfter.Value));
            if (query.CreatedBefore.HasValue)
                Append("created_before", FormatRfc3339Utc(query.CreatedBefore.Value));
            if (query.UpdatedAfter.HasValue)
                Append("updated_after", FormatRfc3339Utc(query.UpdatedAfter.Value));
            if (query.UpdatedBefore.HasValue)
                Append("updated_before", FormatRfc3339Utc(query.UpdatedBefore.Value));

            return new Uri(sb.ToString(), UriKind.Relative);
        }

        /// <summary>
        /// Format a <see cref="DateTimeOffset"/> as RFC3339 UTC with the
        /// <c>Z</c> zone suffix. The wire contract is UTC; we normalize
        /// to UTC here and emit the canonical <c>Z</c> form regardless of
        /// the caller's original offset. Sub-second precision is
        /// preserved at the .NET DateTime default (100ns ticks).
        /// </summary>
        private static string FormatRfc3339Utc(DateTimeOffset ts)
        {
            // "o" round-trip yields "2026-01-02T03:04:05.0000000+00:00" for
            // a UTC offset; switching to UTC and using the explicit "Z"
            // form keeps the wire string compact and matches what
            // RFC3339-strict parsers expect.
            return ts.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse the <c>{"items":[...], "next_cursor":"..."}</c> response
        /// envelope into a <see cref="CharacterAssetPage"/>. Each item is
        /// the same shape as the read-path <c>X-Asset-Metadata</c>
        /// payload, so we delegate per-item parsing to
        /// <see cref="CharacterAssetMetadataParser.ParseElement"/> for
        /// consistency.
        /// </summary>
        private static CharacterAssetPage ParseQueryResponse(byte[] body)
        {
            if (body == null || body.Length == 0)
                throw new RemoteAssetMalformedMetadataException(
                    "Query response body is empty.");

            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        throw new RemoteAssetMalformedMetadataException(
                            "Query response root must be a JSON object.");

                    var items = new List<CharacterAssetMetadata>();
                    if (root.TryGetProperty("items", out var itemsEl))
                    {
                        if (itemsEl.ValueKind != JsonValueKind.Array)
                            throw new RemoteAssetMalformedMetadataException(
                                "Query response 'items' must be a JSON array.");
                        foreach (var item in itemsEl.EnumerateArray())
                        {
                            items.Add(CharacterAssetMetadataParser.ParseElement(item));
                        }
                    }
                    // Spec is explicit: items is always present. Treat a
                    // missing items key the same as an empty array on
                    // forward-compat grounds (don't throw on a server
                    // that returns {"next_cursor": null} alone).

                    string? nextCursor = null;
                    if (root.TryGetProperty("next_cursor", out var ncEl)
                        && ncEl.ValueKind == JsonValueKind.String)
                    {
                        nextCursor = ncEl.GetString();
                    }
                    // JsonValueKind.Null falls through → nextCursor stays
                    // null, which is the contract for "last page".

                    return new CharacterAssetPage(items, nextCursor);
                }
            }
            catch (JsonException ex)
            {
                throw new RemoteAssetMalformedMetadataException(
                    "Query response is not valid JSON.", ex);
            }
        }

        /// <summary>
        /// Best-effort parse of a 422 body to extract the <c>error</c>
        /// discriminator and any per-field <c>errors</c> array. The wire
        /// shape is loosely specified — Pydantic v2 commonly emits
        /// <c>{"detail": [...]}</c>, the asset-backend spec calls for
        /// <c>{"error": "invalid_cursor", ...}</c>. We accept either and
        /// return <c>(null, [])</c> when the body is not parseable as
        /// JSON or doesn't carry the expected fields.
        /// </summary>
        private static (string? errorCode, IReadOnlyList<string> errors) ParseValidationBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return (null, Array.Empty<string>());

            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return (null, Array.Empty<string>());

                    string? code = null;
                    if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                        code = errEl.GetString();
                    else if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String)
                        code = codeEl.GetString();

                    var errors = new List<string>();
                    if (root.TryGetProperty("errors", out var errsEl) && errsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in errsEl.EnumerateArray())
                        {
                            if (e.ValueKind == JsonValueKind.String)
                            {
                                var s = e.GetString();
                                if (s != null) errors.Add(s);
                            }
                        }
                    }
                    return (code, errors);
                }
            }
            catch (JsonException)
            {
                return (null, Array.Empty<string>());
            }
        }

        private async Task AttachAuthAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string token = await _config.AuthTokenProvider(ct).ConfigureAwait(false) ?? string.Empty;
            if (token.Length > 0)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private static string? ExtractSingleHeader(HttpResponseMessage resp, string name)
        {
            // Header may live on the response or on the content; check both.
            if (resp.Headers.TryGetValues(name, out var fromResp))
            {
                foreach (var v in fromResp) return v;
            }
            if (resp.Content != null && resp.Content.Headers.TryGetValues(name, out var fromContent))
            {
                foreach (var v in fromContent) return v;
            }
            return null;
        }

        private static TimeSpan? ParseRetryAfter(HttpResponseMessage resp)
        {
            RetryConditionHeaderValue? rah = resp.Headers.RetryAfter;
            if (rah != null)
            {
                if (rah.Delta.HasValue) return rah.Delta.Value;
                if (rah.Date.HasValue)
                {
                    var delta = rah.Date.Value - DateTimeOffset.UtcNow;
                    return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
                }
            }
            // Raw header fallback (some servers send a bare integer that
            // doesn't parse into RetryConditionHeaderValue cleanly).
            if (resp.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var v in values)
                {
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
                        return TimeSpan.FromSeconds(seconds);
                }
            }
            return null;
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp)
        {
            if (resp.Content == null) return string.Empty;
            try
            {
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

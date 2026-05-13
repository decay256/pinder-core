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
    /// The remaining members (Save / Publish / Delete / ListIds) still
    /// throw <see cref="NotSupportedException"/> until sub-PR #855 wires
    /// them up.
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

        public Task SaveAsync(CharacterDefinition def, CancellationToken ct = default) =>
            throw new NotSupportedException(
                "SaveAsync is reserved for sub-PR #855 (write path). " +
                "On the remote store, save semantics map to PublishAsync.");

        public Task<bool> DeleteAsync(string characterId, CancellationToken ct = default) =>
            throw new NotSupportedException("DeleteAsync is reserved for sub-PR #855 (write path).");

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

        public Task<CharacterAssetMetadata> PublishAsync(
            CharacterDefinition def,
            CharacterAssetMetadata metadata,
            CancellationToken ct = default) =>
            throw new NotSupportedException("PublishAsync is reserved for sub-PR #855 (write path).");

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

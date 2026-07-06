using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Owns read-only interfaces: <see cref="LoadAsync"/>, <see cref="GetMetadataAsync"/>,
    /// <see cref="ExistsAsync"/>, <see cref="ListIdsAsync"/>, <see cref="QueryAsync"/>
    /// along with read-only JSON parsers and fetch response validators.
    /// </summary>
    public sealed class EigencoreCharacterStoreRead
    {
        private const string AssetMetadataHeader = "X-Asset-Metadata";

        private readonly Configuration _config;
        private readonly HttpClient _http;

        public EigencoreCharacterStoreRead(Configuration config, HttpClient http)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

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
        /// </summary>
        public async Task<CharacterAssetMetadata?> GetMetadataAsync(string characterId, CancellationToken ct = default)
        {
            ValidateId(characterId);

            (byte[] _, CharacterAssetMetadata meta, bool found) = await FetchAsync(characterId, ct).ConfigureAwait(false);
            return found ? meta : null;
        }

        /// <summary>
        /// <c>GET {baseUrl}/assets/{asset_id}</c>. Returns <c>true</c> on
        /// 200, <c>false</c> on 404.
        /// </summary>
        public async Task<bool> ExistsAsync(string characterId, CancellationToken ct = default)
        {
            ValidateId(characterId);

            (byte[] _, CharacterAssetMetadata __, bool found) = await FetchAsync(characterId, ct).ConfigureAwait(false);
            return found;
        }

        public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException(
                "ListIdsAsync is not part of the v1 eigencore wire contract. " +
                "Discovery happens via QueryAsync which returns metadata pages.");

        /// <summary>
        /// <c>GET {baseUrl}/assets?&lt;encoded filters&gt;</c>. Returns one
        /// page of metadata matching <paramref name="query"/>.
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

                        if (status == 429)
                        {
                            retried = await EigencoreResponseHandler.Handle429RetryAsync(
                                resp,
                                retried,
                                "Eigencore returned 429 for GET assets query after one retry.",
                                _config.DefaultRetryAfter,
                                ct).ConfigureAwait(false);
                            continue;
                        }

                        await EigencoreResponseHandler.HandleFailureResponseAsync(resp, "GET assets query", ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        resp.Dispose();
                    }
                }
            }
        }

        // ---- helper methods ------------------------------------------------

        internal static void ValidateId(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
        }

        internal static Uri BuildAssetUri(string characterId)
        {
            return new Uri($"assets/{Uri.EscapeDataString(characterId)}", UriKind.Relative);
        }

        private async Task AttachAuthAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string token = await _config.AuthTokenProvider(ct).ConfigureAwait(false) ?? string.Empty;
            if (token.Length > 0)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private async Task<(byte[] payload, CharacterAssetMetadata meta, bool found)> FetchAsync(
            string characterId,
            CancellationToken ct)
        {
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
                            await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return (Array.Empty<byte>(), null!, false);
                        }

                        if (status == 429)
                        {
                            retried = await EigencoreResponseHandler.Handle429RetryAsync(
                                resp,
                                retried,
                                $"Eigencore returned 429 for GET assets/{characterId} after one retry.",
                                _config.DefaultRetryAfter,
                                ct).ConfigureAwait(false);
                            continue;
                        }

                        await EigencoreResponseHandler.HandleFailureResponseAsync(resp, $"GET assets/{characterId}", ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        resp.Dispose();
                    }
                }
            }
        }

        private static Uri BuildQueryUri(CharacterAssetQuery query)
        {
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

            if (query.OwnerId != null)
                Append("owner_id", query.OwnerId);

            if (query.IsPublic.HasValue)
                Append("is_public", query.IsPublic.Value ? "true" : "false");

            if (query.Tags != null)
            {
                foreach (var t in query.Tags)
                {
                    if (!string.IsNullOrEmpty(t))
                        Append("tag", t);
                }
            }

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

        private static string FormatRfc3339Utc(DateTimeOffset ts)
        {
            return ts.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        }

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

                    string? nextCursor = null;
                    if (root.TryGetProperty("next_cursor", out var ncEl)
                        && ncEl.ValueKind == JsonValueKind.String)
                    {
                        nextCursor = ncEl.GetString();
                    }

                    return new CharacterAssetPage(items, nextCursor);
                }
            }
            catch (JsonException ex)
            {
                throw new RemoteAssetMalformedMetadataException(
                    "Query response is not valid JSON.", ex);
            }
        }

        internal static (string? errorCode, IReadOnlyList<string> errors) ParseValidationBody(string body)
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

        private static string? ExtractSingleHeader(HttpResponseMessage resp, string name)
        {
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

        internal static TimeSpan? ParseRetryAfter(HttpResponseMessage resp)
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

        internal static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp)
        {
            if (resp.Content == null) return string.Empty;
            try
            {
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false) ?? string.Empty;
            }
            catch (Exception ex) when (!(ex is HttpRequestException))
            {
                return string.Empty;
            }
        }
    }
}

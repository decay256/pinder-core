using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    /// Sub-PR scope (#853): read path only — <see cref="LoadAsync"/>,
    /// <see cref="GetMetadataAsync"/>, <see cref="ExistsAsync"/>. The
    /// other interface members throw <see cref="NotSupportedException"/>
    /// until sub-PRs #854 (query) and #855 (publish/save/delete) wire
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

        public Task<CharacterAssetPage> QueryAsync(CharacterAssetQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException("QueryAsync is reserved for sub-PR #854 (query path).");

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

using System;
using System.Collections.Generic;
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
    /// Encapsulates multipart form-data assembly, payload and metadata byte-size
    /// pre-validation, token attachment, and retry-on-429 rate-limiting loops
    /// for <see cref="PublishAsync"/>, <see cref="SaveAsync"/>, and <see cref="DeleteAsync"/>.
    /// </summary>
    public sealed class SyncHelper
    {
        private readonly Configuration _config;
        private readonly HttpClient _http;

        public SyncHelper(Configuration config, HttpClient http)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// <c>POST {baseUrl}/assets</c> as <c>multipart/form-data</c> with
        /// exactly two parts named <c>metadata</c> and <c>payload</c>.
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
                            string body401 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetAuthException(
                                "Eigencore returned 401 for POST assets.",
                                responseBody: body401);
                        }

                        if (status == 403)
                        {
                            string body403 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw BuildForbiddenException(body403, "POST assets");
                        }

                        if (status == 422)
                        {
                            string body422 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            (string? errorCode, IReadOnlyList<string> errors) = EigencoreCharacterStoreRead.ParseValidationBody(body422);
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
                            TimeSpan delay = EigencoreCharacterStoreRead.ParseRetryAfter(resp) ?? _config.DefaultRetryAfter;
                            string body429 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
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
                            string body5xx = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetServerException(
                                $"Eigencore returned {status} for POST assets.",
                                statusCode: status,
                                responseBody: body5xx);
                        }

                        string bodyOther = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
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
        /// Save = publish.
        /// </summary>
        public Task SaveAsync(CharacterDefinition def, CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            string assetId = def.CharacterId.ToString("d");
            var metadata = new CharacterAssetMetadata(
                characterId: assetId,
                ownerId: string.Empty,
                tags: Array.Empty<string>(),
                isPublic: false,
                createdAt: DateTimeOffset.MinValue,
                updatedAt: DateTimeOffset.MinValue,
                assetKind: CharacterAssetMetadata.AssetKindCharacterV1);
            return PublishAsync(def, metadata, ct);
        }

        /// <summary>
        /// <c>DELETE {baseUrl}/assets/{asset_id}</c>.
        /// </summary>
        public async Task<bool> DeleteAsync(string characterId, CancellationToken ct = default)
        {
            EigencoreCharacterStoreRead.ValidateId(characterId);

            bool retried = false;

            while (true)
            {
                using (var req = new HttpRequestMessage(HttpMethod.Delete, EigencoreCharacterStoreRead.BuildAssetUri(characterId)))
                {
                    await AttachAuthAsync(req, ct).ConfigureAwait(false);

                    HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    try
                    {
                        int status = (int)resp.StatusCode;

                        if (status == 200 || status == 204)
                        {
                            if (resp.Content != null)
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return true;
                        }

                        if (status == 404)
                        {
                            if (resp.Content != null)
                                await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            return false;
                        }

                        if (status == 401)
                        {
                            string body401 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetAuthException(
                                $"Eigencore returned 401 for DELETE assets/{characterId}.",
                                responseBody: body401);
                        }

                        if (status == 403)
                        {
                            string body403 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetForbiddenException(
                                $"Eigencore returned 403 for DELETE assets/{characterId} (caller is not the asset owner).",
                                responseBody: body403);
                        }

                        if (status == 422)
                        {
                            string body422 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            (_, IReadOnlyList<string> errors) = EigencoreCharacterStoreRead.ParseValidationBody(body422);
                            throw new RemoteAssetValidationException(
                                $"Eigencore returned 422 for DELETE assets/{characterId}.",
                                errors: errors,
                                responseBody: body422);
                        }

                        if (status == 429)
                        {
                            TimeSpan delay = EigencoreCharacterStoreRead.ParseRetryAfter(resp) ?? _config.DefaultRetryAfter;
                            string body429 = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
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
                            string body5xx = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
                            throw new RemoteAssetServerException(
                                $"Eigencore returned {status} for DELETE assets/{characterId}.",
                                statusCode: status,
                                responseBody: body5xx);
                        }

                        string bodyOther = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
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

        private byte[] SerializePayload(CharacterDefinition def)
        {
            if (_config.PayloadSerializer != null)
                return _config.PayloadSerializer(def) ?? Array.Empty<byte>();
            string json = CharacterDefinitionWriter.Write(def);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        }

        private static MultipartFormDataContent BuildPublishMultipart(byte[] metaBytes, byte[] payloadBytes)
        {
            var multipart = new MultipartFormDataContent();

            var metaPart = new ByteArrayContent(metaBytes);
            metaPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            multipart.Add(metaPart, "metadata");

            var payloadPart = new ByteArrayContent(payloadBytes);
            payloadPart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(payloadPart, "payload");

            return multipart;
        }

        private static CharacterAssetMetadata ParsePublishResponse(byte[] body)
        {
            if (body == null || body.Length == 0)
                throw new RemoteAssetMalformedMetadataException(
                    "Publish response body is empty.");
            return CharacterAssetMetadataParser.ParseBytes(body);
        }

        private static RemoteAssetForbiddenException BuildForbiddenException(string body403, string opLabel)
        {
            string? offendingPrefix = ExtractForbiddenPrefix(body403);
            string msg = offendingPrefix != null
                ? $"Eigencore returned 403 permission_denied for {opLabel}: reserved tag prefix '{offendingPrefix}' is not allowed for this caller."
                : $"Eigencore returned 403 permission_denied for {opLabel}.";
            return new RemoteAssetForbiddenException(msg, responseBody: body403);
        }

        private static string? ExtractForbiddenPrefix(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;

                    if (root.TryGetProperty("prefix", out var pEl)
                        && pEl.ValueKind == JsonValueKind.String)
                    {
                        var p = pEl.GetString();
                        if (!string.IsNullOrEmpty(p)) return p;
                    }

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
            }

            foreach (var candidate in new[] { "auto-", "official-" })
            {
                if (body.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            }
            return null;
        }

        private async Task AttachAuthAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string token = await _config.AuthTokenProvider(ct).ConfigureAwait(false) ?? string.Empty;
            if (token.Length > 0)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
    }
}

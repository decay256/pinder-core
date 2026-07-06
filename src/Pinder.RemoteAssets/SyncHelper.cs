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

                        if (status == 429)
                        {
                            retried = await EigencoreResponseHandler.Handle429RetryAsync(
                                resp,
                                retried,
                                "Eigencore returned 429 for POST assets after one retry.",
                                _config.DefaultRetryAfter,
                                ct).ConfigureAwait(false);
                            continue;
                        }

                        await EigencoreResponseHandler.HandleFailureResponseAsync(resp, "POST assets", ct).ConfigureAwait(false);
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

                        if (status == 429)
                        {
                            retried = await EigencoreResponseHandler.Handle429RetryAsync(
                                resp,
                                retried,
                                $"Eigencore returned 429 for DELETE assets/{characterId} after one retry.",
                                _config.DefaultRetryAfter,
                                ct).ConfigureAwait(false);
                            continue;
                        }

                        await EigencoreResponseHandler.HandleFailureResponseAsync(resp, $"DELETE assets/{characterId}", ct).ConfigureAwait(false);
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.RemoteAssets.Exceptions;

namespace Pinder.RemoteAssets
{
    public static class EigencoreResponseHandler
    {
        public static async Task HandleFailureResponseAsync(
            HttpResponseMessage resp,
            string operationContext,
            CancellationToken ct)
        {
            int status = (int)resp.StatusCode;
            string body = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);

            if (status == 401)
            {
                throw new RemoteAssetAuthException($"Eigencore returned 401 for {operationContext}.", responseBody: body);
            }
            if (status == 403)
            {
                throw BuildForbiddenException(body, operationContext);
            }
            if (status == 422)
            {
                (string? errorCode, var errors) = EigencoreCharacterStoreRead.ParseValidationBody(body);
                if (string.Equals(errorCode, "metadata_too_large", StringComparison.Ordinal) ||
                    string.Equals(errorCode, "payload_too_large", StringComparison.Ordinal))
                {
                    string subject = string.Equals(errorCode, "metadata_too_large", StringComparison.Ordinal) ? "metadata" : "payload";
                    throw new RemoteAssetTooLargeException(
                        $"Eigencore returned 422 {errorCode} for {operationContext}.",
                        subject: subject,
                        responseBody: body);
                }
                if (string.Equals(errorCode, "invalid_cursor", StringComparison.Ordinal))
                {
                    throw new RemoteAssetInvalidCursorException(
                        $"Eigencore returned 422 invalid_cursor for {operationContext}.",
                        responseBody: body);
                }

                string msg = operationContext.Contains("POST assets")
                    ? $"Eigencore returned 422 for {operationContext} (code={errorCode ?? "<none>"})."
                    : $"Eigencore returned 422 for {operationContext}.";

                throw new RemoteAssetValidationException(
                    msg,
                    errors: errors,
                    responseBody: body);
            }
            if (status >= 500 && status <= 599)
            {
                throw new RemoteAssetServerException(
                    $"Eigencore returned {status} for {operationContext}.",
                    statusCode: status,
                    responseBody: body);
            }

            throw new RemoteAssetServerException(
                $"Eigencore returned unexpected status {status} for {operationContext}.",
                statusCode: status,
                responseBody: body);
        }

        public static async Task<bool> Handle429RetryAsync(
            HttpResponseMessage resp,
            bool retried,
            string exceptionMessage,
            TimeSpan defaultRetryAfter,
            CancellationToken ct)
        {
            if ((int)resp.StatusCode != 429) return false;

            string body = await EigencoreCharacterStoreRead.SafeReadBodyAsync(resp).ConfigureAwait(false);
            TimeSpan delay = EigencoreCharacterStoreRead.ParseRetryAfter(resp) ?? defaultRetryAfter;

            if (retried)
            {
                throw new RemoteAssetRateLimitException(
                    exceptionMessage,
                    retryAfter: delay,
                    responseBody: body);
            }

            resp.Dispose();
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct).ConfigureAwait(false);

            return true;
        }

        private static RemoteAssetForbiddenException BuildForbiddenException(string body403, string operationContext)
        {
            if (operationContext.StartsWith("DELETE assets/"))
            {
                return new RemoteAssetForbiddenException(
                    $"Eigencore returned 403 for {operationContext} (caller is not the asset owner).",
                    responseBody: body403);
            }

            string? offendingPrefix = ExtractForbiddenPrefix(body403);
            string msg = offendingPrefix != null
                ? $"Eigencore returned 403 permission_denied for {operationContext}: reserved tag prefix '{offendingPrefix}' is not allowed for this caller."
                : $"Eigencore returned 403 permission_denied for {operationContext}.";
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
    }
}
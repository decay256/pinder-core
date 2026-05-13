using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Pinder.Core.Characters;
using Pinder.RemoteAssets.Exceptions;

namespace Pinder.RemoteAssets
{
    /// <summary>
    /// Translates the JSON shape inside <c>X-Asset-Metadata</c> (or a
    /// query response item) into the Pinder-side
    /// <see cref="CharacterAssetMetadata"/> POCO.
    ///
    /// The wire vocabulary is defined in
    /// <c>docs/specs/character-asset-vocabulary.md</c>:
    /// the wire spells <c>asset_id</c> / <c>asset_kind</c> /
    /// <c>owner_id</c> / <c>tags</c> / <c>is_public</c> /
    /// <c>created_at</c> / <c>updated_at</c> (plus the server-added
    /// read-only <c>payload_size</c>). The POCO renames
    /// <c>asset_id</c> → <c>CharacterId</c>; everything else is a
    /// straight projection.
    ///
    /// Unknown attributes are tolerated (forward-compat — see § Forward
    /// compatibility in the wire spec). Missing required attributes
    /// throw <see cref="RemoteAssetMalformedMetadataException"/>.
    /// </summary>
    internal static class CharacterAssetMetadataParser
    {
        /// <summary>
        /// Decode the verbatim header value (RFC 4648 standard padded
        /// base64) into the metadata bytes. NOT base64url.
        /// </summary>
        /// <exception cref="RemoteAssetMalformedMetadataException">
        /// The header was missing or not valid RFC 4648 base64.
        /// </exception>
        public static byte[] DecodeHeader(string? headerValue)
        {
            if (string.IsNullOrEmpty(headerValue))
                throw new RemoteAssetMalformedMetadataException(
                    "X-Asset-Metadata header missing on 200 OK response.");

            try
            {
                // RFC 4648 standard padded base64. Convert.FromBase64String
                // accepts only the +/= alphabet — base64url ( - and _ )
                // throws FormatException. That is exactly the wire
                // contract we want; the regression test in #853 pins it.
                return Convert.FromBase64String(headerValue);
            }
            catch (FormatException ex)
            {
                throw new RemoteAssetMalformedMetadataException(
                    "X-Asset-Metadata header is not valid RFC 4648 standard padded base64. " +
                    "Common cause: the server is encoding with base64url ('-'/'_') instead " +
                    "of standard base64 ('+'/'/'); see docs/specs/character-asset-vocabulary.md § Fetch.",
                    ex);
            }
        }

        /// <summary>
        /// Parse the JSON bytes inside a decoded <c>X-Asset-Metadata</c>
        /// (or a query response item) into the POCO.
        /// </summary>
        public static CharacterAssetMetadata ParseBytes(byte[] jsonBytes)
        {
            if (jsonBytes == null) throw new ArgumentNullException(nameof(jsonBytes));
            try
            {
                using (var doc = JsonDocument.Parse(jsonBytes))
                {
                    return ParseElement(doc.RootElement);
                }
            }
            catch (JsonException ex)
            {
                throw new RemoteAssetMalformedMetadataException(
                    "X-Asset-Metadata bytes are not valid JSON.", ex);
            }
        }

        public static CharacterAssetMetadata ParseElement(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                throw new RemoteAssetMalformedMetadataException(
                    "Asset metadata root must be a JSON object.");

            string assetKind = ReadOptionalString(root, "asset_kind") ?? CharacterAssetMetadata.AssetKindCharacterV1;
            string assetId = ReadRequiredString(root, "asset_id");
            string ownerId = ReadOptionalString(root, "owner_id") ?? string.Empty;
            bool isPublic = ReadOptionalBool(root, "is_public") ?? false;
            IReadOnlyList<string> tags = ReadOptionalStringArray(root, "tags") ?? Array.Empty<string>();
            DateTimeOffset createdAt = ReadRequiredTimestamp(root, "created_at");
            DateTimeOffset updatedAt = ReadRequiredTimestamp(root, "updated_at");

            // payload_size is intentionally ignored on read for v1 (the
            // POCO doesn't carry it; forward-compat rule). If a future
            // ticket exposes it, the property lands on CharacterAssetMetadata
            // and gets read here.

            return new CharacterAssetMetadata(
                characterId: assetId,
                ownerId: ownerId,
                tags: tags,
                isPublic: isPublic,
                createdAt: createdAt,
                updatedAt: updatedAt,
                assetKind: assetKind);
        }

        private static string ReadRequiredString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
                throw new RemoteAssetMalformedMetadataException(
                    $"Required asset-metadata field '{name}' missing or not a string.");
            return el.GetString() ?? throw new RemoteAssetMalformedMetadataException(
                $"Required asset-metadata field '{name}' is null.");
        }

        private static string? ReadOptionalString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Null) return null;
            if (el.ValueKind != JsonValueKind.String)
                throw new RemoteAssetMalformedMetadataException(
                    $"Asset-metadata field '{name}' is present but not a string.");
            return el.GetString();
        }

        private static bool? ReadOptionalBool(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el)) return null;
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            if (el.ValueKind == JsonValueKind.Null) return null;
            throw new RemoteAssetMalformedMetadataException(
                $"Asset-metadata field '{name}' is present but not a boolean.");
        }

        private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el)) return null;
            if (el.ValueKind == JsonValueKind.Null) return null;
            if (el.ValueKind != JsonValueKind.Array)
                throw new RemoteAssetMalformedMetadataException(
                    $"Asset-metadata field '{name}' is present but not an array.");
            var list = new List<string>(el.GetArrayLength());
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new RemoteAssetMalformedMetadataException(
                        $"Asset-metadata field '{name}' contains a non-string element.");
                var s = item.GetString();
                if (s != null) list.Add(s);
            }
            return list;
        }

        private static DateTimeOffset ReadRequiredTimestamp(JsonElement obj, string name)
        {
            string raw = ReadRequiredString(obj, name);
            // RFC3339 / ISO 8601 with UTC offset. Parse with
            // AssumeUniversal so a string like "...Z" or "...+00:00"
            // both land as UTC. The spec mandates UTC; we don't apply
            // any zone fix-ups.
            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var ts))
            {
                throw new RemoteAssetMalformedMetadataException(
                    $"Asset-metadata field '{name}' is not a valid RFC3339 timestamp: '{raw}'.");
            }
            return ts;
        }
    }
}

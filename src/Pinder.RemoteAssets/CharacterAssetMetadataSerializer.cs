using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Pinder.Core.Characters;

namespace Pinder.RemoteAssets
{
    /// <summary>
    /// Serialises a <see cref="CharacterAssetMetadata"/> POCO into the JSON
    /// shape that rides in the <c>metadata</c> part of a
    /// <c>POST /assets</c> multipart request.
    ///
    /// This is the WRITE direction of the boundary rename specified in
    /// <c>docs/specs/character-asset-vocabulary.md</c>: the POCO field
    /// <see cref="CharacterAssetMetadata.CharacterId"/> is emitted as the
    /// wire field <c>asset_id</c>. The serialiser MUST NEVER emit a JSON
    /// property literally named <c>character_id</c> — pinned by a
    /// regression test in <c>EigencoreCharacterStoreWriteTests</c>.
    ///
    /// Server-controlled fields (<c>owner_id</c>, <c>created_at</c>,
    /// <c>updated_at</c>, <c>payload_size</c>) are NOT emitted on the
    /// write path. Per the spec, the server stamps them itself; clients
    /// that supply them get them silently overridden, but we keep the
    /// wire small and the contract clear by simply not sending them.
    ///
    /// Inverse of <see cref="CharacterAssetMetadataParser.ParseElement"/>.
    /// </summary>
    internal static class CharacterAssetMetadataSerializer
    {
        /// <summary>
        /// Serialise metadata to UTF-8 JSON bytes for the
        /// <c>metadata</c> multipart part. Output is compact (no
        /// indentation) — wire size matters here because the part is
        /// capped at 4 KiB.
        /// </summary>
        public static byte[] SerializeBytes(CharacterAssetMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartObject();

                    // asset_kind — discriminator. Required by the spec.
                    w.WriteString("asset_kind", string.IsNullOrEmpty(metadata.AssetKind)
                        ? CharacterAssetMetadata.AssetKindCharacterV1
                        : metadata.AssetKind);

                    // POCO CharacterId → wire asset_id. THE rename. Never
                    // emit "character_id". Pinned by regression test.
                    w.WriteString("asset_id", metadata.CharacterId);

                    // is_public — client-supplied.
                    w.WriteBoolean("is_public", metadata.IsPublic);

                    // tags — client-supplied. Empty array is legal.
                    w.WriteStartArray("tags");
                    if (metadata.Tags != null)
                    {
                        foreach (var t in metadata.Tags)
                        {
                            if (t != null) w.WriteStringValue(t);
                        }
                    }
                    w.WriteEndArray();

                    // owner_id / created_at / updated_at / payload_size are
                    // server-controlled; do NOT serialise on write. The
                    // spec calls this out: "the server stamps those
                    // itself" and writes that supply them get them
                    // overwritten on publish.

                    w.WriteEndObject();
                    w.Flush();
                }
                return ms.ToArray();
            }
        }
    }
}

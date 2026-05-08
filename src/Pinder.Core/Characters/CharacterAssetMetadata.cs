using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Server-supplied metadata accompanying a remote character. Opaque to
    /// the engine: Pinder commits to the field names + types + semantics,
    /// the asset backend (e.g. Eigencore — gated #819) commits to indexing
    /// + querying + round-trip preservation.
    ///
    /// The full attribute vocabulary (legal tag prefixes, length limits,
    /// canonical timestamps) is specified in issue #818. This type is
    /// intentionally only the *shape* of the contract.
    /// </summary>
    public sealed class CharacterAssetMetadata
    {
        /// <summary>Asset kind discriminator. Today always "character/v1".</summary>
        public const string AssetKindCharacterV1 = "character/v1";

        /// <summary>UUIDv4 identity of the underlying <see cref="CharacterDefinition"/>.</summary>
        public string CharacterId { get; }

        /// <summary>
        /// Owner identity as understood by the asset backend. Format and
        /// semantics are the backend's contract; the engine treats it as an
        /// opaque string. Empty string is permitted for ownerless assets
        /// (e.g. official packs uploaded by a service identity).
        /// </summary>
        public string OwnerId { get; }

        /// <summary>
        /// Author-supplied tags. ALL tags must match in
        /// <see cref="CharacterAssetQuery.Tags"/> filters. The legal vocabulary
        /// (reserved prefixes, length / charset limits) is specified in
        /// issue #818.
        /// </summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Whether the asset is publicly discoverable.</summary>
        public bool IsPublic { get; }

        /// <summary>Server-assigned creation timestamp (UTC).</summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>Server-assigned last-modified timestamp (UTC).</summary>
        public DateTimeOffset UpdatedAt { get; }

        /// <summary>
        /// Asset kind discriminator, always
        /// <see cref="AssetKindCharacterV1"/> ("character/v1") for v1.
        /// Future kinds (item packs, anatomy packs) reuse the same metadata
        /// envelope with a different value.
        /// </summary>
        public string AssetKind { get; }

        public CharacterAssetMetadata(
            string characterId,
            string ownerId,
            IReadOnlyList<string> tags,
            bool isPublic,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            string assetKind = AssetKindCharacterV1)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            CharacterId = characterId;
            OwnerId = ownerId ?? string.Empty;
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            IsPublic = isPublic;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            AssetKind = string.IsNullOrWhiteSpace(assetKind) ? AssetKindCharacterV1 : assetKind;
        }
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Network-backed character store. Extends <see cref="ICharacterStore"/>
    /// with server-side discovery (<see cref="QueryAsync"/>), per-asset
    /// metadata access, and a publish flow that stamps server-controlled
    /// fields (timestamps, owner identity).
    ///
    /// Implementations live outside this repo (the first one is gated under
    /// issue #819). This interface intentionally has no HTTP / auth /
    /// network types — those are the implementation's concern.
    ///
    /// Drop-in compatibility: a caller that already takes an
    /// <see cref="ICharacterStore"/> can be handed an
    /// <see cref="IRemoteCharacterStore"/> and continues to work; the
    /// remote-specific methods are additive.
    /// </summary>
    public interface IRemoteCharacterStore : ICharacterStore
    {
        /// <summary>
        /// Returns one page of metadata matching the given query. Metadata
        /// only — the caller follows up with
        /// <see cref="ICharacterStore.LoadAsync"/> for each
        /// <see cref="CharacterAssetMetadata.CharacterId"/> it actually wants
        /// to instantiate.
        /// </summary>
        Task<CharacterAssetPage> QueryAsync(CharacterAssetQuery query, CancellationToken ct = default);

        /// <summary>
        /// Returns the metadata for a single character, or <c>null</c> when
        /// no character with that id is in the store (or is not visible to
        /// the calling identity).
        /// </summary>
        Task<CharacterAssetMetadata?> GetMetadataAsync(string characterId, CancellationToken ct = default);

        /// <summary>
        /// Publishes a character to the remote store. The server may
        /// overwrite <see cref="CharacterAssetMetadata.CharacterId"/>,
        /// <see cref="CharacterAssetMetadata.OwnerId"/>,
        /// <see cref="CharacterAssetMetadata.CreatedAt"/>,
        /// <see cref="CharacterAssetMetadata.UpdatedAt"/> on the returned
        /// metadata; client-supplied <see cref="CharacterAssetMetadata.Tags"/>
        /// and <see cref="CharacterAssetMetadata.IsPublic"/> flow through
        /// unchanged on a successful publish.
        /// </summary>
        /// <returns>The server's view of the published asset's metadata.</returns>
        Task<CharacterAssetMetadata> PublishAsync(
            CharacterDefinition def,
            CharacterAssetMetadata metadata,
            CancellationToken ct = default);
    }
}

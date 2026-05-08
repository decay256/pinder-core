using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Single page of metadata returned by
    /// <see cref="IRemoteCharacterStore.QueryAsync"/>. Metadata only — the
    /// caller fetches each character's payload via
    /// <see cref="ICharacterStore.LoadAsync"/> on demand.
    /// </summary>
    public sealed class CharacterAssetPage
    {
        /// <summary>Items in this page, in server-defined order.</summary>
        public IReadOnlyList<CharacterAssetMetadata> Items { get; }

        /// <summary>
        /// Pagination cursor for the next page, or <c>null</c> when this is
        /// the last page. Pass back unchanged in
        /// <see cref="CharacterAssetQuery.Cursor"/>.
        /// </summary>
        public string? NextCursor { get; }

        public CharacterAssetPage(IReadOnlyList<CharacterAssetMetadata> items, string? nextCursor)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            NextCursor = nextCursor;
        }
    }
}

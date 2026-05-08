using System;
using System.Collections.Generic;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Filter / pagination input for <see cref="IRemoteCharacterStore.QueryAsync"/>.
    /// All filter properties are AND'd together. Within
    /// <see cref="Tags"/>, every entry must match (intersection semantics, not
    /// union).
    ///
    /// Defaults: no filters (returns the most recently updated public assets
    /// the caller has access to), <see cref="Limit"/> = 50.
    /// </summary>
    public sealed class CharacterAssetQuery
    {
        /// <summary>Maximum legal value of <see cref="Limit"/>.</summary>
        public const int MaxLimit = 500;

        /// <summary>Default value of <see cref="Limit"/> if the caller omits it.</summary>
        public const int DefaultLimit = 50;

        /// <summary>
        /// Filter to assets owned by this owner identity. <c>null</c> means
        /// "any owner". Empty string is reserved for the explicit
        /// "ownerless / service-owned" filter.
        /// </summary>
        public string? OwnerId { get; }

        /// <summary>
        /// Required tags. Every tag in this list must appear on the asset's
        /// <see cref="CharacterAssetMetadata.Tags"/>. <c>null</c> or empty
        /// means "no tag filter".
        /// </summary>
        public IReadOnlyList<string>? Tags { get; }

        /// <summary>
        /// Filter to public-only or private-only assets. <c>null</c> means
        /// "no visibility filter" (server may further restrict by auth).
        /// </summary>
        public bool? IsPublic { get; }

        /// <summary>
        /// Maximum number of results to return in this page. 1..<see cref="MaxLimit"/>;
        /// values outside the range are clamped at construction time.
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// Opaque pagination cursor returned by a previous
        /// <see cref="CharacterAssetPage.NextCursor"/>. <c>null</c> requests
        /// the first page.
        /// </summary>
        public string? Cursor { get; }

        public CharacterAssetQuery(
            string? ownerId = null,
            IReadOnlyList<string>? tags = null,
            bool? isPublic = null,
            int limit = DefaultLimit,
            string? cursor = null)
        {
            OwnerId = ownerId;
            Tags = tags;
            IsPublic = isPublic;
            Limit = ClampLimit(limit);
            Cursor = cursor;
        }

        private static int ClampLimit(int requested)
        {
            if (requested < 1) return 1;
            if (requested > MaxLimit) return MaxLimit;
            return requested;
        }
    }
}

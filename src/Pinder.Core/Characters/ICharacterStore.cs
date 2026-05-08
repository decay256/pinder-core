using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Abstraction over a *collection* of <see cref="CharacterDefinition"/>s.
    /// Local directories, future remote asset stores (issue #817), and the
    /// Unity-side ScriptableObject store all sit behind this seam.
    ///
    /// Identity is the v1 <c>character_id</c> (a UUIDv4 string). Stores
    /// MUST resolve ids by reading each backing artifact's <c>character_id</c>
    /// field, not by parsing filenames or paths — filenames are presentation
    /// (see <c>docs/specs/character-file-format.md</c>).
    ///
    /// All methods are async with <see cref="CancellationToken"/> so blocking
    /// I/O is never a hidden assumption. netstandard2.0 implementations that
    /// have nothing to await should still return completed Tasks.
    /// </summary>
    public interface ICharacterStore
    {
        /// <summary>
        /// Returns every <c>character_id</c> known to this store. Order is
        /// implementation-defined; callers MUST NOT depend on it.
        /// </summary>
        Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default);

        /// <summary>
        /// Loads the character with the given id. Returns <c>null</c> when no
        /// character with that id is in the store.
        /// </summary>
        /// <exception cref="FormatException">
        /// The backing artifact exists but is malformed.
        /// </exception>
        Task<CharacterDefinition?> LoadAsync(string characterId, CancellationToken ct = default);

        /// <summary>
        /// Saves the given definition. Overwrites if a character with the
        /// same <see cref="CharacterDefinition.CharacterId"/> already exists.
        /// </summary>
        Task SaveAsync(CharacterDefinition def, CancellationToken ct = default);

        /// <summary>
        /// Deletes the character with the given id. Returns <c>true</c> if a
        /// character was deleted, <c>false</c> if no character with that id
        /// existed.
        /// </summary>
        Task<bool> DeleteAsync(string characterId, CancellationToken ct = default);

        /// <summary>
        /// Returns whether a character with the given id is in the store.
        /// </summary>
        Task<bool> ExistsAsync(string characterId, CancellationToken ct = default);
    }
}

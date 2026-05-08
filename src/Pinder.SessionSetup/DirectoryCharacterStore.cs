using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.SessionSetup
{
    /// <summary>
    /// File-backed <see cref="ICharacterStore"/> rooted at a single directory.
    /// Each character occupies one <c>*.json</c> file. The mapping between
    /// <c>character_id</c> (UUIDv4 in the file) and file path is built lazily
    /// on first use and invalidated on every <see cref="SaveAsync"/> /
    /// <see cref="DeleteAsync"/>.
    ///
    /// Filename slug is presentation: it derives from
    /// <see cref="CharacterDefinition.Name"/> and resolves collisions by
    /// appending a short character_id suffix.
    ///
    /// Concurrency contract: all mutating operations are serialised through
    /// a per-instance lock. Reads see a consistent index but two stores
    /// pointed at the same directory do NOT coordinate beyond what the
    /// filesystem itself provides (no fcntl locks). Single-process, possibly
    /// multi-task usage is safe; cross-process is not.
    /// </summary>
    public sealed class DirectoryCharacterStore : ICharacterStore
    {
        private readonly string _directory;
        private readonly object _gate = new object();

        // Lazy index: character_id -> absolute file path. null until built;
        // swapped wholesale on rebuild. Mutating operations rebuild the
        // index by partial update under _gate to avoid a directory rescan
        // on every change.
        private Dictionary<string, string>? _idIndex;

        public DirectoryCharacterStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path must be non-empty.", nameof(directory));
            _directory = Path.GetFullPath(directory);
        }

        /// <summary>The absolute directory path this store is rooted at.</summary>
        public string Directory => _directory;

        public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var index = EnsureIndex();
            IReadOnlyList<string> ids = index.Keys.ToList();
            return Task.FromResult(ids);
        }

        public Task<CharacterDefinition?> LoadAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();

            var index = EnsureIndex();
            if (!index.TryGetValue(characterId, out string? path))
                return Task.FromResult<CharacterDefinition?>(null);

            // The file may have been deleted out from under us between
            // index build and now. Treat that as "not found" rather than
            // an exception, but invalidate the index so the next call
            // reflects reality.
            if (!File.Exists(path))
            {
                lock (_gate) { _idIndex = null; }
                return Task.FromResult<CharacterDefinition?>(null);
            }

            string json = File.ReadAllText(path);
            var def = CharacterDefinitionLoader.ParseDefinition(json);
            return Task.FromResult<CharacterDefinition?>(def);
        }

        public Task SaveAsync(CharacterDefinition def, CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            ct.ThrowIfCancellationRequested();

            string id = def.CharacterId.ToString("D");
            string content = CharacterDefinitionWriter.Write(def);

            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_directory);

                var index = EnsureIndexLocked();
                if (index.TryGetValue(id, out string? existingPath))
                {
                    // Overwrite in place — preserves any human-curated
                    // filename slug.
                    File.WriteAllText(existingPath, content, new UTF8Encoding(false));
                    return Task.CompletedTask;
                }

                string filename = ChooseFilename(def, index);
                string fullPath = Path.Combine(_directory, filename);
                File.WriteAllText(fullPath, content, new UTF8Encoding(false));
                index[id] = fullPath;
            }

            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var index = EnsureIndexLocked();
                if (!index.TryGetValue(characterId, out string? path))
                    return Task.FromResult(false);

                if (File.Exists(path))
                    File.Delete(path);

                index.Remove(characterId);
                return Task.FromResult(true);
            }
        }

        public Task<bool> ExistsAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();

            var index = EnsureIndex();
            return Task.FromResult(index.ContainsKey(characterId));
        }

        // --- index management ------------------------------------------------

        private IReadOnlyDictionary<string, string> EnsureIndex()
        {
            lock (_gate)
            {
                return EnsureIndexLocked();
            }
        }

        private Dictionary<string, string> EnsureIndexLocked()
        {
            if (_idIndex != null) return _idIndex;

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (System.IO.Directory.Exists(_directory))
            {
                foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
                {
                    // Skip files we know are not character files (the schema
                    // file lives next to the characters in `data/characters/`).
                    string fileName = Path.GetFileName(path);
                    if (fileName.Equals("character-schema.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? id = TryReadCharacterId(path);
                    if (id == null) continue;

                    // Last-write-wins on collision. Two files claiming the
                    // same character_id is a user error; we don't try to
                    // pick a winner intelligently.
                    index[id] = path;
                }
            }
            _idIndex = index;
            return index;
        }

        private static string? TryReadCharacterId(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!doc.RootElement.TryGetProperty("character_id", out var idProp)) return null;
                if (idProp.ValueKind != JsonValueKind.String) return null;
                string raw = idProp.GetString()!;
                return Guid.TryParseExact(raw, "D", out _) ? raw : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // --- filename selection ---------------------------------------------

        private static string ChooseFilename(
            CharacterDefinition def,
            IReadOnlyDictionary<string, string> index)
        {
            string baseSlug = Slugify(def.Name);
            if (string.IsNullOrEmpty(baseSlug))
                baseSlug = "character";

            string preferred = baseSlug + ".json";
            if (!IndexContainsFile(index, preferred))
                return preferred;

            // Slug already taken by another character; append a short
            // disambiguator from the new character's id.
            string shortId = def.CharacterId.ToString("N").Substring(0, 8);
            return $"{baseSlug}-{shortId}.json";
        }

        private static bool IndexContainsFile(IReadOnlyDictionary<string, string> index, string filename)
        {
            foreach (var path in index.Values)
            {
                if (Path.GetFileName(path).Equals(filename, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Lowercase, ASCII letters/digits, dashes only. No path separators,
        /// no leading/trailing dashes. Empty input maps to "character".
        /// </summary>
        public static string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var sb = new StringBuilder(name.Length);
            bool lastWasDash = false;
            foreach (char c in name)
            {
                char lower = char.ToLowerInvariant(c);
                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
                {
                    sb.Append(lower);
                    lastWasDash = false;
                }
                else if (sb.Length > 0 && !lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }

            // Trim trailing dash.
            while (sb.Length > 0 && sb[sb.Length - 1] == '-')
                sb.Length--;

            return sb.ToString();
        }
    }
}

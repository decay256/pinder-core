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
    /// a per-instance <see cref="SemaphoreSlim"/>. Reads see a consistent
    /// index but two stores pointed at the same directory do NOT coordinate
    /// beyond what the filesystem itself provides (no fcntl locks).
    /// Single-process, possibly multi-task usage is safe; cross-process is
    /// not.
    ///
    /// I/O contract: every method here performs genuine asynchronous disk
    /// I/O (async file streams, async JSON parsing) rather than synchronous
    /// calls wrapped in <c>Task.FromResult</c>. Callers that <c>await</c>
    /// these methods are not blocked on disk work for the duration of the
    /// call. <see cref="System.IO.Directory.EnumerateFiles(string, string)"/>
    /// itself has no async counterpart in .NET and remains a synchronous
    /// (cheap, metadata-only) directory listing; the per-file content reads
    /// it drives are fully async.
    /// </summary>
    public sealed class DirectoryCharacterStore : ICharacterStore
    {
        private const int DefaultBufferSize = 4096;

        private readonly string _directory;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        // Lazy index: character_id -> absolute file path. null until built;
        // swapped wholesale on rebuild. Mutating operations rebuild the
        // index by partial update under _gate to avoid a directory rescan
        // on every change.
        private Dictionary<string, string>? _idIndex;

        /// <summary>
        /// Test-only seam (exercised via
        /// <c>InternalsVisibleTo("Pinder.Core.Tests")</c>): when set, every
        /// disk read/write awaits this delegate first. Lets tests prove
        /// these methods are genuinely asynchronous — i.e. do not block the
        /// calling thread and do not complete synchronously — under an
        /// injected slow I/O operation, without standing up a full
        /// swappable file-system abstraction. Must always be reset to
        /// <c>null</c> by the test that sets it.
        /// </summary>
        internal static Func<CancellationToken, Task>? TestIoDelayHook;

        public DirectoryCharacterStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path must be non-empty.", nameof(directory));
            _directory = Path.GetFullPath(directory);
        }

        /// <summary>The absolute directory path this store is rooted at.</summary>
        public string Directory => _directory;

        public async Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var index = await EnsureIndexAsync(ct).ConfigureAwait(false);
            IReadOnlyList<string> ids = index.Keys.ToList();
            return ids;
        }

        public async Task<CharacterDefinition?> LoadAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();

            var index = await EnsureIndexAsync(ct).ConfigureAwait(false);
            if (!index.TryGetValue(characterId, out string? path))
                return null;

            // The file may have been deleted out from under us between
            // index build and now. Treat that as "not found" rather than
            // an exception, but invalidate the index so the next call
            // reflects reality.
            if (!File.Exists(path))
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                try { _idIndex = null; }
                finally { _gate.Release(); }
                return null;
            }

            string json = await ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return CharacterDefinitionLoader.ParseDefinition(json);
        }

        public async Task SaveAsync(CharacterDefinition def, CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            ct.ThrowIfCancellationRequested();

            string id = def.CharacterId.ToString("D");
            string content = CharacterDefinitionWriter.Write(def);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                System.IO.Directory.CreateDirectory(_directory);

                var index = await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
                if (index.TryGetValue(id, out string? existingPath))
                {
                    // Overwrite in place — preserves any human-curated
                    // filename slug.
                    await WriteAllTextAsync(existingPath, content, ct).ConfigureAwait(false);
                    return;
                }

                string filename = ChooseFilename(def, index);
                string fullPath = Path.Combine(_directory, filename);
                await WriteAllTextAsync(fullPath, content, ct).ConfigureAwait(false);
                index[id] = fullPath;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> DeleteAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var index = await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
                if (!index.TryGetValue(characterId, out string? path))
                    return false;

                if (File.Exists(path))
                    File.Delete(path);

                index.Remove(characterId);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> ExistsAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();

            var index = await EnsureIndexAsync(ct).ConfigureAwait(false);
            return index.ContainsKey(characterId);
        }

        // --- index management ------------------------------------------------

        private async Task<Dictionary<string, string>> EnsureIndexAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Rebuilds (if necessary) and returns the id index. Callers must
        /// already hold <see cref="_gate"/>. The directory listing itself
        /// is a cheap synchronous syscall with no async counterpart; the
        /// per-file reads it drives (<see cref="TryReadCharacterIdAsync"/>)
        /// are genuinely asynchronous.
        /// </summary>
        private async Task<Dictionary<string, string>> EnsureIndexLockedAsync(CancellationToken ct)
        {
            if (_idIndex != null) return _idIndex;

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (System.IO.Directory.Exists(_directory))
            {
                foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip files we know are not character files (the schema
                    // file lives next to the characters in `data/characters/`).
                    string fileName = Path.GetFileName(path);
                    if (fileName.Equals("character-schema.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? id = await TryReadCharacterIdAsync(path, ct).ConfigureAwait(false);
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

        private static async Task<string?> TryReadCharacterIdAsync(string path, CancellationToken ct)
        {
            try
            {
                await MaybeDelayForTestAsync(ct).ConfigureAwait(false);

                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize, useAsync: true);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
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

        // --- raw file I/O (genuinely async) -----------------------------------

        private static async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
        {
            await MaybeDelayForTestAsync(ct).ConfigureAwait(false);

            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize, useAsync: true);
            // Matches File.ReadAllText's behaviour: auto-detect BOM, default to UTF-8.
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = await reader.ReadToEndAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return text;
        }

        private static async Task WriteAllTextAsync(string path, string content, CancellationToken ct)
        {
            await MaybeDelayForTestAsync(ct).ConfigureAwait(false);

            using var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize, useAsync: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(content).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }

        private static Task MaybeDelayForTestAsync(CancellationToken ct)
        {
            var hook = TestIoDelayHook;
            return hook != null ? hook(ct) : Task.CompletedTask;
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

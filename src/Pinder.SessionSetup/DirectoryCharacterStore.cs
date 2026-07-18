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
        /// per-file reads it drives (<see cref="ReadCharacterIdForIndexAsync"/>)
        /// are genuinely asynchronous.
        /// </summary>
        private async Task<Dictionary<string, string>> EnsureIndexLockedAsync(CancellationToken ct)
        {
            if (_idIndex != null) return _idIndex;

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<CharacterIndexValidationError>();
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

                    CharacterIndexIdRead idRead =
                        await ReadCharacterIdForIndexAsync(path, ct).ConfigureAwait(false);
                    if (idRead.Error != null)
                    {
                        errors.Add(idRead.Error);
                        continue;
                    }

                    string id = idRead.CharacterId!;
                    if (index.TryGetValue(id, out string? existingPath))
                    {
                        errors.Add(CharacterIndexValidationError.Duplicate(id, existingPath, path));
                        continue;
                    }

                    index.Add(id, path);
                }
            }

            if (errors.Count > 0)
                throw CreateIndexValidationException(errors);

            _idIndex = index;
            return index;
        }

        private InvalidOperationException CreateIndexValidationException(
            IReadOnlyList<CharacterIndexValidationError> errors)
        {
            var message = new StringBuilder();
            message.Append("DirectoryCharacterStore could not build a valid character index for '");
            message.Append(_directory);
            message.Append("'. ");
            message.Append(errors.Count);
            message.Append(errors.Count == 1 ? " error was found: " : " errors were found: ");
            message.Append(string.Join("; ", errors.Select(e => e.Message)));

            var innerExceptions = errors
                .Where(e => e.Exception != null)
                .Select(e => e.Exception!)
                .ToList();
            Exception? inner = innerExceptions.Count == 0
                ? null
                : new AggregateException(innerExceptions);

            return new InvalidOperationException(message.ToString(), inner);
        }

        private static async Task<CharacterIndexIdRead> ReadCharacterIdForIndexAsync(
            string path,
            CancellationToken ct)
        {
            try
            {
                await MaybeDelayForTestAsync(ct).ConfigureAwait(false);

                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize, useAsync: true);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return CharacterIndexIdRead.Failed(
                        CharacterIndexValidationError.Malformed(path, "root JSON value must be an object."));
                }

                if (!doc.RootElement.TryGetProperty("character_id", out var idProp))
                {
                    return CharacterIndexIdRead.Failed(
                        CharacterIndexValidationError.Malformed(path, "missing required property 'character_id'."));
                }

                if (idProp.ValueKind != JsonValueKind.String)
                {
                    return CharacterIndexIdRead.Failed(
                        CharacterIndexValidationError.Malformed(path, "'character_id' must be a string."));
                }

                string raw = idProp.GetString()!;
                if (!Guid.TryParseExact(raw, "D", out _))
                {
                    return CharacterIndexIdRead.Failed(
                        CharacterIndexValidationError.Malformed(
                            path,
                            "'character_id' must be a UUID in canonical D format."));
                }

                return CharacterIndexIdRead.Success(raw);
            }
            catch (IOException ex)
            {
                return CharacterIndexIdRead.Failed(CharacterIndexValidationError.Unreadable(path, ex));
            }
            catch (UnauthorizedAccessException ex)
            {
                return CharacterIndexIdRead.Failed(CharacterIndexValidationError.AccessDenied(path, ex));
            }
            catch (JsonException ex)
            {
                return CharacterIndexIdRead.Failed(CharacterIndexValidationError.Malformed(
                    path,
                    "malformed JSON.",
                    ex));
            }
        }

        private sealed class CharacterIndexIdRead
        {
            private CharacterIndexIdRead(string? characterId, CharacterIndexValidationError? error)
            {
                CharacterId = characterId;
                Error = error;
            }

            public string? CharacterId { get; }
            public CharacterIndexValidationError? Error { get; }

            public static CharacterIndexIdRead Success(string characterId)
            {
                return new CharacterIndexIdRead(characterId, null);
            }

            public static CharacterIndexIdRead Failed(CharacterIndexValidationError error)
            {
                return new CharacterIndexIdRead(null, error);
            }
        }

        private sealed class CharacterIndexValidationError
        {
            private CharacterIndexValidationError(string message, Exception? exception = null)
            {
                Message = message;
                Exception = exception;
            }

            public string Message { get; }
            public Exception? Exception { get; }

            public static CharacterIndexValidationError Malformed(
                string path,
                string reason,
                Exception? exception = null)
            {
                return new CharacterIndexValidationError(
                    $"Character file '{path}' is invalid and must be fixed: {reason}",
                    exception);
            }

            public static CharacterIndexValidationError Unreadable(string path, IOException exception)
            {
                return new CharacterIndexValidationError(
                    $"Character file '{path}' could not be read because of an I/O error; " +
                    "retry after the filesystem issue is corrected.",
                    exception);
            }

            public static CharacterIndexValidationError AccessDenied(
                string path,
                UnauthorizedAccessException exception)
            {
                return new CharacterIndexValidationError(
                    $"Character file '{path}' could not be read because access was denied; " +
                    "fix file permissions before rebuilding the index.",
                    exception);
            }

            public static CharacterIndexValidationError Duplicate(
                string characterId,
                string firstPath,
                string duplicatePath)
            {
                return new CharacterIndexValidationError(
                    $"Duplicate character_id '{characterId}' appears in both '{firstPath}' " +
                    $"and '{duplicatePath}'.");
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

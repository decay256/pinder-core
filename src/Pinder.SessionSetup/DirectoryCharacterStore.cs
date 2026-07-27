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
    /// Concurrency contract: each instance serialises its operations through
    /// a <see cref="SemaphoreSlim"/>. Mutations additionally acquire an
    /// exclusive lock file shared by every store rooted at this directory,
    /// then replace artifacts atomically. Multiple processes using this store
    /// therefore coordinate compare-and-swap, save, and delete operations.
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
    public sealed class DirectoryCharacterStore :
        ICharacterStore,
        IAtomicCharacterArtifactStore,
        ICharacterStoreDiagnosticEnumerator
    {
        private const int DefaultBufferSize = 4096;
        private const string StoreLockFileName = ".pinder-character-store.lock";

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
            CharacterStoreEnumeration enumeration =
                await EnumerateArtifactsAsync(ct).ConfigureAwait(false);
            return enumeration.CharacterIds;
        }

        public async Task<CharacterStoreEnumeration> EnumerateArtifactsAsync(
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                IndexScan scan = await ScanIndexLockedAsync(ct).ConfigureAwait(false);
                _idIndex = scan.Index;
                return new CharacterStoreEnumeration(
                    scan.Index.Keys.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                    scan.Diagnostics);
            }
            finally
            {
                _gate.Release();
            }
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

        public async Task<CharacterArtifact?> ReadArtifactAsync(
            string characterId,
            CancellationToken ct = default)
        {
            ValidateCharacterIdArgument(characterId);
            ct.ThrowIfCancellationRequested();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _idIndex = null;
                var index = await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
                if (!index.TryGetValue(characterId, out string? path) || !File.Exists(path))
                    return null;

                byte[] content = await ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                return new CharacterArtifact(characterId, content);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<CharacterArtifact> CompareExchangeArtifactAsync(
            string characterId,
            string expectedRevision,
            byte[] replacementContent,
            CancellationToken ct = default)
        {
            ValidateCharacterIdArgument(characterId);
            if (string.IsNullOrWhiteSpace(expectedRevision))
                throw new ArgumentException("Expected revision must not be blank.", nameof(expectedRevision));
            if (replacementContent == null)
                throw new ArgumentNullException(nameof(replacementContent));
            if (replacementContent.Length == 0)
                throw new ArgumentException("Replacement content must not be empty.", nameof(replacementContent));
            ValidateReplacementIdentity(characterId, replacementContent);
            ct.ThrowIfCancellationRequested();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                using (await AcquireStoreLockAsync(ct).ConfigureAwait(false))
                {
                    _idIndex = null;
                    var index = await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
                    if (!index.TryGetValue(characterId, out string? path) || !File.Exists(path))
                        throw new KeyNotFoundException("Character artifact was not found.");

                    byte[] activeContent = await ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                    string activeRevision = CharacterArtifactRevisions.Compute(activeContent);
                    if (!string.Equals(
                            activeRevision,
                            expectedRevision,
                            StringComparison.Ordinal))
                    {
                        throw new CharacterArtifactRevisionConflictException(
                            expectedRevision,
                            activeRevision);
                    }

                    await AtomicWriteAsync(
                        path,
                        replacementContent,
                        replaceExisting: true,
                        ct).ConfigureAwait(false);
                    return new CharacterArtifact(characterId, replacementContent);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(CharacterDefinition def, CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            ct.ThrowIfCancellationRequested();

            string id = def.CharacterId.ToString("D");
            byte[] content = Encoding.UTF8.GetBytes(CharacterDefinitionWriter.Write(def));

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                using (await AcquireStoreLockAsync(ct).ConfigureAwait(false))
                {
                    _idIndex = null;

                var index = await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
                if (index.TryGetValue(id, out string? existingPath))
                {
                    // Overwrite in place — preserves any human-curated
                    // filename slug.
                    await AtomicWriteAsync(existingPath, content, true, ct).ConfigureAwait(false);
                    return;
                }

                string filename = ChooseFilename(def, index);
                string fullPath = Path.Combine(_directory, filename);
                await AtomicWriteAsync(fullPath, content, false, ct).ConfigureAwait(false);
                index[id] = fullPath;
                }
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
                System.IO.Directory.CreateDirectory(_directory);
                using (await AcquireStoreLockAsync(ct).ConfigureAwait(false))
                {
                    _idIndex = null;
                var index = await EnsureIndexLockedAsync(ct).ConfigureAwait(false);
                if (!index.TryGetValue(characterId, out string? path))
                    return false;

                if (File.Exists(path))
                    File.Delete(path);

                index.Remove(characterId);
                return true;
                }
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
            IndexScan scan = await ScanIndexLockedAsync(ct).ConfigureAwait(false);
            _idIndex = scan.Index;
            return _idIndex;
        }

        private async Task<IndexScan> ScanIndexLockedAsync(CancellationToken ct)
        {
            var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var diagnostics = new List<CharacterStoreDiagnostic>();
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
                        diagnostics.Add(idRead.Error.ToDiagnostic());
                        continue;
                    }

                    string id = idRead.CharacterId!;
                    if (duplicateIds.Contains(id))
                    {
                        diagnostics.Add(CharacterIndexValidationError
                            .Duplicate(id, path)
                            .ToDiagnostic());
                        continue;
                    }

                    if (candidates.TryGetValue(id, out string? existingPath))
                    {
                        candidates.Remove(id);
                        duplicateIds.Add(id);
                        diagnostics.Add(CharacterIndexValidationError
                            .Duplicate(id, existingPath)
                            .ToDiagnostic());
                        diagnostics.Add(CharacterIndexValidationError
                            .Duplicate(id, path)
                            .ToDiagnostic());
                        continue;
                    }

                    candidates.Add(id, path);
                }
            }

            return new IndexScan(candidates, diagnostics);
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
            private CharacterIndexValidationError(
                string path,
                string code,
                string message,
                string? characterId = null)
            {
                Path = path;
                Code = code;
                Message = message;
                CharacterId = characterId;
            }

            public string Path { get; }
            public string Code { get; }
            public string Message { get; }
            public string? CharacterId { get; }

            public CharacterStoreDiagnostic ToDiagnostic()
                => new CharacterStoreDiagnostic(
                    System.IO.Path.GetFileName(Path),
                    Code,
                    Message,
                    CharacterId);

            public static CharacterIndexValidationError Malformed(
                string path,
                string reason,
                Exception? exception = null)
            {
                return new CharacterIndexValidationError(
                    path,
                    CharacterStoreDiagnosticCodes.MalformedArtifact,
                    $"Character artifact is invalid and must be fixed: {reason}");
            }

            public static CharacterIndexValidationError Unreadable(string path, IOException exception)
            {
                return new CharacterIndexValidationError(
                    path,
                    CharacterStoreDiagnosticCodes.UnreadableArtifact,
                    "Character artifact could not be read because of an I/O error.");
            }

            public static CharacterIndexValidationError AccessDenied(
                string path,
                UnauthorizedAccessException exception)
            {
                return new CharacterIndexValidationError(
                    path,
                    CharacterStoreDiagnosticCodes.AccessDenied,
                    "Character artifact could not be read because access was denied.");
            }

            public static CharacterIndexValidationError Duplicate(
                string characterId,
                string path)
            {
                return new CharacterIndexValidationError(
                    path,
                    CharacterStoreDiagnosticCodes.DuplicateCharacterId,
                    "Character artifact has a duplicate character_id.",
                    characterId);
            }
        }

        private sealed class IndexScan
        {
            public IndexScan(
                Dictionary<string, string> index,
                IReadOnlyList<CharacterStoreDiagnostic> diagnostics)
            {
                Index = index;
                Diagnostics = diagnostics;
            }

            public Dictionary<string, string> Index { get; }
            public IReadOnlyList<CharacterStoreDiagnostic> Diagnostics { get; }
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

        private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
        {
            await MaybeDelayForTestAsync(ct).ConfigureAwait(false);

            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize, useAsync: true);
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, DefaultBufferSize, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return output.ToArray();
        }

        private static async Task WriteAllBytesAsync(
            string path,
            byte[] content,
            CancellationToken ct)
        {
            await MaybeDelayForTestAsync(ct).ConfigureAwait(false);
            using var stream = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None, DefaultBufferSize, useAsync: true);
            await stream.WriteAsync(content, 0, content.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static async Task AtomicWriteAsync(
            string path,
            byte[] content,
            bool replaceExisting,
            CancellationToken ct)
        {
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await WriteAllBytesAsync(tempPath, content, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (replaceExisting)
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private async Task<FileStream> AcquireStoreLockAsync(CancellationToken ct)
        {
            string path = Path.Combine(_directory, StoreLockFileName);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(
                        path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        1,
                        useAsync: false);
                }
                catch (IOException)
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                }
            }
        }

        private static void ValidateCharacterIdArgument(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
        }

        private static void ValidateReplacementIdentity(
            string characterId,
            byte[] replacementContent)
        {
            try
            {
                using var document = JsonDocument.Parse(replacementContent);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("character_id", out var id)
                    || id.ValueKind != JsonValueKind.String
                    || !string.Equals(id.GetString(), characterId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException(
                        "Replacement artifact character_id does not match the requested character.");
                }
            }
            catch (JsonException ex)
            {
                throw new FormatException("Replacement artifact is not valid JSON.", ex);
            }
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

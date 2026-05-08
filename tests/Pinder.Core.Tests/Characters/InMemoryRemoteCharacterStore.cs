using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.Core.Tests.Characters
{
    /// <summary>
    /// Test-only in-memory implementation of <see cref="IRemoteCharacterStore"/>.
    /// Validates the interface shape works for at least one impl and gives
    /// IRemoteCharacterStore-consuming tests something to inject.
    ///
    /// Lives in the test project intentionally — issue #817 does not ship
    /// any production impl (gated #819).
    /// </summary>
    public sealed class InMemoryRemoteCharacterStore : IRemoteCharacterStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, CharacterDefinition> _payloads =
            new Dictionary<string, CharacterDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CharacterAssetMetadata> _metas =
            new Dictionary<string, CharacterAssetMetadata>(StringComparer.OrdinalIgnoreCase);

        // Sequence counter the fake uses to generate deterministic
        // server-stamped CreatedAt / UpdatedAt timestamps.
        private long _seq;

        public string SimulatedServiceOwnerId { get; set; } = "service:test";

        // --- ICharacterStore ------------------------------------------------

        public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IReadOnlyList<string> ids = _payloads.Keys.ToList();
                return Task.FromResult(ids);
            }
        }

        public Task<CharacterDefinition?> LoadAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _payloads.TryGetValue(characterId, out var def);
                return Task.FromResult<CharacterDefinition?>(def);
            }
        }

        public Task SaveAsync(CharacterDefinition def, CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            ct.ThrowIfCancellationRequested();
            string id = def.CharacterId.ToString("D");
            lock (_gate)
            {
                _payloads[id] = def;
                if (!_metas.ContainsKey(id))
                {
                    _metas[id] = StampServerSide(new CharacterAssetMetadata(
                        characterId: id,
                        ownerId: SimulatedServiceOwnerId,
                        tags: Array.Empty<string>(),
                        isPublic: false,
                        createdAt: DateTimeOffset.MinValue,
                        updatedAt: DateTimeOffset.MinValue),
                        existing: null);
                }
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
                bool removed = _payloads.Remove(characterId);
                _metas.Remove(characterId);
                return Task.FromResult(removed);
            }
        }

        public Task<bool> ExistsAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_payloads.ContainsKey(characterId));
            }
        }

        // --- IRemoteCharacterStore ------------------------------------------

        public Task<CharacterAssetPage> QueryAsync(CharacterAssetQuery query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                IEnumerable<CharacterAssetMetadata> results = _metas.Values
                    .OrderBy(m => m.UpdatedAt)
                    .ThenBy(m => m.CharacterId, StringComparer.Ordinal);

                if (!string.IsNullOrEmpty(query.OwnerId))
                    results = results.Where(m => m.OwnerId == query.OwnerId);
                if (query.IsPublic.HasValue)
                    results = results.Where(m => m.IsPublic == query.IsPublic.Value);
                if (query.Tags != null && query.Tags.Count > 0)
                    results = results.Where(m => query.Tags.All(t =>
                        m.Tags.Contains(t, StringComparer.Ordinal)));

                var ordered = results.ToList();

                int offset = 0;
                if (!string.IsNullOrEmpty(query.Cursor) && int.TryParse(query.Cursor, out var parsed))
                    offset = Math.Max(0, parsed);

                var page = ordered.Skip(offset).Take(query.Limit).ToList();
                int nextOffset = offset + page.Count;
                string? nextCursor = nextOffset < ordered.Count
                    ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null;

                return Task.FromResult(new CharacterAssetPage(page, nextCursor));
            }
        }

        public Task<CharacterAssetMetadata?> GetMetadataAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must be non-empty.", nameof(characterId));
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _metas.TryGetValue(characterId, out var meta);
                return Task.FromResult<CharacterAssetMetadata?>(meta);
            }
        }

        public Task<CharacterAssetMetadata> PublishAsync(
            CharacterDefinition def,
            CharacterAssetMetadata metadata,
            CancellationToken ct = default)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            ct.ThrowIfCancellationRequested();

            string id = def.CharacterId.ToString("D");

            lock (_gate)
            {
                _payloads[id] = def;

                _metas.TryGetValue(id, out var existing);
                var stamped = StampServerSide(
                    new CharacterAssetMetadata(
                        characterId: id,
                        ownerId: existing?.OwnerId ?? SimulatedServiceOwnerId,
                        tags: metadata.Tags,
                        isPublic: metadata.IsPublic,
                        createdAt: DateTimeOffset.MinValue, // overwritten below
                        updatedAt: DateTimeOffset.MinValue,
                        assetKind: metadata.AssetKind),
                    existing);
                _metas[id] = stamped;
                return Task.FromResult(stamped);
            }
        }

        // --- helpers --------------------------------------------------------

        private CharacterAssetMetadata StampServerSide(
            CharacterAssetMetadata client,
            CharacterAssetMetadata? existing)
        {
            long seq = System.Threading.Interlocked.Increment(ref _seq);
            // Use a fixed epoch + the sequence to keep tests deterministic
            // without dragging IClock through the test fake.
            var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var stamp = epoch.AddSeconds(seq);
            return new CharacterAssetMetadata(
                characterId: client.CharacterId,
                ownerId: existing?.OwnerId ?? SimulatedServiceOwnerId,
                tags: client.Tags,
                isPublic: client.IsPublic,
                createdAt: existing?.CreatedAt ?? stamp,
                updatedAt: stamp,
                assetKind: client.AssetKind);
        }
    }
}

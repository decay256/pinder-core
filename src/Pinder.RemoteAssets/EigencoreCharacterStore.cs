using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.RemoteAssets
{
    /// <summary>
    /// Eigencore-backed <see cref="IRemoteCharacterStore"/>. Talks raw HTTP
    /// against the asset-backend contract documented in
    /// <c>docs/specs/character-asset-vocabulary.md</c>.
    ///
    /// This is the ONLY assembly in the pinder-core repo that is allowed
    /// to know an eigencore-shaped backend exists. Pinder.Core, the engine,
    /// and the session runner do not reference this type. See AGENTS.md
    /// ("Eigencore is a THIRD-PARTY APP from Pinder's perspective") and
    /// lesson §35 of pinder-web/LESSONS_LEARNED.md.
    ///
    /// Sub-PR scope:
    /// #853 — read path (<see cref="LoadAsync"/>, <see cref="GetMetadataAsync"/>,
    ///        <see cref="ExistsAsync"/>).
    /// #854 — query / paging path (<see cref="QueryAsync"/>).
    /// #855 — write path (<see cref="PublishAsync"/>,
    ///        <see cref="SaveAsync"/>, <see cref="DeleteAsync"/>).
    /// <see cref="ListIdsAsync"/> remains <see cref="NotSupportedException"/>:
    /// the v1 wire contract has no list-all endpoint; discovery happens
    /// via <see cref="QueryAsync"/>.
    /// </summary>
    public sealed class EigencoreCharacterStore : IRemoteCharacterStore
    {
        private readonly Configuration _config;
        private readonly HttpClient _http;
        private readonly EigencoreCharacterStoreRead _readStore;
        private readonly SyncHelper _syncHelper;

        public EigencoreCharacterStore(Configuration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // disposeHandler:false — the caller owns the lifetime of the
            // injected HttpMessageHandler (socket-exhaustion guidance).
            _http = new HttpClient(_config.HttpMessageHandler, disposeHandler: false)
            {
                Timeout = _config.RequestTimeout,
            };

            // Normalize BaseUrl to end with a single slash so relative
            // "assets/{id}" appends cleanly.
            var baseStr = _config.BaseUrl.AbsoluteUri;
            if (!baseStr.EndsWith("/", StringComparison.Ordinal))
                baseStr += "/";
            _http.BaseAddress = new Uri(baseStr, UriKind.Absolute);

            // Cap response buffer size proportional to PayloadSizeCapBytes
            // (defence-in-depth against a compromised or misconfigured
            // eigencore — the contract allows at most PayloadSizeCapBytes
            // plus metadata envelope + HTTP framing; *4 gives generous
            // headroom without permitting unbounded responses).
            _http.MaxResponseContentBufferSize = Math.Max(_config.PayloadSizeCapBytes, 1024 * 1024) * 4;

            _readStore = new EigencoreCharacterStoreRead(_config, _http);
            _syncHelper = new SyncHelper(_config, _http);
        }

        public Task<CharacterDefinition?> LoadAsync(string characterId, CancellationToken ct = default) =>
            _readStore.LoadAsync(characterId, ct);

        public Task<CharacterAssetMetadata?> GetMetadataAsync(string characterId, CancellationToken ct = default) =>
            _readStore.GetMetadataAsync(characterId, ct);

        public Task<bool> ExistsAsync(string characterId, CancellationToken ct = default) =>
            _readStore.ExistsAsync(characterId, ct);

        public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct = default) =>
            _readStore.ListIdsAsync(ct);

        public Task<CharacterAssetPage> QueryAsync(CharacterAssetQuery query, CancellationToken ct = default) =>
            _readStore.QueryAsync(query, ct);

        public Task<CharacterAssetMetadata> PublishAsync(
            CharacterDefinition def,
            CharacterAssetMetadata metadata,
            CancellationToken ct = default) =>
            _syncHelper.PublishAsync(def, metadata, ct);

        public Task SaveAsync(CharacterDefinition def, CancellationToken ct = default) =>
            _syncHelper.SaveAsync(def, ct);

        public Task<bool> DeleteAsync(string characterId, CancellationToken ct = default) =>
            _syncHelper.DeleteAsync(characterId, ct);
    }
}

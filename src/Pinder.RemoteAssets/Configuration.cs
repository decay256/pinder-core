using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.RemoteAssets
{
    /// <summary>
    /// Parses raw payload bytes (a v1 character definition JSON, as it
    /// sits on disk and on the wire) into a strongly-typed
    /// <see cref="CharacterDefinition"/>.
    ///
    /// Injected so <c>Pinder.RemoteAssets</c> stays decoupled from any
    /// specific parser implementation — production callers wire in
    /// <c>Pinder.SessionSetup.CharacterDefinitionLoader.ParseDefinition</c>;
    /// tests inject whatever stub keeps the test small.
    /// </summary>
    public delegate CharacterDefinition CharacterPayloadParser(byte[] payload);

    /// <summary>
    /// Serialises a <see cref="CharacterDefinition"/> to the raw payload
    /// bytes that ride in the <c>payload</c> part of a <c>POST /assets</c>
    /// multipart request. The contract is byte-equal to the on-disk v1
    /// character JSON — see <c>docs/specs/character-asset-vocabulary.md</c>
    /// § Publish.
    ///
    /// If <see cref="Configuration.PayloadSerializer"/> is left null,
    /// the store falls back to
    /// <c>Pinder.Core.Characters.CharacterDefinitionWriter.Write</c>
    /// (UTF-8 encoded). Tests can inject a stub that returns canned bytes.
    ///
    /// A <c>null</c> or zero-length return value is treated as a failed
    /// serialization and throws <c>RemoteAssetValidationException</c>
    /// BEFORE any HTTP request is sent — the store never silently
    /// substitutes an empty payload (fail-fast; see
    /// <c>SyncHelper.SerializePayload</c>).
    /// </summary>
    public delegate byte[] CharacterPayloadSerializer(CharacterDefinition def);

    /// <summary>
    /// Configuration for <see cref="EigencoreCharacterStore"/> — the
    /// <see cref="IRemoteCharacterStore"/> implementation that talks HTTP to
    /// an eigencore-shaped asset backend.
    ///
    /// All fields are injected by the caller. <c>Pinder.RemoteAssets</c> is the
    /// only assembly in this repo that is allowed to know eigencore exists
    /// (architectural rule from <c>AGENTS.md</c> / lesson §35 of
    /// <c>pinder-web/LESSONS_LEARNED.md</c>). Even within this assembly we
    /// avoid wiring the type to a specific HTTP client — the
    /// <see cref="HttpMessageHandler"/> is the seam tests use to inject a fake.
    /// </summary>
    public sealed class Configuration
    {
        /// <summary>
        /// Base URL of the asset backend. The wrapper absorbs the
        /// <c>/api/v1</c> (or future-rev) prefix here so the wire-level
        /// route code does NOT hard-code <c>/api/v1</c>. Example for the
        /// reference backend: <c>https://eigencore.example.com/api/v1</c>.
        /// MUST be absolute and use the HTTPS scheme.
        /// </summary>
        public Uri BaseUrl { get; }

        /// <summary>
        /// The <see cref="HttpMessageHandler"/> the store will compose its
        /// internal <see cref="HttpClient"/> on top of. Production callers
        /// inject a real <c>HttpClientHandler</c> (or whatever is configured
        /// in DI); tests inject a fake.
        ///
        /// The store does NOT dispose this handler — ownership belongs to the
        /// caller. Reuse a single handler across the process per the standard
        /// .NET socket-exhaustion guidance.
        /// </summary>
        public HttpMessageHandler HttpMessageHandler { get; }

        /// <summary>
        /// Auth-token provider. Called on every request to fetch the
        /// current bearer token, in case the caller rotates tokens
        /// out-of-band. The wrapper does NOT cache the returned token.
        /// Provider MAY return an empty string for unauthenticated
        /// requests; the wrapper then omits the <c>Authorization</c>
        /// header rather than sending <c>Bearer </c> with no value.
        /// </summary>
        public Func<CancellationToken, Task<string>> AuthTokenProvider { get; }

        /// <summary>
        /// Parses raw payload bytes into a <see cref="CharacterDefinition"/>.
        /// Production callers wire
        /// <c>Pinder.SessionSetup.CharacterDefinitionLoader.ParseDefinition</c>;
        /// the indirection keeps this assembly from depending on
        /// <c>Pinder.SessionSetup</c>.
        /// </summary>
        public CharacterPayloadParser PayloadParser { get; }

        /// <summary>
        /// Optional per-request HTTP timeout. Defaults to
        /// <see cref="TimeSpan.FromSeconds(30)"/>. Network-level errors
        /// (DNS, connection reset, timeout) propagate as
        /// <see cref="HttpRequestException"/> — the wrapper does NOT
        /// wrap them in a typed exception.
        /// </summary>
        public TimeSpan RequestTimeout { get; }

        /// <summary>
        /// Optional default delay used when a 429 response is missing or
        /// has an unparseable <c>Retry-After</c> header. Defaults to one
        /// second. Tests override this to keep the suite fast.
        /// </summary>
        public TimeSpan DefaultRetryAfter { get; }

        /// <summary>
        /// Hard cap on the size of the serialized <c>metadata</c> part of
        /// a <c>POST /assets</c> multipart request. Defaults to 4 KiB —
        /// matches the reference backend's
        /// <c>MAX_ASSET_METADATA_JSON_SIZE</c> (not env-overridable
        /// server-side). Violations throw
        /// <c>RemoteAssetTooLargeException(subject="metadata")</c> BEFORE
        /// the HTTP request is sent (fail-fast — see
        /// <c>docs/specs/character-asset-vocabulary.md</c> § Publish).
        /// </summary>
        public int MetadataSizeCapBytes { get; }

        /// <summary>
        /// Cap on the size of the <c>payload</c> part of a <c>POST /assets</c>
        /// multipart request. Defaults to 256 KiB — matches the reference
        /// backend's <c>max_asset_payload_size</c> default. Per-deployment
        /// overrides on the server can raise / lower this; callers should
        /// match the deployed value here when known. Violations throw
        /// <c>RemoteAssetTooLargeException(subject="payload")</c> BEFORE
        /// the HTTP request is sent.
        /// </summary>
        public int PayloadSizeCapBytes { get; }

        /// <summary>
        /// Serialises a <see cref="CharacterDefinition"/> to the bytes that
        /// ride in the <c>payload</c> part of a <c>POST /assets</c>
        /// multipart request. If null, the store falls back to
        /// <c>CharacterDefinitionWriter.Write</c> + UTF-8 encoding.
        /// </summary>
        public CharacterPayloadSerializer? PayloadSerializer { get; }

        /// <param name="allowInsecureBaseUrl">
        /// Defaults to <c>false</c>. When <c>true</c>, permits <c>http://</c>
        /// URIs in <see cref="BaseUrl"/> instead of enforcing the HTTPS
        /// scheme. Intended ONLY for tests against local fake servers; never
        /// used in production code.
        /// </param>
        public Configuration(
            Uri baseUrl,
            HttpMessageHandler httpMessageHandler,
            Func<CancellationToken, Task<string>> authTokenProvider,
            CharacterPayloadParser payloadParser,
            TimeSpan? requestTimeout = null,
            TimeSpan? defaultRetryAfter = null,
            int? metadataSizeCapBytes = null,
            int? payloadSizeCapBytes = null,
            CharacterPayloadSerializer? payloadSerializer = null,
            bool allowInsecureBaseUrl = false)
        {
            if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
            if (!baseUrl.IsAbsoluteUri)
                throw new ArgumentException("BaseUrl must be absolute.", nameof(baseUrl));
            if (!allowInsecureBaseUrl && baseUrl.Scheme != "https")
                throw new ArgumentException($"BaseUrl must use the HTTPS scheme. Rejected scheme: {baseUrl.Scheme}.", nameof(baseUrl));
            BaseUrl = baseUrl;
            HttpMessageHandler = httpMessageHandler ?? throw new ArgumentNullException(nameof(httpMessageHandler));
            AuthTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
            PayloadParser = payloadParser ?? throw new ArgumentNullException(nameof(payloadParser));
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
            DefaultRetryAfter = defaultRetryAfter ?? TimeSpan.FromSeconds(1);
            MetadataSizeCapBytes = metadataSizeCapBytes ?? 4 * 1024;       // 4 KiB
            PayloadSizeCapBytes = payloadSizeCapBytes ?? 256 * 1024;       // 256 KiB
            PayloadSerializer = payloadSerializer;
            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(requestTimeout), "RequestTimeout must be positive.");
            if (DefaultRetryAfter < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(defaultRetryAfter), "DefaultRetryAfter must be non-negative.");
            if (MetadataSizeCapBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(metadataSizeCapBytes), "MetadataSizeCapBytes must be positive.");
            if (PayloadSizeCapBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(payloadSizeCapBytes), "PayloadSizeCapBytes must be positive.");
        }
    }
}

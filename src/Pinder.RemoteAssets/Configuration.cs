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
        /// MUST be absolute. A trailing slash is tolerated either way.
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

        public Configuration(
            Uri baseUrl,
            HttpMessageHandler httpMessageHandler,
            Func<CancellationToken, Task<string>> authTokenProvider,
            CharacterPayloadParser payloadParser,
            TimeSpan? requestTimeout = null,
            TimeSpan? defaultRetryAfter = null)
        {
            if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
            if (!baseUrl.IsAbsoluteUri)
                throw new ArgumentException("BaseUrl must be absolute.", nameof(baseUrl));
            BaseUrl = baseUrl;
            HttpMessageHandler = httpMessageHandler ?? throw new ArgumentNullException(nameof(httpMessageHandler));
            AuthTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
            PayloadParser = payloadParser ?? throw new ArgumentNullException(nameof(payloadParser));
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
            DefaultRetryAfter = defaultRetryAfter ?? TimeSpan.FromSeconds(1);
            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(requestTimeout), "RequestTimeout must be positive.");
            if (DefaultRetryAfter < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(defaultRetryAfter), "DefaultRetryAfter must be non-negative.");
        }
    }
}

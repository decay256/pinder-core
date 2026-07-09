using System.Collections.Generic;
using System.Linq;

namespace Pinder.Core.Contracts
{
    /// <summary>
    /// #1127: version handshake policy for hosts that execute the game engine.
    /// This type is the canonical source of truth for the current API contract
    /// version and supported-version set. Host applications own their request
    /// envelopes, response bodies, and serializer-specific field names.
    ///
    /// <para>
    /// <b>Bump policy (monotonic integer).</b> <see cref="ApiContractVersion"/>
    /// is a monotonically increasing integer. It is incremented by exactly one
    /// on ANY breaking change to the request/response contract: a renamed or
    /// removed field, a changed field type, a changed required-ness, or a
    /// changed semantic interpretation of an existing field. Purely additive,
    /// backwards-compatible changes do NOT bump the version. The handshake's
    /// only job is accept/reject on the supported-set match below; an integer is
    /// deliberately simpler to validate and document than semver because there
    /// is no notion of "minor compatible": a version is either in
    /// <see cref="SupportedVersions"/> or it is rejected.
    /// </para>
    ///
    /// <para>
    /// <b>Coordination with #1128 (T8).</b> #1128's integration doc MUST stamp
    /// the SAME number this constant holds (currently <c>1</c>). This constant
    /// is authoritative; the doc mirrors it. <see cref="ContractTests"/> assert
    /// the stamped number so a drift between code and doc breaks the build.
    /// </para>
    /// </summary>
    public static class ApiContract
    {
        /// <summary>
        /// The current canonical API contract version. Monotonic integer; see the
        /// type doc-comment for the bump policy. #1128 must stamp this same value.
        /// </summary>
        public const int ApiContractVersion = 1;

        /// <summary>
        /// The set of contract versions this build accepts. Today it is exactly
        /// the current version. When a breaking change ships we bump
        /// <see cref="ApiContractVersion"/>; this set may transiently list more
        /// than one entry during a deliberate dual-version migration window, but
        /// the default policy is "current version only".
        /// </summary>
        public static readonly IReadOnlyCollection<int> SupportedVersions =
            new[] { ApiContractVersion };

        /// <summary>
        /// True only if <paramref name="apiVersion"/> is present AND belongs to
        /// the <see cref="SupportedVersions"/> set. A missing (null) version is
        /// NOT supported: the handshake is mandatory. Note this deliberately is
        /// NOT a naive <c>apiVersion != null</c> check: a present-but-unsupported
        /// version (e.g. a future <c>2</c>) is rejected even though it parses fine.
        /// </summary>
        public static bool IsSupported(int? apiVersion)
        {
            return apiVersion.HasValue && SupportedVersions.Contains(apiVersion.Value);
        }

        /// <summary>
        /// Validates the <c>apiVersion</c> carried by a request contract against
        /// the supported set. Returns <c>null</c> when the version is accepted, or
        /// a populated <see cref="ApiVersionMismatchError"/> describing the
        /// rejection. This is a domain-level compatibility decision; host
        /// applications decide how to expose that rejection at their transport
        /// boundary.
        /// </summary>
        /// <returns>
        /// <c>null</c> if accepted; otherwise the structured mismatch error value.
        /// </returns>
        public static ApiVersionMismatchError? Validate(ApiRequestContract? request)
        {
            int? received = request?.ApiVersion;
            if (IsSupported(received))
            {
                return null;
            }

            return ApiVersionMismatchError.ForReceived(received);
        }
    }
}

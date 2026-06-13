using System.Text.Json.Serialization;

namespace Pinder.Core.Contracts
{
    /// <summary>
    /// #1127: the canonical Unity client ⇆ game-api REQUEST envelope contract.
    /// Today it carries only the mandatory version handshake field; future
    /// request payload fields are added here (additive fields do not bump
    /// <see cref="ApiContract.ApiContractVersion"/> — see the bump policy).
    ///
    /// <para>
    /// The <c>apiVersion</c> field is defined ON the request contract rather than
    /// as a parallel handshake envelope so there is a single, non-jagged wire
    /// shape that both server and client serialize. It is nullable so the
    /// "missing version" case round-trips faithfully through deserialization and
    /// is then rejected by <see cref="ApiContract.Validate"/> — a missing version
    /// is a mismatch, not a silent default.
    /// </para>
    /// </summary>
    public sealed class ApiRequestContract
    {
        /// <summary>
        /// The wire field name for the API contract version. Pinned here as the
        /// single source of truth for the serialized property name; renaming it
        /// is a breaking wire change and is guarded by the contract tests.
        /// </summary>
        public const string ApiVersionFieldName = "apiVersion";

        /// <summary>
        /// The contract version the caller declares it speaks. Nullable: a request
        /// that omits it deserializes to <c>null</c> and is rejected by the
        /// handshake (the version is mandatory). Serialized as <c>apiVersion</c>.
        /// </summary>
        [JsonPropertyName(ApiVersionFieldName)]
        public int? ApiVersion { get; set; }

        public ApiRequestContract()
        {
        }

        public ApiRequestContract(int? apiVersion)
        {
            ApiVersion = apiVersion;
        }
    }
}

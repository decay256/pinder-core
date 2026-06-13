using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinder.Core.Contracts
{
    /// <summary>
    /// #1127: the structured error a server emits when a request's
    /// <c>apiVersion</c> handshake fails (missing or unsupported). Defined ONCE
    /// here in pinder-core so pinder-web (which performs the actual wire-level
    /// rejection in game-api 5101) and the Unity client (which parses the
    /// rejection) reference the SAME type and the SAME field names — there is no
    /// second, drifting copy of the error shape.
    ///
    /// <para>
    /// <b>Wire body shape</b> (System.Text.Json, camelCase property names pinned
    /// via <see cref="JsonPropertyName"/>):
    /// <code>
    /// {
    ///   "code": "api_version_mismatch",
    ///   "message": "Unsupported apiVersion ...",
    ///   "received": 2,            // null when the request omitted apiVersion
    ///   "supported": [1]
    /// }
    /// </code>
    /// The <see cref="Code"/> string and the four field names are part of the wire
    /// contract; the regression tests pin them so a rename breaks the build rather
    /// than silently changing the wire.
    /// </para>
    /// </summary>
    public sealed class ApiVersionMismatchError
    {
        /// <summary>
        /// The stable, machine-readable error code clients switch on. PINNED wire
        /// value — do not change without bumping <see cref="ApiContract.ApiContractVersion"/>.
        /// </summary>
        public const string CodeValue = "api_version_mismatch";

        /// <summary>Machine-readable error code. Always <see cref="CodeValue"/>.</summary>
        [JsonPropertyName("code")]
        public string Code { get; set; } = CodeValue;

        /// <summary>Human-readable diagnostic; not a stable contract surface.</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The version the client sent, or <c>null</c> if the request omitted
        /// <c>apiVersion</c> entirely. Echoed back so the client can diagnose.
        /// </summary>
        [JsonPropertyName("received")]
        public int? Received { get; set; }

        /// <summary>The versions this server accepts (mirrors <see cref="ApiContract.SupportedVersions"/>).</summary>
        [JsonPropertyName("supported")]
        public IReadOnlyCollection<int> Supported { get; set; } = new int[0];

        public ApiVersionMismatchError()
        {
        }

        /// <summary>
        /// Builds the mismatch error for a given received version (null = the
        /// request omitted <c>apiVersion</c>). Populates the supported set and a
        /// deterministic human-readable message.
        /// </summary>
        public static ApiVersionMismatchError ForReceived(int? received)
        {
            var supported = ApiContract.SupportedVersions.ToArray();
            string supportedList = string.Join(", ", supported);
            string message = received.HasValue
                ? $"Unsupported apiVersion {received.Value}; this server supports [{supportedList}]."
                : $"Missing required apiVersion; this server supports [{supportedList}].";

            return new ApiVersionMismatchError
            {
                Code = CodeValue,
                Message = message,
                Received = received,
                Supported = supported,
            };
        }

        /// <summary>
        /// Serializes this error to its canonical wire JSON. Deterministic: the
        /// same error value always produces byte-identical JSON (no indentation,
        /// fixed property order from the declaration / attributes).
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, SerializerOptions);
        }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            // Property names come from [JsonPropertyName]; no naming policy applied
            // so the wire shape is fully pinned by the attributes above.
        };
    }
}

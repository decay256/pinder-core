using System.Collections.Generic;
using System.Linq;

namespace Pinder.Core.Contracts
{
    /// <summary>
    /// #1127: domain value returned when an <c>apiVersion</c> handshake fails
    /// because the version is missing or unsupported. Transport layers map this
    /// value to their own DTOs and serializer-specific response bodies.
    /// </summary>
    public sealed class ApiVersionMismatchError
    {
        /// <summary>
        /// The stable, machine-readable error code hosts and clients switch on.
        /// Do not change without bumping <see cref="ApiContract.ApiContractVersion"/>.
        /// </summary>
        public const string CodeValue = "api_version_mismatch";

        /// <summary>Machine-readable error code. Always <see cref="CodeValue"/>.</summary>
        public string Code { get; set; } = CodeValue;

        /// <summary>Human-readable diagnostic; not a stable contract surface.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The version the client sent, or <c>null</c> if the request omitted
        /// <c>apiVersion</c> entirely. Echoed back so the client can diagnose.
        /// </summary>
        public int? Received { get; set; }

        /// <summary>The versions this engine build accepts.</summary>
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
    }
}

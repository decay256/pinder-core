using System.Text.Json;
using Pinder.Core.Contracts;
using Xunit;

namespace Pinder.Core.Tests.Contracts
{
    /// <summary>
    /// #1127 WIRE-CONTRACT-REGRESSION-TESTS for the apiVersion handshake.
    ///
    /// The "looks right but is wrong" trap for a version handshake is a naive
    /// <c>apiVersion != null</c> (or any non-empty) acceptance check: it accepts a
    /// present-but-unsupported version. These tests deliberately feed values that
    /// PASS a naive check but must FAIL the documented supported-set policy, and
    /// pin the exact wire <c>code</c> string + body field names so a rename breaks
    /// the build rather than silently changing the wire.
    /// </summary>
    public class ApiContractTests
    {
        // ---- version constant / #1128 coordination --------------------------------

        [Fact]
        public void ApiContractVersion_is_the_stamped_monotonic_integer_one()
        {
            // #1128 (T8) MUST stamp this SAME number in its integration doc.
            // If the canonical version is bumped, update #1128's doc in the same change.
            Assert.Equal(1, ApiContract.ApiContractVersion);
        }

        [Fact]
        public void SupportedVersions_contains_the_current_version()
        {
            Assert.Contains(ApiContract.ApiContractVersion, ApiContract.SupportedVersions);
        }

        // ---- accept / reject policy (the "looks right but wrong" trap) -------------

        [Fact]
        public void Matching_version_is_accepted()
        {
            var request = new ApiRequestContract(ApiContract.ApiContractVersion);

            Assert.True(ApiContract.IsSupported(request.ApiVersion));
            Assert.Null(ApiContract.Validate(request)); // null == accepted
        }

        [Fact]
        public void Missing_apiVersion_maps_to_the_mismatch_error()
        {
            var request = new ApiRequestContract(null);

            // Trap: a naive `apiVersion != null` check would treat this differently,
            // but the handshake is mandatory — a missing version is a mismatch.
            Assert.False(ApiContract.IsSupported(request.ApiVersion));

            var error = ApiContract.Validate(request);
            Assert.NotNull(error);
            Assert.Equal(ApiVersionMismatchError.CodeValue, error!.Code);
            Assert.Null(error.Received);
        }

        [Fact]
        public void Wrong_but_parseable_apiVersion_maps_to_the_mismatch_error()
        {
            // 2 is a perfectly parseable integer and would PASS any naive
            // "non-null"/"parses as int" acceptance — but it is NOT in the
            // supported set, so the documented policy must reject it.
            const int unsupportedButParseable = 2;
            var request = new ApiRequestContract(unsupportedButParseable);

            Assert.False(ApiContract.IsSupported(request.ApiVersion));

            var error = ApiContract.Validate(request);
            Assert.NotNull(error);
            Assert.Equal(ApiVersionMismatchError.CodeValue, error!.Code);
            Assert.Equal(unsupportedButParseable, error.Received);
        }

        [Fact]
        public void Null_request_maps_to_the_mismatch_error()
        {
            var error = ApiContract.Validate(null);
            Assert.NotNull(error);
            Assert.Equal(ApiVersionMismatchError.CodeValue, error!.Code);
            Assert.Null(error.Received);
        }

        // ---- exact wire body pinning (rename must break the build) -----------------

        [Fact]
        public void Mismatch_error_code_string_is_pinned()
        {
            // A refactor that renames the code must break THIS test, not silently
            // change the wire contract every client switches on.
            Assert.Equal("api_version_mismatch", ApiVersionMismatchError.CodeValue);
        }

        [Fact]
        public void Request_contract_field_name_is_pinned()
        {
            Assert.Equal("apiVersion", ApiRequestContract.ApiVersionFieldName);
        }

        [Fact]
        public void Mismatch_error_serializes_to_the_documented_body_shape_for_unsupported_version()
        {
            var error = ApiVersionMismatchError.ForReceived(2);

            // Deterministic, byte-stable serialization of the documented shape.
            // Property order follows declaration order: code, message, received, supported.
            string json = error.ToJson();
            Assert.Equal(
                "{\"code\":\"api_version_mismatch\",\"message\":\"Unsupported apiVersion 2; this server supports [1].\",\"received\":2,\"supported\":[1]}",
                json);
        }

        [Fact]
        public void Mismatch_error_serializes_to_the_documented_body_shape_for_missing_version()
        {
            var error = ApiVersionMismatchError.ForReceived(null);

            string json = error.ToJson();
            Assert.Equal(
                "{\"code\":\"api_version_mismatch\",\"message\":\"Missing required apiVersion; this server supports [1].\",\"received\":null,\"supported\":[1]}",
                json);
        }

        [Fact]
        public void Mismatch_error_body_field_names_are_pinned()
        {
            // Pin the EXACT JSON property names. Renaming any C# property without
            // its [JsonPropertyName] must break this test (the wire field names are
            // the contract clients parse).
            using var doc = JsonDocument.Parse(ApiVersionMismatchError.ForReceived(2).ToJson());
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("code", out _), "wire field 'code' missing");
            Assert.True(root.TryGetProperty("message", out _), "wire field 'message' missing");
            Assert.True(root.TryGetProperty("received", out _), "wire field 'received' missing");
            Assert.True(root.TryGetProperty("supported", out _), "wire field 'supported' missing");
            Assert.Equal(4, root.EnumerateObject().Count()); // no extra/unexpected wire fields
        }

        // ---- request contract round-trips the missing-version case faithfully -----

        [Fact]
        public void Request_deserializes_missing_apiVersion_to_null_then_rejected()
        {
            // A wire payload that omits apiVersion must deserialize to null (not a
            // silent default of 0), and then be rejected by the handshake.
            var request = JsonSerializer.Deserialize<ApiRequestContract>("{}");
            Assert.NotNull(request);
            Assert.Null(request!.ApiVersion);
            Assert.NotNull(ApiContract.Validate(request));
        }

        [Fact]
        public void Request_round_trips_apiVersion_field_name_on_the_wire()
        {
            var request = new ApiRequestContract(ApiContract.ApiContractVersion);
            string json = JsonSerializer.Serialize(request);

            // The serialized property MUST be the pinned 'apiVersion' name.
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("apiVersion", out var v));
            Assert.Equal(ApiContract.ApiContractVersion, v.GetInt32());
        }
    }
}

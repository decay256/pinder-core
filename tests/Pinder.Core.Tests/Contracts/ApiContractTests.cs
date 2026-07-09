using System.Text.Json;
using Pinder.Core.Contracts;
using Xunit;

namespace Pinder.Core.Tests.Contracts
{
    /// <summary>
    /// #1127 regression tests for the apiVersion handshake policy.
    ///
    /// The "looks right but is wrong" trap for a version handshake is a naive
    /// <c>apiVersion != null</c> acceptance check: it accepts a present-but-
    /// unsupported version. These tests deliberately feed values that pass a
    /// naive check but must fail the documented supported-set policy, and pin the
    /// mismatch value consumed by host-layer transport DTOs.
    /// </summary>
    public class ApiContractTests
    {
        [Fact]
        public void ApiContractVersion_is_the_stamped_monotonic_integer_one()
        {
            Assert.Equal(1, ApiContract.ApiContractVersion);
        }

        [Fact]
        public void SupportedVersions_contains_the_current_version()
        {
            Assert.Contains(ApiContract.ApiContractVersion, ApiContract.SupportedVersions);
        }

        [Fact]
        public void Matching_version_is_accepted()
        {
            var request = new ApiRequestContract(ApiContract.ApiContractVersion);

            Assert.True(ApiContract.IsSupported(request.ApiVersion));
            Assert.Null(ApiContract.Validate(request));
        }

        [Fact]
        public void Missing_apiVersion_maps_to_the_mismatch_error()
        {
            var request = new ApiRequestContract(null);

            Assert.False(ApiContract.IsSupported(request.ApiVersion));

            var error = ApiContract.Validate(request);
            Assert.NotNull(error);
            Assert.Equal(ApiVersionMismatchError.CodeValue, error!.Code);
            Assert.Null(error.Received);
        }

        [Fact]
        public void Wrong_but_parseable_apiVersion_maps_to_the_mismatch_error()
        {
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

        [Fact]
        public void Mismatch_error_code_string_is_pinned()
        {
            Assert.Equal("api_version_mismatch", ApiVersionMismatchError.CodeValue);
        }

        [Fact]
        public void Request_contract_field_name_is_pinned()
        {
            Assert.Equal("apiVersion", ApiRequestContract.ApiVersionFieldName);
        }

        [Fact]
        public void Mismatch_error_for_unsupported_version_populates_domain_value()
        {
            var error = ApiVersionMismatchError.ForReceived(2);

            Assert.Equal(ApiVersionMismatchError.CodeValue, error.Code);
            Assert.Equal("Unsupported apiVersion 2; this server supports [1].", error.Message);
            Assert.Equal(2, error.Received);
            Assert.Equal(new[] { ApiContract.ApiContractVersion }, error.Supported);
        }

        [Fact]
        public void Mismatch_error_for_missing_version_populates_domain_value()
        {
            var error = ApiVersionMismatchError.ForReceived(null);

            Assert.Equal(ApiVersionMismatchError.CodeValue, error.Code);
            Assert.Equal("Missing required apiVersion; this server supports [1].", error.Message);
            Assert.Null(error.Received);
            Assert.Equal(new[] { ApiContract.ApiContractVersion }, error.Supported);
        }

        [Fact]
        public void Request_deserializes_missing_apiVersion_to_null_then_rejected()
        {
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

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("apiVersion", out var v));
            Assert.Equal(ApiContract.ApiContractVersion, v.GetInt32());
        }
    }
}

using System;
using System.Collections.Generic;
using Xunit;
using Pinder.RemoteAssets.Exceptions;

namespace Pinder.RemoteAssets.Tests
{
    public class ExceptionToStringTests
    {
        [Fact]
        public void RemoteAssetException_ToString_ContainsBaseAndCustomProperties()
        {
            var exception = new RemoteAssetServerException("Server error", 500, "{\"error\": \"Internal Server Error\"}");

            var result = exception.ToString();

            Assert.Contains("[RemoteAssetFailureDetails]", result);
            Assert.Contains("StatusCode: 500", result);
            Assert.Contains("ResponseBody: {\"error\": \"Internal Server Error\"}", result);
            Assert.Contains("Server error", result);
        }

        [Fact]
        public void RemoteAssetValidationException_ToString_ContainsErrorsAndBaseProperties()
        {
            var errors = new List<string> { "asset_id is invalid", "name cannot be empty" };
            var exception = new RemoteAssetValidationException("Validation failed", errors, "{\"code\": 422}");

            var result = exception.ToString();

            Assert.Contains("[RemoteAssetValidationFailureDetails]", result);
            Assert.Contains("Errors: [asset_id is invalid, name cannot be empty]", result);
            Assert.Contains("[RemoteAssetFailureDetails]", result);
            Assert.Contains("StatusCode: 422", result);
            Assert.Contains("ResponseBody: {\"code\": 422}", result);
            Assert.Contains("Validation failed", result);
        }

        [Fact]
        public void RemoteAssetRateLimitException_ToString_ContainsRetryAfterAndBaseProperties()
        {
            var exception = new RemoteAssetRateLimitException("Rate limited", TimeSpan.FromSeconds(5), "Too Many Requests");

            var result = exception.ToString();

            Assert.Contains("[RemoteAssetRateLimitFailureDetails]", result);
            Assert.Contains("RetryAfter: 00:00:05", result);
            Assert.Contains("[RemoteAssetFailureDetails]", result);
            Assert.Contains("StatusCode: 429", result);
            Assert.Contains("ResponseBody: Too Many Requests", result);
            Assert.Contains("Rate limited", result);
        }

        [Fact]
        public void RemoteAssetTooLargeException_ToString_ContainsSubjectAndBaseProperties()
        {
            var exception = new RemoteAssetTooLargeException("Payload too large", "payload", "Payload exceeded size limit");

            var result = exception.ToString();

            Assert.Contains("[RemoteAssetTooLargeFailureDetails]", result);
            Assert.Contains("Subject: payload", result);
            Assert.Contains("[RemoteAssetFailureDetails]", result);
            Assert.Contains("StatusCode: 422", result);
            Assert.Contains("ResponseBody: Payload exceeded size limit", result);
            Assert.Contains("Payload too large", result);
        }
    }
}

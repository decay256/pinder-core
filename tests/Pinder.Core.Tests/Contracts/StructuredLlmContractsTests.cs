using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.Core.Tests.Contracts
{
    public class StructuredLlmContractsTests
    {
        [Fact]
        public void Request_exposes_the_complete_provider_neutral_contract()
        {
            var metadata = new Dictionary<string, string> { ["attempt"] = "2" };

            var request = new StructuredLlmRequest(
                "dialogue_options",
                "dialogue_options.v1",
                "{\"type\":\"object\"}",
                "system",
                "user",
                0.2,
                321,
                LlmPhase.DialogueOptions,
                metadata);

            Assert.Equal("dialogue_options", request.SchemaName);
            Assert.Equal("dialogue_options.v1", request.SchemaVersion);
            Assert.Equal("{\"type\":\"object\"}", request.JsonSchema);
            Assert.Equal("system", request.SystemPrompt);
            Assert.Equal("user", request.UserMessage);
            Assert.Equal(0.2, request.Temperature);
            Assert.Equal(321, request.MaxTokens);
            Assert.Equal(LlmPhase.DialogueOptions, request.Phase);
            Assert.Same(metadata, request.Metadata);
        }

        [Theory]
        [InlineData(true, "native_structured_output")]
        [InlineData(false, "local_validation")]
        public void Response_exposes_transport_details_and_selects_the_default_validation_mode(
            bool usedNativeStructuredOutput,
            string expectedValidationMode)
        {
            var metadata = new Dictionary<string, string> { ["finish_reason"] = "stop" };

            var response = new StructuredLlmResponse(
                "{\"ok\":true}",
                "test-provider",
                "test-model",
                usedNativeStructuredOutput,
                metadata,
                "{\"request\":true}");

            Assert.Equal("{\"ok\":true}", response.JsonText);
            Assert.Equal("test-provider", response.Provider);
            Assert.Equal("test-model", response.Model);
            Assert.Equal(usedNativeStructuredOutput, response.UsedNativeStructuredOutput);
            Assert.Same(metadata, response.Metadata);
            Assert.Equal("{\"request\":true}", response.ProviderRequestJson);
            Assert.Equal(expectedValidationMode, response.ValidationMode);
        }

        [Fact]
        public void Validation_is_reported_once_and_observer_failures_do_not_escape()
        {
            var observed = new List<StructuredLlmValidationResult>();
            var response = new StructuredLlmResponse(
                "{}",
                validationMode: "test_tool",
                validationObserver: result =>
                {
                    observed.Add(result);
                    throw new InvalidOperationException("diagnostic failure");
                });

            response.ReportValidation("rejected", "wrong_count");
            response.ReportValidation("accepted");

            var result = Assert.Single(observed);
            Assert.Equal("test_tool", result.Mode);
            Assert.Equal("rejected", result.Outcome);
            Assert.Equal("wrong_count", result.RejectionReason);
        }

        [Fact]
        public void Response_copies_preserve_metadata_and_chain_validation_to_the_original()
        {
            var originalResults = new List<StructuredLlmValidationResult>();
            var addedResults = new List<StructuredLlmValidationResult>();
            var metadata = new Dictionary<string, string> { ["provider_response_id"] = "r-1" };
            var original = new StructuredLlmResponse(
                "{\"draft\":true}",
                "provider",
                "model",
                true,
                metadata,
                "{\"request\":true}",
                "tool",
                originalResults.Add);

            var response = original
                .WithJsonText("{\"final\":true}")
                .WithValidationObserver(addedResults.Add);
            response.ReportValidation("accepted");

            Assert.Equal("{\"final\":true}", response.JsonText);
            Assert.Same(metadata, response.Metadata);
            Assert.Single(originalResults);
            Assert.Single(addedResults);
            Assert.Equal("accepted", originalResults[0].Outcome);
            Assert.Equal("accepted", addedResults[0].Outcome);
        }

        [Fact]
        public void Constructors_reject_null_required_values()
        {
            Assert.Throws<ArgumentNullException>(() => new StructuredLlmRequest(
                null!, "v1", "{}", "system", "user", 0.2, 10, "phase"));
            Assert.Throws<ArgumentNullException>(() =>
                new StructuredLlmValidationResult(null!, "accepted", null));
            Assert.Throws<ArgumentNullException>(() =>
                new StructuredLlmValidationResult("mode", null!, null));
        }
    }
}

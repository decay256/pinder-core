using System;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    /// <summary>
    /// Spec-driven tests for issue #205 — AnthropicOptions and AnthropicApiException.
    /// Tests verify behavior from docs/specs/issue-205-spec.md only.
    /// </summary>
    public class SpecAnthropicTests
    {
        #region AC3: AnthropicOptions defaults

        // What: AC3 — ApiKey defaults to empty string
        // Mutation: would catch if ApiKey defaulted to null
        [Fact]
        public void AnthropicOptions_ApiKey_DefaultsToEmptyString()
        {
            var opts = new AnthropicOptions();
            Assert.Equal("", opts.ApiKey);
        }

        // What: AC3 — Model defaults to "claude-sonnet-4-20250514"
        // Mutation: would catch if Model default was a different model string
        [Fact]
        public void AnthropicOptions_Model_DefaultsToClaudeSonnet()
        {
            var opts = new AnthropicOptions();
            Assert.Equal("claude-sonnet-4-20250514", opts.Model);
        }

        // What: AC3 — MaxTokens defaults to 1024
        // Mutation: would catch if MaxTokens defaulted to 0 or 4096
        [Fact]
        public void AnthropicOptions_MaxTokens_DefaultsTo1024()
        {
            var opts = new AnthropicOptions();
            Assert.Equal(1024, opts.MaxTokens);
        }

        // What: AC3 — Temperature defaults to 0.9
        // Mutation: would catch if Temperature defaulted to 0.0 or 1.0
        [Fact]
        public void AnthropicOptions_Temperature_DefaultsTo0Point9()
        {
            var opts = new AnthropicOptions();
            Assert.Equal(0.9, opts.Temperature);
        }

        // What: AC3 — All per-method temperature overrides default to null
        // Mutation: would catch if any override defaulted to a non-null value
        [Theory]
        [InlineData(nameof(AnthropicOptions.DialogueOptionsTemperature))]
        [InlineData(nameof(AnthropicOptions.DeliveryTemperature))]
        [InlineData(nameof(AnthropicOptions.OpponentResponseTemperature))]
        [InlineData(nameof(AnthropicOptions.InterestChangeBeatTemperature))]
        public void AnthropicOptions_PerMethodTemperatures_DefaultToNull(string propertyName)
        {
            var opts = new AnthropicOptions();
            var prop = typeof(AnthropicOptions).GetProperty(propertyName);
            Assert.NotNull(prop);
            var value = prop!.GetValue(opts);
            Assert.Null(value);
        }

        // What: AC3 — All 8 properties are settable
        // Mutation: would catch if any property was read-only
        [Fact]
        public void AnthropicOptions_AllProperties_AreSettable()
        {
            var opts = new AnthropicOptions
            {
                ApiKey = "sk-ant-test",
                Model = "claude-opus-4-20250514",
                MaxTokens = 4096,
                Temperature = 0.3,
                DialogueOptionsTemperature = 1.0,
                DeliveryTemperature = 0.5,
                OpponentResponseTemperature = 0.8,
                InterestChangeBeatTemperature = 0.6
            };

            Assert.Equal("sk-ant-test", opts.ApiKey);
            Assert.Equal("claude-opus-4-20250514", opts.Model);
            Assert.Equal(4096, opts.MaxTokens);
            Assert.Equal(0.3, opts.Temperature);
            Assert.Equal(1.0, opts.DialogueOptionsTemperature);
            Assert.Equal(0.5, opts.DeliveryTemperature);
            Assert.Equal(0.8, opts.OpponentResponseTemperature);
            Assert.Equal(0.6, opts.InterestChangeBeatTemperature);
        }

        // What: AC3 — Exactly 8 public instance properties exist
        // Mutation: would catch if a property was missing or extra ones were added
        [Fact]
        public void AnthropicOptions_Has_Exactly8_PublicProperties()
        {
            var props = typeof(AnthropicOptions).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.Equal(8, props.Length);
        }

        #endregion

        #region AC4: AnthropicApiException

        // What: AC4 — Exception inherits from System.Exception
        // Mutation: would catch if exception inherited from a different base class
        [Fact]
        public void AnthropicApiException_InheritsFrom_SystemException()
        {
            var ex = new AnthropicApiException(400, "bad");
            Assert.IsAssignableFrom<Exception>(ex);
        }

        // What: AC4 — Message format matches spec exactly
        // Mutation: would catch if message format was "Error {code}" instead of "Anthropic API error {code}: {body}"
        [Fact]
        public void AnthropicApiException_MessageFormat_MatchesSpec()
        {
            var body = "{\"error\":{\"type\":\"rate_limit_error\"}}";
            var ex = new AnthropicApiException(429, body);
            Assert.Equal($"Anthropic API error 429: {body}", ex.Message);
        }

        // What: AC4 — StatusCode is exposed as read-only
        // Mutation: would catch if StatusCode returned wrong value
        [Fact]
        public void AnthropicApiException_StatusCode_IsPreserved()
        {
            var ex = new AnthropicApiException(529, "overloaded");
            Assert.Equal(529, ex.StatusCode);
        }

        // What: AC4 — ResponseBody is exposed as read-only
        // Mutation: would catch if ResponseBody returned wrong string
        [Fact]
        public void AnthropicApiException_ResponseBody_IsPreserved()
        {
            var body = "server error details";
            var ex = new AnthropicApiException(500, body);
            Assert.Equal(body, ex.ResponseBody);
        }

        // What: Error condition — empty responseBody is accepted
        // Mutation: would catch if constructor threw on empty string
        [Fact]
        public void AnthropicApiException_AcceptsEmptyResponseBody()
        {
            var ex = new AnthropicApiException(500, "");
            Assert.Equal("", ex.ResponseBody);
            Assert.Equal("Anthropic API error 500: ", ex.Message);
        }

        // What: Error condition — various HTTP status codes work
        // Mutation: would catch if StatusCode was hardcoded
        [Theory]
        [InlineData(400, "bad request")]
        [InlineData(401, "unauthorized")]
        [InlineData(403, "forbidden")]
        [InlineData(429, "rate limited")]
        [InlineData(500, "server error")]
        [InlineData(529, "overloaded")]
        public void AnthropicApiException_HandlesVariousStatusCodes(int code, string body)
        {
            var ex = new AnthropicApiException(code, body);
            Assert.Equal(code, ex.StatusCode);
            Assert.Equal(body, ex.ResponseBody);
            Assert.Equal($"Anthropic API error {code}: {body}", ex.Message);
        }

        // What: AC4 — Exception can be caught as Exception
        // Mutation: would catch if it didn't inherit from Exception properly
        [Fact]
        public void AnthropicApiException_CanBeCaughtAsException()
        {
            Exception? caught = null;
            try
            {
                throw new AnthropicApiException(503, "unavailable");
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.NotNull(caught);
            Assert.IsType<AnthropicApiException>(caught);
        }

        #endregion
    }
}

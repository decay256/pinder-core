using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class DateePerformanceStructuredContractTests
    {
        [Theory]
        [InlineData("null", null, null)]
        [InlineData(@"{ ""stat"": ""HONESTY"", ""description"": ""asks directly whether the warmth is real"" }", StatType.Honesty, null)]
        public void ParseStrict_MapsNullAndTellOnlySignals(string tellJson, StatType? tellStat, StatType? weaknessStat)
        {
            string json = ValidJson(tellJson, "null");

            DateePerformanceStructuredResult result = DateePerformanceStructuredContract.ParseStrict(json, 4, "test", "model");

            Assert.Equal("Visible in-world message", result.Response.MessageText);
            if (tellStat.HasValue)
            {
                Assert.NotNull(result.Response.DetectedTell);
                Assert.Equal(tellStat.Value, result.Response.DetectedTell!.Stat);
                Assert.Equal("asks directly whether the warmth is real", result.Response.DetectedTell.Description);
            }
            else
            {
                Assert.Null(result.Response.DetectedTell);
            }
            Assert.Equal(weaknessStat.HasValue, result.Response.WeaknessWindow != null);
        }

        [Fact]
        public void ParseStrict_MapsWeaknessDescriptionToTrustedResultOnly()
        {
            DateePerformanceStructuredResult result = DateePerformanceStructuredContract.ParseStrict(
                ValidJson("null", @"{ ""defending_stat"": ""SELF_AWARENESS"", ""dc_reduction"": 3, ""description"": ""lets the guard drop for a second"" }"),
                5,
                null,
                null);

            Assert.Null(result.Response.DetectedTell);
            Assert.NotNull(result.Response.WeaknessWindow);
            Assert.Equal(StatType.SelfAwareness, result.Response.WeaknessWindow!.DefendingStat);
            Assert.Equal(3, result.Response.WeaknessWindow.DcReduction);
            Assert.Equal("lets the guard drop for a second", result.WeaknessDescription);
        }

        [Theory]
        [InlineData("", "empty_output")]
        [InlineData("not json", "invalid_json")]
        [InlineData(@"{""schema_version"":""wrong"",""message"":""hi"",""signals"":{""tell"":null,""weakness"":null}}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""signals"":{""tell"":null,""weakness"":null}}", "missing_message")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""   "",""signals"":{""tell"":null,""weakness"":null}}", "invalid_message")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi [SIGNALS]"",""signals"":{""tell"":null,""weakness"":null}}", "legacy_signal_marker")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi"",""extra"":true,""signals"":{""tell"":null,""weakness"":null}}", "unexpected_property")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi""}", "missing_signals")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi"",""signals"":{""weakness"":null}}", "invalid_tell")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi"",""signals"":{""tell"":null}}", "invalid_weakness")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi"",""signals"":{""tell"":{""stat"":""SelfAwareness"",""description"":""bad wire token""},""weakness"":null}}", "invalid_tell")]
        [InlineData(@"{""schema_version"":""datee_performance.v1"",""message"":""hi"",""signals"":{""tell"":null,""weakness"":{""defending_stat"":""HONESTY"",""dc_reduction"":1,""description"":""bad""}}}", "invalid_weakness")]
        public void ParseStrict_RejectsStableFailureReasons(string json, string reason)
        {
            LlmContractException ex = Assert.Throws<LlmContractException>(
                () => DateePerformanceStructuredContract.ParseStrict(json, 1, "provider", "model"));

            Assert.Equal("datee_response", ex.Phase);
            Assert.Equal(reason, ex.Reason);
            Assert.Equal(DateePerformanceStructuredContract.ParserName, ex.ParserName);
            Assert.Equal("provider", ex.Provider);
            Assert.Equal("model", ex.Model);
        }

        [Theory]
        [InlineData(" // trailing comment")]
        [InlineData(" /* trailing block comment */")]
        public void ParseStrict_RejectsJsonCommentsAfterCompleteObject(string trailingComment)
        {
            string json = ValidJson("null", "null") + trailingComment;

            LlmContractException ex = Assert.Throws<LlmContractException>(
                () => DateePerformanceStructuredContract.ParseStrict(json, 1, null, null));

            Assert.Equal("invalid_json", ex.Reason);
        }

        [Fact]
        public void ParseStrict_RejectsDuplicateProperties()
        {
            string json = @"{""schema_version"":""datee_performance.v1"",""message"":""hi"",""message"":""bye"",""signals"":{""tell"":null,""weakness"":null}}";

            LlmContractException ex = Assert.Throws<LlmContractException>(
                () => DateePerformanceStructuredContract.ParseStrict(json, 1, null, null));

            Assert.Equal("invalid_json", ex.Reason);
        }

        [Fact]
        public void CreateRequest_DeclaresStrictRequiredNullableShape()
        {
            StructuredLlmRequest request = DateePerformanceStructuredContract.CreateRequest(
                "system",
                "user",
                0.7,
                900,
                2,
                new Dictionary<string, string>());

            JObject schema = JObject.Parse(request.JsonSchema);
            Assert.Equal("datee_performance", request.SchemaName);
            Assert.Equal("datee_performance.v1", request.SchemaVersion);
            Assert.False(schema.Value<bool>("additionalProperties"));
            Assert.Contains("schema_version", schema["required"]!.ToObject<string[]>()!);
            Assert.Contains("message", schema["required"]!.ToObject<string[]>()!);
            Assert.Contains("signals", schema["required"]!.ToObject<string[]>()!);
            Assert.Contains("SELF_AWARENESS", request.JsonSchema, StringComparison.Ordinal);
            Assert.Contains("\"enum\":[2,3]", request.JsonSchema, StringComparison.Ordinal);
        }

        [Fact]
        public void ParseStrict_AcceptsEveryStatWireToken()
        {
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                string wire = StatNameNormalizer.ToWireToken(stat);
                DateePerformanceStructuredResult result = DateePerformanceStructuredContract.ParseStrict(
                    ValidJson(@"{ ""stat"": """ + wire + @""", ""description"": ""reveals something real"" }",
                        @"{ ""defending_stat"": """ + wire + @""", ""dc_reduction"": 2, ""description"": ""opens a window"" }"),
                    6,
                    null,
                    null);

                Assert.Equal(stat, result.Response.DetectedTell!.Stat);
                Assert.Equal(stat, result.Response.WeaknessWindow!.DefendingStat);
            }
        }

        [Fact]
        public void ParseStrict_StripsPersonaSelfTagsBeforeMessageValidation()
        {
            DateePerformanceStructuredResult result = DateePerformanceStructuredContract.ParseStrict(
                ValidJson("null", "null").Replace("Visible in-world message", "Visible /end) message /rant"),
                7,
                null,
                null);

            Assert.Equal("Visible) message", result.Response.MessageText);
        }

        private static string ValidJson(string tell, string weakness)
        {
            return @"{
  ""schema_version"": ""datee_performance.v1"",
  ""message"": ""Visible in-world message"",
  ""signals"": {
    ""tell"": " + tell + @",
    ""weakness"": " + weakness + @"
  }
}";
        }
    }
}

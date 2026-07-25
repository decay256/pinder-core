using System;
using System.Text.Json;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class GeneratedJsonObjectExtractorTests
    {
        [Fact]
        public void TryExtractFirstValidObject_FencedProse_ReturnsObject()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "Here is the object:\n```json\n{ \"derived_feeling\": \"left\", \"defense_reaction\": \"joke\" }\n```");

            Assert.True(result.Success);
            Assert.Equal("{ \"derived_feeling\": \"left\", \"defense_reaction\": \"joke\" }", result.Json);
        }

        [Fact]
        public void TryExtractFirstValidObject_NestedObjectsAndEscapedStringBraces_ReturnsWholeObject()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "prefix { \"outer\": { \"inner\": 1 }, \"text\": \"brace } quote \\\" slash \\\\ and { ok\" } suffix");

            Assert.True(result.Success);

            using var document = JsonDocument.Parse(result.Json!);
            Assert.Equal(1, document.RootElement.GetProperty("outer").GetProperty("inner").GetInt32());
            Assert.Equal("brace } quote \" slash \\ and { ok", document.RootElement.GetProperty("text").GetString());
        }

        [Fact]
        public void TryExtractFirstValidObject_InvalidBalancedCandidateThenValidCandidate_ReturnsValidCandidate()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "bad { nope } then { \"ok\": true }");

            Assert.True(result.Success);
            Assert.Equal("{ \"ok\": true }", result.Json);
            Assert.Equal(18, result.CandidateStartIndex);
            Assert.Equal(32, result.CandidateEndIndexExclusive);
        }

        [Fact]
        public void TryExtractFirstValidObject_RootArrayWithNestedObject_IsRejected()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "[{ \"derived_feeling\": \"left\", \"defense_reaction\": \"joke\" }]");

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.NoValidObject, result.FailureCode);
            Assert.Null(result.Json);
        }

        [Fact]
        public void TryExtractFirstValidObject_ProseWrappedRootArrayWithNestedObject_IsRejected()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "Here is the result: [{ \"derived_feeling\": \"left\", \"defense_reaction\": \"joke\" }]");

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.NoValidObject, result.FailureCode);
            Assert.Null(result.Json);
        }

        [Fact]
        public void TryExtractFirstValidObject_FencedRootArrayWithNestedObject_IsRejected()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "```json\n[{ \"derived_feeling\": \"left\", \"defense_reaction\": \"joke\" }]\n```");

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.NoValidObject, result.FailureCode);
            Assert.Null(result.Json);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryExtractFirstValidObject_EmptyInput_ReturnsEmptyFailure(string? input)
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(input);

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.EmptyInput, result.FailureCode);
        }

        [Fact]
        public void TryExtractFirstValidObject_NoObject_ReturnsNoObjectFailure()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject("plain narrative only");

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.NoObject, result.FailureCode);
        }

        [Fact]
        public void TryExtractFirstValidObject_UnterminatedObject_ReturnsUnterminatedFailure()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject("{ \"a\": { \"b\": 1 }");

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.UnterminatedObject, result.FailureCode);
        }

        [Fact]
        public void TryExtractFirstValidObject_InputTooLarge_ReturnsInputTooLargeFailure()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "12345",
                new GeneratedJsonObjectExtractionOptions(maxInputChars: 4, maxObjectChars: 100));

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.InputTooLarge, result.FailureCode);
        }

        [Fact]
        public void TryExtractFirstValidObject_ObjectTooLarge_ReturnsObjectTooLargeFailure()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject(
                "{ \"long\": true }",
                new GeneratedJsonObjectExtractionOptions(maxInputChars: 100, maxObjectChars: 5));

            Assert.False(result.Success);
            Assert.Equal(GeneratedJsonObjectExtractionFailureCode.ObjectTooLarge, result.FailureCode);
        }

        [Theory]
        [InlineData(0, 10, "maxInputChars")]
        [InlineData(10, 0, "maxObjectChars")]
        public void Constructor_InvalidOptions_Throws(int maxInputChars, int maxObjectChars, string parameterName)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => new GeneratedJsonObjectExtractionOptions(maxInputChars, maxObjectChars));

            Assert.Equal(parameterName, ex.ParamName);
        }

        [Fact]
        public void TryExtractFirstValidObject_ExposesCandidateOffsets()
        {
            var result = GeneratedJsonObjectExtractor.TryExtractFirstValidObject("aa {\"x\":1} zz");

            Assert.True(result.Success);
            Assert.Equal(3, result.CandidateStartIndex);
            Assert.Equal(10, result.CandidateEndIndexExclusive);
        }
    }
}

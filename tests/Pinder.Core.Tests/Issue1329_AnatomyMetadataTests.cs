using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public sealed class Issue1329_AnatomyMetadataTests
    {
        [Fact]
        public void BundledFile_All25ParametersExposeMetadata()
        {
            var repo = new JsonAnatomyRepository(LoadBundledAnatomyJson());
            var all = repo.GetAll().ToList();

            Assert.Equal(25, all.Count);

            foreach (var parameter in all)
            {
                Assert.NotNull(parameter.Metadata);
                Assert.False(string.IsNullOrWhiteSpace(parameter.Metadata!.Group));
                Assert.False(string.IsNullOrWhiteSpace(parameter.Metadata.Section));
                Assert.False(string.IsNullOrWhiteSpace(parameter.Metadata.LabelKey));
                Assert.False(string.IsNullOrWhiteSpace(parameter.Metadata.ControlType));
                Assert.InRange(parameter.Metadata.NormalizedMin, 0f, 1f);
                Assert.InRange(parameter.Metadata.NormalizedMax, 0f, 1f);
                Assert.InRange(parameter.Metadata.NormalizedDefault,
                    parameter.Metadata.NormalizedMin,
                    parameter.Metadata.NormalizedMax);
                Assert.True(parameter.Metadata.NormalizedStep > 0f);
                Assert.True(parameter.Metadata.DisplayOrder > 0);
            }
        }

        [Fact]
        public void GetAll_ReturnsParametersByDisplayOrder()
        {
            string json = "[" +
                ParameterJson("second", displayOrder: 20) + "," +
                ParameterJson("first", displayOrder: 10) +
                "]";

            var ids = new JsonAnatomyRepository(json).GetAll()
                .Select(parameter => parameter.Id)
                .ToList();

            Assert.Equal(new[] { "first", "second" }, ids);
        }

        [Fact]
        public void AnatomyParameterDefinition_MetadataSerializesForApiConsumers()
        {
            var repo = new JsonAnatomyRepository(LoadBundledAnatomyJson());
            var parameter = repo.GetParameter("trunkLengthBase");

            string json = JsonSerializer.Serialize(parameter);

            Assert.Contains(@"""Metadata"":", json);
            Assert.Contains(@"""Group"":""trunk""", json);
            Assert.Contains(@"""DisplayOrder"":10", json);
        }

        [Fact]
        public void Loader_RejectsDuplicateParameterIds()
        {
            string json = "[" +
                ParameterJson("duplicate", displayOrder: 10) + "," +
                ParameterJson("duplicate", displayOrder: 20) +
                "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("Duplicate anatomy parameter id 'duplicate'", ex.Message);
        }

        [Fact]
        public void Loader_RejectsDuplicateDisplayOrders()
        {
            string json = "[" +
                ParameterJson("one", displayOrder: 10) + "," +
                ParameterJson("two", displayOrder: 10) +
                "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("Duplicate anatomy metadata.display_order '10'", ex.Message);
            Assert.Contains("two", ex.Message);
        }

        [Fact]
        public void Loader_RejectsMissingMetadataObject()
        {
            const string json = @"[{
  ""id"": ""missing-metadata"",
  ""name"": ""Missing Metadata"",
  ""bands"": []
}]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("missing-metadata", ex.Message);
            Assert.Contains("field 'metadata'", ex.Message);
            Assert.Contains("required", ex.Message);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("42")]
        [InlineData(@"""not-an-object""")]
        [InlineData("[]")]
        public void Loader_RejectsNonObjectParameterEntryWithIndex(string invalidEntry)
        {
            string json = "[" + ParameterJson("valid") + "," + invalidEntry + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Equal(
                "Invalid anatomy parameter entry at index 1: expected a JSON object.",
                ex.Message);
        }

        [Theory]
        [InlineData(@"""control_type"": ""dial""", "metadata.control_type")]
        [InlineData(@"""normalized_min"": -0.01", "metadata.normalized_min")]
        [InlineData(@"""normalized_max"": 1.01", "metadata.normalized_max")]
        [InlineData(@"""normalized_default"": 1.01", "metadata.normalized_default")]
        [InlineData(@"""normalized_step"": 0", "metadata.normalized_step")]
        [InlineData(@"""display_order"": 0", "metadata.display_order")]
        public void Loader_RejectsInvalidMetadataFields(string replacement, string expectedField)
        {
            string json = "[" + ParameterJson("broken").Replace(
                ReplacementTarget(replacement),
                replacement,
                StringComparison.Ordinal) + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("broken", ex.Message);
            Assert.Contains(expectedField, ex.Message);
        }

        private static string ReplacementTarget(string replacement)
        {
            if (replacement.Contains("control_type", StringComparison.Ordinal))
                return @"""control_type"": ""slider""";
            if (replacement.Contains("normalized_min", StringComparison.Ordinal))
                return @"""normalized_min"": 0";
            if (replacement.Contains("normalized_max", StringComparison.Ordinal))
                return @"""normalized_max"": 1";
            if (replacement.Contains("normalized_default", StringComparison.Ordinal))
                return @"""normalized_default"": 0.5";
            if (replacement.Contains("normalized_step", StringComparison.Ordinal))
                return @"""normalized_step"": 0.01";
            return @"""display_order"": 10";
        }

        private static string ParameterJson(
            string id,
            int displayOrder = 10,
            string controlType = "slider")
            => @"{
  ""id"": """ + id + @""",
  ""name"": """ + id + @" Parameter"",
  ""metadata"": {
    ""group"": ""test"",
    ""section"": ""test"",
    ""label_key"": ""anatomy." + id + @".label"",
    ""control_type"": """ + controlType + @""",
    ""normalized_min"": 0,
    ""normalized_max"": 1,
    ""normalized_default"": 0.5,
    ""normalized_step"": 0.01,
    ""display_order"": " + displayOrder + @"
  },
  ""bands"": [
    {
      ""lower"": 0,
      ""upper"": 1,
      ""summary_text"": """ + id + @" summary""
    }
  ]
}";

        private static string LoadBundledAnatomyJson()
            => File.ReadAllText(Path.Combine(
                TestRepoLocator.RepoRoot,
                "data",
                "anatomy",
                "anatomy-parameters.json"));
    }
}

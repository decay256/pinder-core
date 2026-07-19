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

        [Theory]
        [InlineData("null")]
        [InlineData("42")]
        [InlineData(@"""not-an-object""")]
        [InlineData("[]")]
        public void Loader_RejectsNonObjectBandEntryWithParameterIdAndIndex(string invalidBand)
        {
            string json = "[" + ParameterJson(
                "broken-band",
                bandsJson: @"[
    {
      ""lower"": 0,
      ""upper"": 0.5,
      ""summary_text"": ""valid first band""
    },
    " + invalidBand + @"
  ]") + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("broken-band", ex.Message);
            Assert.Contains("band at index 1", ex.Message);
            Assert.Contains("expected a JSON object", ex.Message);
        }

        [Theory]
        [InlineData(@"{ ""upper"": 1, ""summary_text"": ""missing lower"" }", "lower")]
        [InlineData(@"{ ""lower"": 0, ""summary_text"": ""missing upper"" }", "upper")]
        [InlineData(@"{ ""lower"": ""zero"", ""upper"": 1, ""summary_text"": ""non-numeric lower"" }", "lower")]
        [InlineData(@"{ ""lower"": 0, ""upper"": null, ""summary_text"": ""non-numeric upper"" }", "upper")]
        public void Loader_RejectsMissingOrNonNumericBandBounds(string bandJson, string expectedField)
        {
            string json = "[" + ParameterJson("broken-bounds", bandsJson: "[" + bandJson + "]") + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("broken-bounds", ex.Message);
            Assert.Contains("band 0", ex.Message);
            Assert.Contains(expectedField, ex.Message);
            Assert.Contains("finite number", ex.Message);
        }

        [Fact]
        public void Loader_RejectsNonFiniteBandBound()
        {
            string json = "[" + ParameterJson(
                "overflow-bound",
                bandsJson: @"[
    {
      ""lower"": 0,
      ""upper"": 1e100,
      ""summary_text"": ""overflow upper""
    }
  ]") + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("overflow-bound", ex.Message);
            Assert.Contains("band 0", ex.Message);
            Assert.Contains("upper", ex.Message);
            Assert.Contains("must be finite", ex.Message);
        }

        [Theory]
        [InlineData(@"{ ""lower"": -0.01, ""upper"": 1, ""summary_text"": ""negative lower"" }", "lower", "within [0, 1]")]
        [InlineData(@"{ ""lower"": 0, ""upper"": 1.01, ""summary_text"": ""large upper"" }", "upper", "within [0, 1]")]
        [InlineData(@"{ ""lower"": 0.75, ""upper"": 0.5, ""summary_text"": ""inverted"" }", "upper", "greater than lower")]
        public void Loader_RejectsInvalidBandBounds(
            string bandJson,
            string expectedField,
            string expectedReason)
        {
            string json = "[" + ParameterJson("invalid-bounds", bandsJson: "[" + bandJson + "]") + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("invalid-bounds", ex.Message);
            Assert.Contains("band 0", ex.Message);
            Assert.Contains(expectedField, ex.Message);
            Assert.Contains(expectedReason, ex.Message);
        }

        [Theory]
        [InlineData(@"[
    {
      ""lower"": 0.1,
      ""upper"": 1,
      ""summary_text"": ""missing first slice""
    }
  ]", "band 0", "start at 0")]
        [InlineData(@"[
    {
      ""lower"": 0,
      ""upper"": 0.4,
      ""summary_text"": ""first""
    },
    {
      ""lower"": 0.6,
      ""upper"": 1,
      ""summary_text"": ""gap""
    }
  ]", "band 1", "previous band upper")]
        [InlineData(@"[
    {
      ""lower"": 0,
      ""upper"": 0.5,
      ""summary_text"": ""first""
    },
    {
      ""lower"": 0.5,
      ""upper"": 0.9,
      ""summary_text"": ""missing tail""
    }
  ]", "band 1", "end at 1")]
        public void Loader_RejectsBandCoverageGaps(
            string bandsJson,
            string expectedBand,
            string expectedReason)
        {
            string json = "[" + ParameterJson("coverage-gap", bandsJson: bandsJson) + "]";

            var ex = Assert.Throws<FormatException>(() => new JsonAnatomyRepository(json));

            Assert.Contains("coverage-gap", ex.Message);
            Assert.Contains(expectedBand, ex.Message);
            Assert.Contains(expectedReason, ex.Message);
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
            string controlType = "slider",
            string? bandsJson = null)
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
  ""bands"": " + (bandsJson ?? @"[
    {
      ""lower"": 0,
      ""upper"": 1,
      ""summary_text"": """ + id + @" summary""
    }
  ]") + @"
}";

        private static string LoadBundledAnatomyJson()
            => File.ReadAllText(Path.Combine(
                TestRepoLocator.RepoRoot,
                "data",
                "anatomy",
                "anatomy-parameters.json"));
    }
}

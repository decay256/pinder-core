using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #1175 — scalar-band anatomy model.
    ///
    /// Tests for <see cref="AnatomyParameterDefinition"/> and
    /// <see cref="AnatomyBandDefinition"/>: the new scalar-band schema
    /// replaces the old categorical/numeric tier model.
    ///
    /// Previously this file covered AnatomyScaleType (ScaleTypeCategorical /
    /// ScaleTypeNumeric / NumericRange / NumericBreakpoint). Those concepts
    /// are deleted; this file now verifies band-based parsing and resolution.
    /// </summary>
    [Trait("Category", "Characters")]
    public class AnatomyScaleTypeTests
    {
        // ----- new scalar-band shape ------

        private const string MinimalBandJson = @"
[
  {
    ""id"": ""trunkLengthBase"",
    ""name"": ""Trunk Length Base"",
    ""bands"": [
      { ""lower"": 0.00, ""upper"": 0.05 },
      { ""lower"": 0.05, ""upper"": 0.20 },
      { ""lower"": 0.20, ""upper"": 0.50 },
      { ""lower"": 0.50, ""upper"": 0.70 },
      { ""lower"": 0.70, ""upper"": 0.95 },
      { ""lower"": 0.95, ""upper"": 1.00 }
    ]
  }
]";

        [Fact]
        public void BandSchema_ParsesCorrectly()
        {
            var repo = new JsonAnatomyRepository(MinimalBandJson);
            var p = repo.GetParameter("trunkLengthBase");
            Assert.NotNull(p);
            Assert.Equal(6, p!.Bands.Count);
            Assert.InRange(p.Bands[0].Lower, 0.00f - 0.0001f, 0.00f + 0.0001f);
            Assert.InRange(p.Bands[0].Upper, 0.05f - 0.0001f, 0.05f + 0.0001f);
            Assert.InRange(p.Bands[5].Lower, 0.95f - 0.0001f, 0.95f + 0.0001f);
            Assert.InRange(p.Bands[5].Upper, 1.00f - 0.0001f, 1.00f + 0.0001f);
        }

        [Fact]
        public void BandSchema_EmptyBands_ParsesOkay()
        {
            const string json = @"[{ ""id"": ""test"", ""name"": ""Test"", ""bands"": [] }]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("test");
            Assert.NotNull(p);
            Assert.Empty(p!.Bands);
        }

        [Fact]
        public void BandSchema_WithFragments_ParsesFragments()
        {
            const string json = @"
[{
  ""id"": ""trunkGirth"",
  ""name"": ""Trunk Girth"",
  ""bands"": [
    {
      ""lower"": 0.00, ""upper"": 0.20,
      ""personality_fragment"": ""slim and witty"",
      ""backstory_fragment"": ""a history"",
      ""texting_style_fragment"": ""SYNTAX:\n- emoji: none\nTONE:\n- stance (dry): flat"",
      ""archetype_tendencies"": [""The Sniper""],
      ""stat_modifiers"": { ""wit"": 1 },
      ""response_timing_modifier"": {
        ""base_delay_delta_minutes"": -3,
        ""delay_variance_multiplier"": 0.9,
        ""dry_spell_probability_delta"": 0.0,
        ""read_receipt"": ""neutral""
      }
    },
    { ""lower"": 0.20, ""upper"": 1.00 }
  ]
}]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("trunkGirth");
            Assert.NotNull(p);
            Assert.Equal(2, p!.Bands.Count);
            var b0 = p.Bands[0];
            Assert.Equal("slim and witty", b0.PersonalityFragment);
            Assert.Equal("a history", b0.BackstoryFragment);
            Assert.NotNull(b0.TextingStyleFragment);
            Assert.Single(b0.ArchetypeTendencies);
            Assert.Equal("The Sniper", b0.ArchetypeTendencies[0]);
            Assert.Equal(1, b0.StatModifiers[Pinder.Core.Stats.StatType.Wit]);
            Assert.Equal(-3, b0.ResponseTimingModifier.BaseDelayDeltaMinutes);
            Assert.Null(p.Bands[1].PersonalityFragment);
        }

        // ----- ResolveBand ------

        [Theory]
        [InlineData(0.00f, 0)] // lower=0.00 → first band
        [InlineData(0.04f, 0)] // just under first boundary
        [InlineData(0.05f, 1)] // exactly at second band lower
        [InlineData(0.19f, 1)]
        [InlineData(0.20f, 2)]
        [InlineData(0.49f, 2)]
        [InlineData(0.50f, 3)]
        [InlineData(0.69f, 3)]
        [InlineData(0.70f, 4)]
        [InlineData(0.94f, 4)]
        [InlineData(0.95f, 5)]
        [InlineData(1.00f, 5)] // exactly at 1.0 → last band
        public void ResolveBand_StandardThresholds_ReturnsCorrectBand(float value, int expectedBandIndex)
        {
            var repo = new JsonAnatomyRepository(MinimalBandJson);
            var p = repo.GetParameter("trunkLengthBase")!;
            var band = p.ResolveBand(value);
            Assert.NotNull(band);
            Assert.Equal(p.Bands[expectedBandIndex], band);
        }

        [Fact]
        public void ResolveBand_ValueAbove1_ClampsToLastBand()
        {
            var repo = new JsonAnatomyRepository(MinimalBandJson);
            var p = repo.GetParameter("trunkLengthBase")!;
            var band = p.ResolveBand(1.5f);
            Assert.Equal(p.Bands[p.Bands.Count - 1], band);
        }

        [Fact]
        public void ResolveBand_ValueBelow0_ClampsToFirstBand()
        {
            var repo = new JsonAnatomyRepository(MinimalBandJson);
            var p = repo.GetParameter("trunkLengthBase")!;
            var band = p.ResolveBand(-0.1f);
            Assert.Equal(p.Bands[0], band);
        }

        [Fact]
        public void ResolveBand_EmptyBands_ReturnsNull()
        {
            const string json = @"[{ ""id"": ""test"", ""name"": ""Test"", ""bands"": [] }]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("test")!;
            Assert.Null(p.ResolveBand(0.5f));
        }

        // ----- Bool param (isCircumcised) — 2 bands at 0.5 ------

        [Theory]
        [InlineData(0.0f, 0)] // false → band 0
        [InlineData(0.49f, 0)]
        [InlineData(0.5f, 1)]  // true → band 1
        [InlineData(1.0f, 1)]
        public void ResolveBand_BoolParam_TwoBands(float value, int expectedBandIndex)
        {
            const string json = @"
[{
  ""id"": ""isCircumcised"",
  ""name"": ""Circumcision"",
  ""bands"": [
    { ""lower"": 0.00, ""upper"": 0.50, ""personality_fragment"": ""uncircumcised"" },
    { ""lower"": 0.50, ""upper"": 1.00, ""personality_fragment"": ""circumcised"" }
  ]
}]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("isCircumcised")!;
            var band = p.ResolveBand(value);
            Assert.NotNull(band);
            Assert.Equal(p.Bands[expectedBandIndex], band);
        }

        // ----- Bipolar param (trunkCurvature) — neutral midpoint ------

        [Fact]
        public void ResolveBand_Bipolar_NeutralStraight_MidBand()
        {
            // trunkCurvature=0 (straight) → (0+100)/200 = 0.5 → band index 3 (0.50–0.70)
            var repo = new JsonAnatomyRepository(MinimalBandJson); // re-uses standard thresholds
            var p = repo.GetParameter("trunkLengthBase")!;
            // Simulate curvature 0 → normalised 0.5
            float normalised = (0f + 100f) / 200f; // = 0.5
            var band = p.ResolveBand(normalised);
            Assert.NotNull(band);
            Assert.InRange(band!.Lower, 0.50f - 0.0001f, 0.50f + 0.0001f);
            Assert.InRange(band.Upper, 0.70f - 0.0001f, 0.70f + 0.0001f);
        }

        [Fact]
        public void ResolveBand_Bipolar_MaxLeft_FirstBand()
        {
            // trunkCurvature=-100 → (−100+100)/200 = 0 → band 0
            var repo = new JsonAnatomyRepository(MinimalBandJson);
            var p = repo.GetParameter("trunkLengthBase")!;
            float normalised = (-100f + 100f) / 200f; // = 0.0
            var band = p.ResolveBand(normalised);
            Assert.Equal(p.Bands[0], band);
        }

        [Fact]
        public void ResolveBand_Bipolar_MaxRight_LastBand()
        {
            // trunkCurvature=100 → (100+100)/200 = 1 → band 5
            var repo = new JsonAnatomyRepository(MinimalBandJson);
            var p = repo.GetParameter("trunkLengthBase")!;
            float normalised = (100f + 100f) / 200f; // = 1.0
            var band = p.ResolveBand(normalised);
            Assert.Equal(p.Bands[p.Bands.Count - 1], band);
        }

        // ----- Guard: no old Tier/ScaleType/NumericRange API on the type ------

        [Fact]
        public void AnatomyParameterDefinition_NoBandsProperty_IsNotTiers()
        {
            // Guards that the old 'Tiers', 'ScaleType', 'NumericRange' properties
            // are gone. If they were present this would be a compile error, but
            // we document intent here.
            var type = typeof(AnatomyParameterDefinition);
            Assert.Null(type.GetProperty("Tiers"));
            Assert.Null(type.GetProperty("ScaleType"));
            Assert.Null(type.GetProperty("NumericRange"));
            Assert.Null(type.GetMethod("GetTier"));
        }

        [Fact]
        public void NumericRangeSpec_TypeDoesNotExist()
        {
            // Guard: NumericRangeSpec was removed in #1175 — it should not
            // appear in the Pinder.Core assembly.
            var asm = typeof(AnatomyParameterDefinition).Assembly;
            var nrs = asm.GetType("Pinder.Core.Characters.NumericRangeSpec");
            Assert.Null(nrs);
        }

        [Fact]
        public void AnatomyTierDefinition_TypeDoesNotExist()
        {
            // Guard: AnatomyTierDefinition was removed in #1175.
            var asm = typeof(AnatomyParameterDefinition).Assembly;
            var atd = asm.GetType("Pinder.Core.Characters.AnatomyTierDefinition");
            Assert.Null(atd);
        }

        // ----- bundled file: correct number of params and band coverage ------

        [Fact]
        public void BundledFile_AllParams_HaveAtLeastOneBand()
        {
            var json = LoadBundledAnatomyJson();
            var repo = new JsonAnatomyRepository(json);
            var all  = repo.GetAll().ToList();

            Assert.True(all.Count >= 24,
                $"Expected at least 24 anatomy params, got {all.Count}");

            foreach (var param in all)
                Assert.NotEmpty(param.Bands);
        }

        [Fact]
        public void BundledFile_IsCircumcised_HasTwoBands()
        {
            var json = LoadBundledAnatomyJson();
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("isCircumcised");
            Assert.NotNull(p);
            Assert.Equal(2, p!.Bands.Count);
            Assert.InRange(p.Bands[0].Lower, 0.00f - 0.0001f, 0.00f + 0.0001f);
            Assert.InRange(p.Bands[0].Upper, 0.50f - 0.0001f, 0.50f + 0.0001f);
            Assert.InRange(p.Bands[1].Lower, 0.50f - 0.0001f, 0.50f + 0.0001f);
            Assert.InRange(p.Bands[1].Upper, 1.00f - 0.0001f, 1.00f + 0.0001f);
        }

        [Fact]
        public void BundledFile_TrunkCurvature_SixBands()
        {
            var json = LoadBundledAnatomyJson();
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("trunkCurvature");
            Assert.NotNull(p);
            Assert.Equal(6, p!.Bands.Count);
        }

        // ----- helper ------

        private static string LoadBundledAnatomyJson()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "data", "anatomy", "anatomy-parameters.json");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new FileNotFoundException(
                "Could not locate data/anatomy/anatomy-parameters.json " +
                "by walking up from " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}

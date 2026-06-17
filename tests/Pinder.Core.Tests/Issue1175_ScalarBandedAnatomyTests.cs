using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Prompts;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for issue #1175: scalar-banded anatomy mirroring
    /// Unity CharacterData. These tests lock the normalization pipeline
    /// (CharacterDataDto → CharacterDataNormalizer → float anatomy dict →
    /// CharacterAssembler band resolution → assembled fragments/stats).
    /// </summary>
    [Trait("Category", "Characters")]
    [Collection("StaticWiring")]
    public class Issue1175_ScalarBandedAnatomyTests
    {
        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        private static IItemRepository LoadItemRepo()
        {
            string json = File.ReadAllText(Path.Combine(RepoRoot, "data", "items", "starter-items.json"));
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository LoadAnatomyRepo()
        {
            string json = File.ReadAllText(Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            return new JsonAnatomyRepository(json);
        }

        private static readonly IReadOnlyDictionary<StatType, int> ZeroBaseStats =
            new Dictionary<StatType, int>();

        private static readonly IReadOnlyDictionary<ShadowStatType, int> ZeroShadow =
            new Dictionary<ShadowStatType, int>();

        // ----------------------------------------------------------------
        // COMMITTED INTEGRATION TEST: real Unity default CharacterData
        // values flow through the entire pipeline and produce expected
        // band fragments in the assembled prompt.
        // ----------------------------------------------------------------

        /// <summary>
        /// A CharacterDataDto with default Unity field values (matching
        /// CharacterData.cs at tip c0d45c5) is normalised, assembled, and
        /// the resulting personality fragments contain expected content
        /// from the bands that those defaults resolve to.
        /// </summary>
        [Fact]
        public void IntegrationTest_DefaultUnityCharacterData_AssemblesExpectedFragments()
        {
            var dto = new CharacterDataDto
            {
                TrunkLengthBase = 50f,
                TrunkLengthMid  = 50f,
                TrunkLengthTip  = 50f,
                TrunkGirth      = 50f,
                TrunkCurvature  = 0f,    // bipolar → 0.5 → neutral/straight band
                GlansScale      = 50f,
                GlansWidth      = 50f,
                ScrotumScale    = 50f,
                LeftTesticleScale  = 50f,
                RightTesticleScale = 50f,
                ScrotumDrop     = 50f,
                Prepucius       = 0f,
                Arrugatis       = 0f,
                Gravitatis      = 0f,
                Venicus         = 0f,
                Sad             = 0f,   // 0/100 → 0.0 → band 0
                Happy           = 0f,
                Serius          = 0f,
                SkinColorR      = 0.87f,
                SkinColorG      = 0.72f,
                SkinColorB      = 0.63f,
                Freckles        = 0f,
                Blemishes       = 0f,
                Veins           = 30f,  // 30/100 = 0.30 → band 2 (0.20-0.50)
                IsCircumcised   = false  // false → 0.0 → band 0 (uncircumcised)
            };

            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.True(anatomy.ContainsKey("trunkCurvature"),    "trunkCurvature must be present");
            Assert.True(anatomy.ContainsKey("isCircumcised"),     "isCircumcised must be present");
            Assert.True(anatomy.ContainsKey("skinHue"),           "skinHue must be present");
            Assert.True(anatomy.ContainsKey("skinSat"),           "skinSat must be present");
            Assert.True(anatomy.ContainsKey("skinVal"),           "skinVal must be present");
            Assert.True(anatomy.ContainsKey("veins"),             "veins must be present");

            // trunkCurvature 0 → (0+100)/200 = 0.5 → band 3 (0.50-0.70)
            float curv = anatomy["trunkCurvature"];
            Assert.Equal(0.5f, curv, precision: 5);

            // isCircumcised false → 0.0
            Assert.Equal(0.0f, anatomy["isCircumcised"], precision: 5);

            // Assemble through the full pipeline
            var assembler = new CharacterAssembler(LoadItemRepo(), LoadAnatomyRepo());
            var fragments = assembler.Assemble(
                Array.Empty<string>(),
                anatomy,
                ZeroBaseStats, ZeroShadow);

            // trunkCurvature 0.5 → band 3 personality = "essentially straight"
            Assert.True(
                fragments.PersonalityFragments.Any(f =>
                    f.Contains("essentially straight", StringComparison.OrdinalIgnoreCase)),
                "trunkCurvature at 0.5 should resolve to 'essentially straight' band");

            // isCircumcised 0.0 → band 0 (< 0.5 = uncircumcised)
            // personality = "nothing hidden; the full picture is always present"
            Assert.True(
                fragments.PersonalityFragments.Any(f =>
                    f.Contains("nothing hidden", StringComparison.OrdinalIgnoreCase)),
                "isCircumcised=false should resolve to uncircumcised band personality");

            // veins = 0.30 → band 2 (0.20-0.50) → personality fragment
            Assert.True(
                fragments.PersonalityFragments.Any(f =>
                    f.Contains("veins defined but subtle", StringComparison.OrdinalIgnoreCase)),
                "veins=0.30 should resolve to band 2 personality");

            // The assembled system prompt must contain PERSONALITY section
            var prompt = PromptBuilder.BuildSystemPrompt(
                "TestChar", "he/him", "test bio", fragments, new TrapState());
            Assert.Contains("PERSONALITY", prompt);
        }

        // ----------------------------------------------------------------
        // BIPOLAR: trunkCurvature normalisation
        // ----------------------------------------------------------------

        [Fact]
        public void Bipolar_TrunkCurvature_Zero_NormalizesToHalf()
        {
            var dto = new CharacterDataDto { TrunkCurvature = 0f };
            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.Equal(0.5f, anatomy["trunkCurvature"], precision: 5);
        }

        [Fact]
        public void Bipolar_TrunkCurvature_MinusFull_NormalizesToZero()
        {
            var dto = new CharacterDataDto { TrunkCurvature = -100f };
            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.Equal(0.0f, anatomy["trunkCurvature"], precision: 5);
        }

        [Fact]
        public void Bipolar_TrunkCurvature_PlusFull_NormalizesToOne()
        {
            var dto = new CharacterDataDto { TrunkCurvature = 100f };
            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.Equal(1.0f, anatomy["trunkCurvature"], precision: 5);
        }

        [Fact]
        public void Bipolar_TrunkCurvature_ResolvesBandsAtExtremes()
        {
            var anatomyRepo = LoadAnatomyRepo();
            var param = anatomyRepo.GetParameter("trunkCurvature");
            Assert.NotNull(param);

            // -100 → 0.0 → band 0 (0.00-0.05)
            var bandMin = param!.ResolveBand(0.0f);
            Assert.NotNull(bandMin);
            Assert.Equal(0.0f, bandMin!.Lower, precision: 5);

            // +100 → 1.0 → last band (0.95-1.00)
            var bandMax = param.ResolveBand(1.0f);
            Assert.NotNull(bandMax);
            Assert.Equal(0.95f, bandMax!.Lower, precision: 5);

            // 0 → 0.5 → band 3 (0.50-0.70)
            var bandMid = param.ResolveBand(0.5f);
            Assert.NotNull(bandMid);
            Assert.Equal(0.50f, bandMid!.Lower, precision: 5);
            Assert.Equal(0.70f, bandMid.Upper, precision: 5);
        }

        // ----------------------------------------------------------------
        // BOOL: isCircumcised
        // ----------------------------------------------------------------

        [Fact]
        public void Bool_IsCircumcised_False_NormalizesToZero()
        {
            var dto = new CharacterDataDto { IsCircumcised = false };
            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.Equal(0.0f, anatomy["isCircumcised"], precision: 5);
        }

        [Fact]
        public void Bool_IsCircumcised_True_NormalizesToOne()
        {
            var dto = new CharacterDataDto { IsCircumcised = true };
            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.Equal(1.0f, anatomy["isCircumcised"], precision: 5);
        }

        [Fact]
        public void Bool_IsCircumcised_ResolvesBands()
        {
            var anatomyRepo = LoadAnatomyRepo();
            var param = anatomyRepo.GetParameter("isCircumcised");
            Assert.NotNull(param);

            // false (0.0) → band 0: lower=0.0, upper=0.5 (uncircumcised band)
            var bandFalse = param!.ResolveBand(0.0f);
            Assert.NotNull(bandFalse);
            Assert.Equal(0.0f, bandFalse!.Lower, precision: 5);
            // isCircumcised has 2 bands; uncircumcised upper should be 0.5
            Assert.Equal(0.50f, bandFalse.Upper, precision: 5);

            // true (1.0) → last band: lower=0.5 (circumcised band)
            var bandTrue = param.ResolveBand(1.0f);
            Assert.NotNull(bandTrue);
            Assert.Equal(0.50f, bandTrue!.Lower, precision: 5);
        }

        // ----------------------------------------------------------------
        // HSV: skinColor RGB → skinHue / skinSat / skinVal
        // ----------------------------------------------------------------

        [Fact]
        public void Hsv_SkinColor_DefaultRgb_ProducesThreeScalarsInRange()
        {
            // Unity default: Color(0.87f, 0.72f, 0.63f)
            var dto = new CharacterDataDto
            {
                SkinColorR = 0.87f,
                SkinColorG = 0.72f,
                SkinColorB = 0.63f,
            };

            var anatomy = CharacterDataNormalizer.Normalize(dto);

            Assert.True(anatomy.ContainsKey("skinHue"), "skinHue must be produced");
            Assert.True(anatomy.ContainsKey("skinSat"), "skinSat must be produced");
            Assert.True(anatomy.ContainsKey("skinVal"), "skinVal must be produced");

            Assert.InRange(anatomy["skinHue"], 0f, 1f);
            Assert.InRange(anatomy["skinSat"], 0f, 1f);
            Assert.InRange(anatomy["skinVal"], 0f, 1f);
        }

        [Fact]
        public void Hsv_SkinColor_DefaultRgb_CorrectHsvValues()
        {
            // Expected: H≈0.0625, S≈0.2759, V=0.87 (computed from RGB→HSV)
            // Using precision=2 to accommodate float rounding differences.
            var dto = new CharacterDataDto
            {
                SkinColorR = 0.87f,
                SkinColorG = 0.72f,
                SkinColorB = 0.63f,
            };

            var anatomy = CharacterDataNormalizer.Normalize(dto);

            // H ≈ 0.0625 (orange hue, ~22.5°)
            Assert.InRange(anatomy["skinHue"], 0.060f, 0.070f);
            // S ≈ 0.2759
            Assert.InRange(anatomy["skinSat"], 0.270f, 0.285f);
            // V = 0.87
            Assert.Equal(0.87f, anatomy["skinVal"], precision: 2);
        }

        [Fact]
        public void Hsv_SkinColor_DefaultRgb_EachChannelResolvesBand()
        {
            var dto = new CharacterDataDto
            {
                SkinColorR = 0.87f,
                SkinColorG = 0.72f,
                SkinColorB = 0.63f,
            };

            var anatomy = CharacterDataNormalizer.Normalize(dto);
            var anatomyRepo = LoadAnatomyRepo();

            // skinHue ≈ 0.0625 → band 1 (0.05-0.20)
            var hueParam = anatomyRepo.GetParameter("skinHue");
            Assert.NotNull(hueParam);
            var hueBand = hueParam!.ResolveBand(anatomy["skinHue"]);
            Assert.NotNull(hueBand);
            Assert.InRange(anatomy["skinHue"], hueBand!.Lower, hueBand.Upper);

            // skinSat ≈ 0.2759 → band 2 (0.20-0.50)
            var satParam = anatomyRepo.GetParameter("skinSat");
            Assert.NotNull(satParam);
            var satBand = satParam!.ResolveBand(anatomy["skinSat"]);
            Assert.NotNull(satBand);
            Assert.InRange(anatomy["skinSat"], satBand!.Lower, satBand.Upper);

            // skinVal = 0.87 → band 4 (0.70-0.95)
            var valParam = anatomyRepo.GetParameter("skinVal");
            Assert.NotNull(valParam);
            var valBand = valParam!.ResolveBand(anatomy["skinVal"]);
            Assert.NotNull(valBand);
            Assert.InRange(anatomy["skinVal"], valBand!.Lower, valBand.Upper);
        }

        // ----------------------------------------------------------------
        // SCHEMA-VERSION ROUND-TRIP
        // ----------------------------------------------------------------

        [Fact]
        public void SchemaVersion_RoundTrip_WriterLoaderPreservesV2()
        {
            // Build a minimal v2 CharacterDefinition with float anatomy
            var anatomy = new Dictionary<string, float>
            {
                { "trunkLengthBase", 0.5f },
                { "trunkGirth",      0.3f },
                { "isCircumcised",   0.0f },
            };

            var spent = new Dictionary<StatType, int>
            {
                { StatType.Charm,         2 },
                { StatType.Rizz,          1 },
                { StatType.Honesty,       0 },
                { StatType.Chaos,         0 },
                { StatType.Wit,           1 },
                { StatType.SelfAwareness, 0 },
            };
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness,       0 },
                { ShadowStatType.Despair,       0 },
                { ShadowStatType.Denial,        0 },
                { ShadowStatType.Fixation,      0 },
                { ShadowStatType.Dread,         0 },
                { ShadowStatType.Overthinking,  0 },
            };

            var original = new CharacterDefinition(
                schemaVersion:  CharacterDefinition.CurrentSchemaVersion,
                characterId:    new Guid("550e8400-e29b-41d4-a716-446655440001"),
                name:           "RoundTripChar",
                genderIdentity: "they/them",
                bio:            "round-trip test",
                level:          1,
                items:          new List<string>().AsReadOnly(),
                anatomy:        anatomy,
                allocation:     new AllocationBlock(spent, 0, shadows));

            // Serialize
            string json = CharacterDefinitionWriter.Write(original);

            // Deserialize
            var roundTripped = CharacterDefinitionLoader.ParseDefinition(json);

            Assert.Equal(CharacterDefinition.CurrentSchemaVersion, roundTripped.SchemaVersion);
            Assert.Equal(2, roundTripped.SchemaVersion);
            Assert.Equal(original.CharacterId, roundTripped.CharacterId);
            Assert.Equal(original.Name,        roundTripped.Name);

            Assert.Equal(3, roundTripped.Anatomy.Count);
            Assert.Equal(0.5f, roundTripped.Anatomy["trunkLengthBase"], precision: 5);
            Assert.Equal(0.3f, roundTripped.Anatomy["trunkGirth"],      precision: 5);
            Assert.Equal(0.0f, roundTripped.Anatomy["isCircumcised"],   precision: 5);
        }

        // ----------------------------------------------------------------
        // GUARD: no legacy tier/range types survive in the assembly path
        // ----------------------------------------------------------------

        [Fact]
        public void Guard_NoLegacyTierOrRangeTypesInAssembly()
        {
            // Reflection: Pinder.Core assembly must NOT contain any type
            // named AnatomyTierDefinition or NumericRangeSpec. These were
            // removed in issue #1175 and must not creep back.
            var assembly = typeof(CharacterAssembler).Assembly;

            var legacyTypes = assembly.GetTypes()
                .Where(t => t.Name == "AnatomyTierDefinition" ||
                            t.Name == "NumericRangeSpec")
                .Select(t => t.FullName)
                .ToList();

            Assert.True(legacyTypes.Count == 0,
                "Pinder.Core must not contain legacy anatomy types: " +
                string.Join(", ", legacyTypes));
        }
    }
}

using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #551 — admin-content-editor sprint, Phase 2a.
    ///
    /// Schema-refactor tests for <see cref="AnatomyParameterDefinition"/> and
    /// <see cref="AnatomyTierDefinition"/>: the loader gains <c>scale_type</c>,
    /// <c>numeric_range</c>, and per-tier <c>numeric_breakpoint</c> support
    /// without breaking the legacy categorical-only file shape.
    /// </summary>
    [Trait("Category", "Characters")]
    public class AnatomyScaleTypeTests
    {
        // ----- legacy categorical-only shape (pre-#551 file format) ------

        private const string LegacyCategoricalOnlyJson = @"
[
  {
    ""id"": ""eye_style"",
    ""name"": ""Eye Style"",
    ""tiers"": [
      {
        ""id"": ""soft"",
        ""name"": ""Soft"",
        ""stat_modifiers"": { ""charm"": 1 },
        ""personality_fragment"": ""warm"",
        ""backstory_fragment"": ""b"",
        ""texting_style_fragment"": ""s"",
        ""archetype_tendencies"": [""The Bio Responder""],
        ""response_timing_modifier"": {
          ""base_delay_delta_minutes"": 0,
          ""delay_variance_multiplier"": 1.0,
          ""dry_spell_probability_delta"": 0.0,
          ""read_receipt"": ""neutral""
        }
      }
    ]
  }
]";

        [Fact]
        public void LegacyFile_NoScaleType_ParsesAsCategorical()
        {
            var repo = new JsonAnatomyRepository(LegacyCategoricalOnlyJson);
            var p = repo.GetParameter("eye_style");
            Assert.NotNull(p);
            Assert.Equal(AnatomyParameterDefinition.ScaleTypeCategorical, p!.ScaleType);
            Assert.Null(p.NumericRange);
            Assert.Single(p.Tiers);
            Assert.Null(p.Tiers[0].NumericBreakpoint);
        }

        [Fact]
        public void LegacyFile_ExplicitCategorical_ParsesAsCategorical()
        {
            const string json = @"
[
  {
    ""id"": ""tattoos"",
    ""name"": ""Tattoos"",
    ""scale_type"": ""categorical"",
    ""tiers"": [
      {
        ""id"": ""none"",
        ""name"": ""None"",
        ""stat_modifiers"": {},
        ""personality_fragment"": ""p"",
        ""backstory_fragment"": ""b"",
        ""texting_style_fragment"": ""s"",
        ""archetype_tendencies"": [],
        ""response_timing_modifier"": {
          ""base_delay_delta_minutes"": 0,
          ""delay_variance_multiplier"": 1.0,
          ""dry_spell_probability_delta"": 0.0,
          ""read_receipt"": ""neutral""
        }
      }
    ]
  }
]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("tattoos");
            Assert.NotNull(p);
            Assert.Equal(AnatomyParameterDefinition.ScaleTypeCategorical, p!.ScaleType);
            Assert.Null(p.NumericRange);
            Assert.Null(p.Tiers[0].NumericBreakpoint);
        }

        // ----- new numeric shape ------

        [Fact]
        public void NumericParameter_RoundTripsBreakpointAndRange()
        {
            const string json = @"
[
  {
    ""id"": ""length"",
    ""name"": ""Length"",
    ""scale_type"": ""numeric"",
    ""numeric_range"": { ""min"": 1, ""max"": 4, ""unit"": ""tier"" },
    ""tiers"": [
      {
        ""id"": ""short"",
        ""name"": ""Short"",
        ""numeric_breakpoint"": 1,
        ""stat_modifiers"": { ""self_awareness"": 1 },
        ""personality_fragment"": ""p"",
        ""backstory_fragment"": ""b"",
        ""texting_style_fragment"": ""s"",
        ""archetype_tendencies"": [""The Sniper""],
        ""response_timing_modifier"": {
          ""base_delay_delta_minutes"": -5,
          ""delay_variance_multiplier"": 0.8,
          ""dry_spell_probability_delta"": 0.0,
          ""read_receipt"": ""neutral""
        }
      },
      {
        ""id"": ""legendary"",
        ""name"": ""Legendary"",
        ""numeric_breakpoint"": 4,
        ""stat_modifiers"": {},
        ""personality_fragment"": ""p"",
        ""backstory_fragment"": ""b"",
        ""texting_style_fragment"": ""s"",
        ""archetype_tendencies"": [],
        ""response_timing_modifier"": {
          ""base_delay_delta_minutes"": 15,
          ""delay_variance_multiplier"": 1.5,
          ""dry_spell_probability_delta"": 0.1,
          ""read_receipt"": ""hides""
        }
      }
    ]
  }
]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("length");

            Assert.NotNull(p);
            Assert.Equal(AnatomyParameterDefinition.ScaleTypeNumeric, p!.ScaleType);
            Assert.NotNull(p.NumericRange);
            Assert.Equal(1, p.NumericRange!.Min);
            Assert.Equal(4, p.NumericRange.Max);
            Assert.Equal("tier", p.NumericRange.Unit);

            Assert.Equal(2, p.Tiers.Count);
            Assert.Equal(1, p.Tiers[0].NumericBreakpoint);
            Assert.Equal(4, p.Tiers[1].NumericBreakpoint);
        }

        [Fact]
        public void NumericParameter_MissingNumericRange_LeavesItNull()
        {
            // Defensive: file with scale_type=numeric but no numeric_range
            // shouldn't blow up; the editor surfaces the missing range and
            // lets the admin fill it in.
            const string json = @"
[
  {
    ""id"": ""length"",
    ""name"": ""Length"",
    ""scale_type"": ""numeric"",
    ""tiers"": []
  }
]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("length");
            Assert.NotNull(p);
            Assert.Equal(AnatomyParameterDefinition.ScaleTypeNumeric, p!.ScaleType);
            Assert.Null(p.NumericRange);
        }

        [Fact]
        public void CategoricalParameter_BreakpointFieldIgnored()
        {
            // If a future hand-edit accidentally leaves a breakpoint on a
            // categorical tier, we drop it on parse rather than surface it.
            // This keeps the (parameter scale_type, tier breakpoint) invariant
            // tight even when the file is in a transient inconsistent state.
            const string json = @"
[
  {
    ""id"": ""eye_style"",
    ""name"": ""Eye Style"",
    ""scale_type"": ""categorical"",
    ""tiers"": [
      {
        ""id"": ""soft"",
        ""name"": ""Soft"",
        ""numeric_breakpoint"": 1,
        ""stat_modifiers"": {},
        ""personality_fragment"": ""p"",
        ""backstory_fragment"": ""b"",
        ""texting_style_fragment"": ""s"",
        ""archetype_tendencies"": [],
        ""response_timing_modifier"": {
          ""base_delay_delta_minutes"": 0,
          ""delay_variance_multiplier"": 1.0,
          ""dry_spell_probability_delta"": 0.0,
          ""read_receipt"": ""neutral""
        }
      }
    ]
  }
]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("eye_style");
            Assert.NotNull(p);
            Assert.Null(p!.Tiers[0].NumericBreakpoint);
        }

        // ----- Skin-Tone-shaped (visual_description only) tier in a numeric param ------

        [Fact]
        public void VisualOnlyTier_InNumericParam_StillRoundTripsBreakpoint()
        {
            // Edge case: a visual-only tier (no personality_fragment) inside
            // a numeric parameter still picks up its numeric_breakpoint.
            // None of the bundled file's numeric params currently use this
            // shape, but the parser should support it for symmetry.
            const string json = @"
[
  {
    ""id"": ""length"",
    ""name"": ""Length"",
    ""scale_type"": ""numeric"",
    ""numeric_range"": { ""min"": 1, ""max"": 2, ""unit"": ""tier"" },
    ""tiers"": [
      { ""id"": ""mini"", ""name"": ""Mini"", ""numeric_breakpoint"": 1, ""visual_description"": ""tiny"" },
      { ""id"": ""mega"", ""name"": ""Mega"", ""numeric_breakpoint"": 2, ""visual_description"": ""huge"" }
    ]
  }
]";
            var repo = new JsonAnatomyRepository(json);
            var p = repo.GetParameter("length");
            Assert.NotNull(p);
            Assert.Equal(2, p!.Tiers.Count);
            Assert.Equal(1, p.Tiers[0].NumericBreakpoint);
            Assert.Equal("tiny", p.Tiers[0].VisualDescription);
            Assert.Equal(2, p.Tiers[1].NumericBreakpoint);
        }

        // ----- bundled file: round-trip the 9 existing parameters ------

        [Fact]
        public void BundledFile_NineParameters_ScaleTypesAreCorrect()
        {
            var json = LoadBundledAnatomyJson();
            var repo = new JsonAnatomyRepository(json);
            var all  = repo.GetAll().ToList();

            Assert.Equal(9, all.Count);

            // The three numeric params per the sprint spec.
            foreach (var pid in new[] { "length", "girth", "ball_size" })
            {
                var p = repo.GetParameter(pid);
                Assert.NotNull(p);
                Assert.Equal(AnatomyParameterDefinition.ScaleTypeNumeric, p!.ScaleType);
                Assert.NotNull(p.NumericRange);
                // Every tier in a numeric param has a breakpoint set.
                Assert.All(p.Tiers, t => Assert.NotNull(t.NumericBreakpoint));
            }

            // The six categorical params per the sprint spec.
            foreach (var pid in new[] {
                "circumcision", "vein_definition", "skin_texture",
                "skin_tone", "tattoos", "eye_style" })
            {
                var p = repo.GetParameter(pid);
                Assert.NotNull(p);
                Assert.Equal(AnatomyParameterDefinition.ScaleTypeCategorical, p!.ScaleType);
                Assert.Null(p.NumericRange);
                Assert.All(p.Tiers, t => Assert.Null(t.NumericBreakpoint));
            }
        }

        [Fact]
        public void BundledFile_NumericParameters_BreakpointsAreOrderedOneThroughFour()
        {
            var json = LoadBundledAnatomyJson();
            var repo = new JsonAnatomyRepository(json);

            // The migration tagged each numeric param's tiers in the order
            // they appear in the file with breakpoints 1..N.
            foreach (var pid in new[] { "length", "girth", "ball_size" })
            {
                var p = repo.GetParameter(pid);
                Assert.NotNull(p);
                var bps = p!.Tiers.Select(t => t.NumericBreakpoint).ToList();
                Assert.Equal(new int?[] { 1, 2, 3, 4 }, bps);
            }
        }

        // ----- helper -----------------------------------------------------

        private static string LoadBundledAnatomyJson()
        {
            // Walk up from the test bin dir to the repo root that contains
            // data/anatomy/anatomy-parameters.json. Works in both the canonical
            // pinder-core clone AND a `git worktree`-based checkout (where
            // .git is a file rather than a directory — see the
            // GIT-WORKTREE-DOTGIT-IS-A-FILE canonical lesson).
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

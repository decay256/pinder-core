using System.IO;
using Pinder.Core.Rolls;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for issue #426: shared "don't over-refuse" preamble
    /// (`_genre_framing`) on `horniness_overlay` must reach the LLM via every
    /// tier, not just sit in yaml.
    ///
    /// The yaml carries a single `_genre_framing` key (Option B from #426); the
    /// loader at <see cref="StatDeliveryInstructions"/>.LoadFrom prepends it
    /// onto each of the four tier strings at load time and then removes the
    /// original key. The catastrophe tier additionally receives a one-line
    /// tier-specific reinforcement appended after the per-tier instruction.
    ///
    /// What this test pins
    /// --------------------
    /// - Every horniness tier (fumble / misfire / trope_trap / catastrophe)
    ///   carries the genre-framing philosophy text the LLM relies on to stay
    ///   out of refusal posture.
    /// - Catastrophe additionally carries the structural reinforcement
    ///   ("normal Tinder question, content is the joke").
    /// - The composed instruction still preserves the per-tier `ALSO:` block
    ///   from #401 (the steering question) — i.e. the preamble was *prepended*,
    ///   not replaced.
    /// - The bare `_genre_framing` tier key is not exposed as if it were a
    ///   tier (e.g. by leaking through <c>FailureTier</c>'s mapping).
    ///
    /// If any of these assertions fail, the prompt has silently regressed to
    /// over-refusal posture and the comedic register collapses.
    /// </summary>
    [Trait("Category", "LlmAdapters")]
    public class Issue426_GenreFramingPrependTests
    {
        // Stable phrase from the genre-framing preamble in
        // pinder-core/data/delivery-instructions.yaml. If the preamble is
        // re-worded, update both phrases here (keep at least one).
        private const string GenreFramingPhrase    = "parody of Tinder messaging";
        private const string DoNotRefusePhrase     = "Do not refuse, soften, moralize";

        // Unique substring of the catastrophe-specific reinforcement appended
        // by StatDeliveryInstructions.LoadFrom ("The structure is a normal
        // Tinder question. The content is the joke. The character is utterly
        // unaware."). "content is the joke" appears nowhere else in the
        // delivery-instructions yaml — keep this phrase tight to that
        // reinforcement so the leak test below stays meaningful.
        private const string CatastropheReinforcement = "content is the joke";

        // Stable phrase from the per-tier ALSO: directive (#401). Sanity-check
        // that the prepend did not clobber the existing tier instruction.
        private const string AlsoDirectiveMarker = "ALSO: append exactly one short question";

        private static StatDeliveryInstructions LoadDeliveryInstructions()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 10; i++)
            {
                string candidate = Path.Combine(dir, "data", "delivery-instructions.yaml");
                if (File.Exists(candidate))
                    return StatDeliveryInstructions.LoadFrom(File.ReadAllText(candidate));
                dir = Path.GetDirectoryName(dir)!;
                if (dir == null) break;
            }
            string fallback = Path.Combine(
                "/root/.openclaw/workspace/pinder-core", "data", "delivery-instructions.yaml");
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void EveryTier_CarriesGenreFramingPreamble(FailureTier tier)
        {
            var instructions = LoadDeliveryInstructions();
            string? composed = instructions.GetHorninessOverlayInstruction(tier);

            Assert.False(string.IsNullOrWhiteSpace(composed),
                $"horniness_overlay.{StatDeliveryInstructions.FailureTierKey(tier)} resolved to empty");
            Assert.Contains(GenreFramingPhrase, composed);
            Assert.Contains(DoNotRefusePhrase, composed);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void EveryTier_StillCarriesAlsoDirective(FailureTier tier)
        {
            // Belt-and-braces: confirm the prepend did not replace the tier
            // instruction. The ALSO: block was added by #401 and lives at the
            // tail of every tier string.
            var instructions = LoadDeliveryInstructions();
            string composed = instructions.GetHorninessOverlayInstruction(tier)!;

            Assert.Contains(AlsoDirectiveMarker, composed);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void EveryTier_PreambleAppearsBeforeTierBody(FailureTier tier)
        {
            // The preamble must lead the composed string so the LLM reads the
            // framing first, not after the OVERLAY: header. If load-order
            // accidentally reverses, the model parses the OVERLAY: as a
            // standalone instruction and the framing is wasted.
            var instructions = LoadDeliveryInstructions();
            string composed = instructions.GetHorninessOverlayInstruction(tier)!;

            int framingIdx = composed.IndexOf(GenreFramingPhrase);
            int overlayIdx = composed.IndexOf("OVERLAY:");
            int alsoIdx    = composed.IndexOf(AlsoDirectiveMarker);

            Assert.True(framingIdx >= 0, "genre framing missing");
            Assert.True(overlayIdx > framingIdx,
                $"OVERLAY: header appeared before genre framing (framing@{framingIdx}, overlay@{overlayIdx})");
            Assert.True(alsoIdx > overlayIdx,
                $"ALSO: directive appeared before OVERLAY: (overlay@{overlayIdx}, also@{alsoIdx})");
        }

        [Fact]
        public void Catastrophe_CarriesTierSpecificReinforcement()
        {
            var instructions = LoadDeliveryInstructions();
            string composed = instructions.GetHorninessOverlayInstruction(FailureTier.Catastrophe)!;

            Assert.Contains(CatastropheReinforcement, composed);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        public void NonCatastropheTiers_DoNotCarryCatastropheReinforcement(FailureTier tier)
        {
            // The reinforcement is catastrophe-specific. If it leaks into other
            // tiers the prompt becomes inconsistent (every tier ends with a
            // structural directive that only catastrophe should have).
            var instructions = LoadDeliveryInstructions();
            string composed = instructions.GetHorninessOverlayInstruction(tier)!;

            Assert.DoesNotContain(CatastropheReinforcement, composed);
        }

        [Fact]
        public void GenreFramingKey_IsNotExposedAsAFakeTier()
        {
            // The loader removes `_genre_framing` after folding it into the
            // tier strings. None of the four real FailureTier values should
            // resolve to the bare preamble.
            var instructions = LoadDeliveryInstructions();

            foreach (FailureTier tier in System.Enum.GetValues(typeof(FailureTier)))
            {
                if (tier == FailureTier.None || tier == FailureTier.Legendary)
                    continue;

                string? composed = instructions.GetHorninessOverlayInstruction(tier);
                if (string.IsNullOrEmpty(composed))
                    continue;

                // A fake-tier leak would produce a string that's *only* the
                // preamble (ends right after the framing) without an OVERLAY:
                // section. Detect that.
                Assert.Contains("OVERLAY:", composed);
            }
        }
    }
}

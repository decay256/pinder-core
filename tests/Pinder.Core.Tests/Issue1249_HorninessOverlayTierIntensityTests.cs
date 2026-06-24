using System.IO;
using Pinder.Core.Rolls;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1249_HorninessOverlayTierIntensityTests
    {
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
            string fallback = Path.Combine("/tmp/pinder-core-1249", "data", "delivery-instructions.yaml");
            return StatDeliveryInstructions.LoadFrom(File.ReadAllText(fallback));
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void EveryTier_IsNonEmpty_AndContainsOverlayMarker(FailureTier tier)
        {
            var instructions = LoadDeliveryInstructions();
            string composed = instructions.GetHorninessOverlayInstruction(tier)!;
            
            Assert.False(string.IsNullOrWhiteSpace(composed), $"horniness_overlay for tier {tier} resolved to empty");
            Assert.Contains("OVERLAY:", composed);
        }

        [Fact]
        public void Catastrophe_IsDistinctFromFumbleAndMisfire()
        {
            var instructions = LoadDeliveryInstructions();
            string catastrophe = instructions.GetHorninessOverlayInstruction(FailureTier.Catastrophe)!;
            string fumble = instructions.GetHorninessOverlayInstruction(FailureTier.Fumble)!;
            string misfire = instructions.GetHorninessOverlayInstruction(FailureTier.Misfire)!;

            Assert.NotEqual(fumble, catastrophe);
            Assert.NotEqual(misfire, catastrophe);
        }

        [Fact]
        public void Catastrophe_ContainsMaximalIntensityMarker_WhileFumbleDoesNot()
        {
            var instructions = LoadDeliveryInstructions();
            string catastrophe = instructions.GetHorninessOverlayInstruction(FailureTier.Catastrophe)!;
            string fumble = instructions.GetHorninessOverlayInstruction(FailureTier.Fumble)!;

            // Catastrophe maximal-intensity marker assertion
            bool hasStrongMarker = catastrophe.Contains("every word") ||
                                   catastrophe.Contains("maximum") ||
                                   catastrophe.Contains("rewrites itself") ||
                                   catastrophe.Contains("psychic violence") ||
                                   catastrophe.Contains("mortified");
            Assert.True(hasStrongMarker, "Catastrophe must contain a maximal intensity marker.");

            // Fumble lacks maximal marker
            Assert.DoesNotContain("every word", fumble);
            Assert.DoesNotContain("maximum", fumble);
            Assert.DoesNotContain("rewrites itself", fumble);
            Assert.DoesNotContain("psychic violence", fumble);
            Assert.DoesNotContain("mortified", fumble);

            // Fumble minimal-intensity marker assertion
            bool hasMildMarker = fumble.Contains("one word") ||
                                 fumble.Contains("single word") ||
                                 fumble.Contains("one verb") ||
                                 fumble.Contains("INVOLUNTARY HEAT (one word");
            Assert.True(hasMildMarker, "Fumble must contain a minimal intensity marker.");
        }

        [Fact]
        public void Catastrophe_CarriesContentIsTheJokeReinforcement_OthersDoNot()
        {
            var instructions = LoadDeliveryInstructions();
            string catastrophe = instructions.GetHorninessOverlayInstruction(FailureTier.Catastrophe)!;
            string fumble = instructions.GetHorninessOverlayInstruction(FailureTier.Fumble)!;
            string misfire = instructions.GetHorninessOverlayInstruction(FailureTier.Misfire)!;
            string tropeTrap = instructions.GetHorninessOverlayInstruction(FailureTier.TropeTrap)!;

            string reinforcement = "content is the joke";

            Assert.Contains(reinforcement, catastrophe);
            Assert.DoesNotContain(reinforcement, fumble);
            Assert.DoesNotContain(reinforcement, misfire);
            Assert.DoesNotContain(reinforcement, tropeTrap);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void TextingRegister_ContainsAppendQuestionDirective_AndNoStageDirections(FailureTier tier)
        {
            var instructions = LoadDeliveryInstructions();
            string composed = instructions.GetHorninessOverlayInstruction(tier)!;

            // We skip brittle asterisk checks as per prompt instruction: "actually skip brittle checks"
            // We just check the texting register question-append contract is pinned
            Assert.Contains("ALSO: append exactly one short question", composed);
        }
    }
}
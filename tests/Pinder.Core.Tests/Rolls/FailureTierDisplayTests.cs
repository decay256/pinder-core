using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests
{
    public class FailureTierDisplayTests
    {
        [Fact]
        public void TropeTrap_OnOptionRoll_LabelIsTropeTrap()
            => Assert.Equal("TropeTrap", FailureTierDisplay.Label(FailureTier.TropeTrap, RollCheckKind.OptionRoll));

        [Theory]
        [InlineData(RollCheckKind.Horniness)]
        [InlineData(RollCheckKind.Shadow)]
        [InlineData(RollCheckKind.ShadowGrowth)]
        [InlineData(RollCheckKind.Steering)]
        public void TropeTrap_OnNonOptionKinds_LabelIsSevere(RollCheckKind kind)
            => Assert.Equal("Severe", FailureTierDisplay.Label(FailureTier.TropeTrap, kind));

        [Theory]
        [InlineData(FailureTier.None)]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void NonTropeTrapTiers_LabelPassesThrough(FailureTier tier)
        {
            Assert.Equal(tier.ToString(), FailureTierDisplay.Label(tier, RollCheckKind.OptionRoll));
            Assert.Equal(tier.ToString(), FailureTierDisplay.Label(tier, RollCheckKind.Horniness));
            Assert.Equal(tier.ToString(), FailureTierDisplay.Label(tier, RollCheckKind.Shadow));
        }

        [Fact]
        public void Enum_Identity_Unchanged()
        {
            // Guard against accidental enum mutation. Wire/YAML rely on these names.
            Assert.Equal("TropeTrap", FailureTier.TropeTrap.ToString());
            Assert.Equal("Fumble", FailureTier.Fumble.ToString());
            Assert.Equal("Catastrophe", FailureTier.Catastrophe.ToString());
        }
    }
}

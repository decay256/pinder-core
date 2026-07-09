using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.Core.Tests
{
    public class FailureTierDisplayTests
    {
        [Fact]
        public void TropeTrap_OnOptionRoll_UsesTropeTrapDisplayKey()
            => Assert.Equal(
                "display_names.failure_tier.trope_trap",
                FailureTierDisplay.DisplayNameKey(FailureTier.TropeTrap, RollCheckKind.OptionRoll));

        [Theory]
        [InlineData(RollCheckKind.Horniness)]
        [InlineData(RollCheckKind.Shadow)]
        [InlineData(RollCheckKind.ShadowGrowth)]
        [InlineData(RollCheckKind.Steering)]
        public void TropeTrap_OnNonOptionKinds_UsesSevereDisplayKey(RollCheckKind kind)
            => Assert.Equal(
                "display_names.failure_tier.severe",
                FailureTierDisplay.DisplayNameKey(FailureTier.TropeTrap, kind));

        [Theory]
        [InlineData(FailureTier.Success)]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void NonTropeTrapTiers_UseSameDisplayKeyRegardlessOfKind(FailureTier tier)
        {
            var expected = FailureTierDisplay.DisplayNameKey(tier, RollCheckKind.OptionRoll);
            Assert.Equal(expected, FailureTierDisplay.DisplayNameKey(tier, RollCheckKind.Horniness));
            Assert.Equal(expected, FailureTierDisplay.DisplayNameKey(tier, RollCheckKind.Shadow));
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

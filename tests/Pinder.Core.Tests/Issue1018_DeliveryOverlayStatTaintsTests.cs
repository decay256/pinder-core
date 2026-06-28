using System;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1018_DeliveryOverlayStatTaintsTests
    {
        [Theory]
        [InlineData(StatType.Rizz, FailureTier.Catastrophe, "ruining this", "no hope")]
        [InlineData(StatType.Rizz, FailureTier.Legendary, "ruining this", "no hope")]
        public void Apply_WithRizzAndSevereFailure_ContainsDespairTheme(StatType stat, FailureTier tier, string keyword1, string keyword2)
        {
            var result = DeliveryOverlay.Apply("Just trying to say hello.", tier, 15, stat);
            Assert.True(result.Contains(keyword1) || result.Contains(keyword2),
                $"Expected Despair-themed prefix for Rizz, but got: {result}");
        }

        [Theory]
        [InlineData(StatType.SelfAwareness, FailureTier.Catastrophe, "(I know how", "(This is a weird thing")]
        [InlineData(StatType.SelfAwareness, FailureTier.Legendary, "(I know how", "(This is a weird thing")]
        [InlineData(StatType.Wit, FailureTier.Catastrophe, "(I know how", "(This is a weird thing")]
        [InlineData(StatType.Wit, FailureTier.Legendary, "(I know how", "(This is a weird thing")]
        public void Apply_WithSelfAwarenessOrWitAndSevereFailure_ContainsOverthinkingTheme(StatType stat, FailureTier tier, string keyword1, string keyword2)
        {
            var result = DeliveryOverlay.Apply("Just trying to say hello.", tier, 15, stat);
            Assert.True(result.Contains(keyword1) || result.Contains(keyword2),
                $"Expected Overthinking-themed prefix for {stat}, but got: {result}");
        }

        [Theory]
        [InlineData(StatType.Charm, FailureTier.Catastrophe, "perfect", "obsess")]
        [InlineData(StatType.Charm, FailureTier.Legendary, "perfect", "obsess")]
        public void Apply_WithCharmAndSevereFailure_ContainsFixationTheme(StatType stat, FailureTier tier, string keyword1, string keyword2)
        {
            var result = DeliveryOverlay.Apply("Just trying to say hello.", tier, 15, stat);
            Assert.True(result.Contains(keyword1) || result.Contains(keyword2),
                $"Expected Fixation-themed prefix for Charm, but got: {result}");
        }

        [Theory]
        [InlineData(StatType.Chaos, FailureTier.Catastrophe, "burn", "madness")]
        [InlineData(StatType.Chaos, FailureTier.Legendary, "burn", "madness")]
        public void Apply_WithChaosAndSevereFailure_ContainsMadnessTheme(StatType stat, FailureTier tier, string keyword1, string keyword2)
        {
            var result = DeliveryOverlay.Apply("Just trying to say hello.", tier, 15, stat);
            Assert.True(result.Contains(keyword1) || result.Contains(keyword2),
                $"Expected Madness-themed prefix for Chaos, but got: {result}");
        }

        [Theory]
        [InlineData(StatType.Honesty, FailureTier.Catastrophe, "dread", "terrified")]
        [InlineData(StatType.Honesty, FailureTier.Legendary, "dread", "terrified")]
        public void Apply_WithHonestyAndSevereFailure_ContainsDreadTheme(StatType stat, FailureTier tier, string keyword1, string keyword2)
        {
            var result = DeliveryOverlay.Apply("Just trying to say hello.", tier, 15, stat);
            Assert.True(result.Contains(keyword1) || result.Contains(keyword2),
                $"Expected Dread-themed prefix for Honesty, but got: {result}");
        }

        [Theory]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.SelfAwareness)]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.Honesty)]
        public void Apply_Success_CommitsVerbatimRegardlessOfStat(StatType stat)
        {
            var input = "This is a perfect delivery.";
            var result = DeliveryOverlay.Apply(input, FailureTier.Success, 0, stat);
            Assert.Equal(input, result);
        }
    }
}

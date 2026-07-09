using System.Collections.Generic;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue943_RollTierOnSuccessTests
    {
        [Fact]
        public void ResolveFixedDC_SuccessfulRoll_HasSuccessTierOnResultAndCheck()
        {
            var result = ResolveFixedDc(roll: 15, dc: 10);

            Assert.True(result.IsSuccess);
            Assert.Equal(FailureTier.Success, result.Tier);
            Assert.Equal(FailureTier.Success, result.Check.Tier);
            Assert.Equal(0, result.MissMargin);
            Assert.Equal(0, result.Check.MissMargin);
        }

        [Theory]
        [InlineData(9, 10, FailureTier.Fumble, 1)]
        [InlineData(5, 10, FailureTier.Misfire, 5)]
        [InlineData(1, 10, FailureTier.Legendary, 9)]
        public void ResolveFixedDC_FailedRoll_UsesRollEngineTierLogic(
            int roll,
            int dc,
            FailureTier expectedTier,
            int expectedMissMargin)
        {
            var result = ResolveFixedDc(roll, dc);

            Assert.False(result.IsSuccess);
            Assert.Equal(expectedTier, result.Tier);
            Assert.Equal(expectedMissMargin, result.MissMargin);
            Assert.Equal(FailureTierLadder.FromMissMargin(expectedMissMargin), result.Check.Tier);
            Assert.Equal(expectedMissMargin, result.Check.MissMargin);
        }

        [Fact]
        public void EngineResolvedSuccessfulRoll_ExposesSuccessTierForHostDtos()
        {
            var result = ResolveFixedDc(roll: 15, dc: 10);

            Assert.Equal(FailureTier.Success, result.Tier);
            Assert.Equal(nameof(FailureTier.Success), result.Tier.ToString());
        }

        private static RollResult ResolveFixedDc(int roll, int dc)
        {
            return RollEngine.ResolveFixedDC(
                StatType.Charm,
                MakeStats(),
                dc,
                new TrapState(),
                1,
                new NullTrapRegistry(),
                new FixedDice(roll));
        }

        private static StatBlock MakeStats()
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, 0 },
                { StatType.Rizz, 0 },
                { StatType.Honesty, 0 },
                { StatType.Chaos, 0 },
                { StatType.Wit, 0 },
                { StatType.SelfAwareness, 0 },
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 },
            };
            return new StatBlock(baseStats, shadowStats);
        }
    }
}

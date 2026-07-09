using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Issue924
{
    /// <summary>
    /// Issue #924: <see cref="RollResult"/> must expose consistent enum values
    /// for host-layer DTOs to project into their own wire shape.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue924_EnumSerializationShapeTests
    {
        private static RollResult MakeResult()
        {
            return new RollResult(
                dieRoll: 14,
                secondDieRoll: null,
                usedDieRoll: 14,
                stat: StatType.Wit,
                statModifier: 3,
                levelBonus: 1,
                dc: 12,
                tier: FailureTier.Success,
                activatedTrap: null,
                externalBonus: 0,
                defendingStat: StatType.Charm);
        }

        private static RollResult MakeMissResult()
        {
            return new RollResult(
                dieRoll: 2,
                secondDieRoll: null,
                usedDieRoll: 2,
                stat: StatType.Chaos,
                statModifier: 0,
                levelBonus: 0,
                dc: 25,
                tier: FailureTier.Catastrophe,
                activatedTrap: null,
                externalBonus: 0,
                defendingStat: StatType.Honesty);
        }

        [Fact]
        public void Result_preserves_attacking_stat()
        {
            Assert.Equal(StatType.Wit, MakeResult().Stat);
        }

        [Fact]
        public void Result_preserves_defending_stat()
        {
            Assert.Equal(StatType.Charm, MakeResult().DefendingStat);
        }

        [Fact]
        public void Result_preserves_failure_tier()
        {
            Assert.Equal(FailureTier.Catastrophe, MakeMissResult().Tier);
        }

        [Fact]
        public void Result_computes_risk_tier_from_need()
        {
            Assert.Equal(RiskTier.Reckless, MakeMissResult().RiskTier);
        }

        [Fact]
        public void ComputeRiskTier_thresholds_are_stable()
        {
            Assert.Equal(RiskTier.Safe, RollResult.ComputeRiskTier(dc: 7, statModifier: 0, levelBonus: 0));
            Assert.Equal(RiskTier.Medium, RollResult.ComputeRiskTier(dc: 8, statModifier: 0, levelBonus: 0));
            Assert.Equal(RiskTier.Hard, RollResult.ComputeRiskTier(dc: 12, statModifier: 0, levelBonus: 0));
            Assert.Equal(RiskTier.Bold, RollResult.ComputeRiskTier(dc: 16, statModifier: 0, levelBonus: 0));
            Assert.Equal(RiskTier.Reckless, RollResult.ComputeRiskTier(dc: 20, statModifier: 0, levelBonus: 0));
        }
    }
}

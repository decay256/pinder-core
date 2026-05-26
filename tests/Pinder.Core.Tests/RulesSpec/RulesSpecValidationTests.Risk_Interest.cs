using Xunit;
using Pinder.Core.Rolls;
using Pinder.Core.Conversation;

namespace Pinder.Core.Tests.RulesSpec
{
    public partial class RulesSpecValidationTests
    {
        // =====================================================================
        // §5 — Risk Tier: boundary edge cases
        // =====================================================================

        // Mutation: would catch if need=5 was Medium instead of Safe
        [Fact]
        public void Edge_S5_RiskTier_Need5_Safe_UpperBound()
        {
            var result = MakeRisk(5, true);
            Assert.Equal(RiskTier.Safe, result.RiskTier);
        }

        // New boundaries: Safe ≤7, Medium 8–11, Hard 12–15, Bold 16–19, Reckless ≥20
        // Mutation: would catch if need=8 was Safe instead of Medium
        [Fact]
        public void Edge_S5_RiskTier_Need6_Medium_LowerBound()
        {
            var result = MakeRisk(8, true); // need=8 is the new Medium lower bound
            Assert.Equal(RiskTier.Medium, result.RiskTier);
        }

        // Mutation: would catch if need=11 was Hard instead of Medium
        [Fact]
        public void Edge_S5_RiskTier_Need10_Medium_UpperBound()
        {
            var result = MakeRisk(11, true); // need=11 is the new Medium upper bound
            Assert.Equal(RiskTier.Medium, result.RiskTier);
        }

        // Mutation: would catch if need=12 was Medium instead of Hard
        [Fact]
        public void Edge_S5_RiskTier_Need11_Hard_LowerBound()
        {
            var result = MakeRisk(12, true); // need=12 is the new Hard lower bound
            Assert.Equal(RiskTier.Hard, result.RiskTier);
        }

        // Mutation: would catch if need=15 was Bold instead of Hard
        [Fact]
        public void Edge_S5_RiskTier_Need15_Hard_UpperBound()
        {
            var result = MakeRisk(15, true);
            Assert.Equal(RiskTier.Hard, result.RiskTier);
        }

        // Mutation: would catch if need=16 was Hard instead of Bold
        [Fact]
        public void Edge_S5_RiskTier_Need16_Bold_LowerBound()
        {
            var result = MakeRisk(16, true);
            Assert.Equal(RiskTier.Bold, result.RiskTier);
        }

        // Medium risk tier now returns +2
        [Fact]
        public void Edge_S5_RiskBonus_Medium_Zero()
        {
            var result = MakeRisk(8, true);
            Assert.Equal(2, RiskTierBonus.GetInterestBonus(result)); // Medium now returns +2
        }

        // =====================================================================
        // §6 — Interest State: all boundary values
        // =====================================================================

        // Mutation: would catch if boundary at 1 was Unmatched instead of Bored
        [Fact]
        public void Edge_S6_Interest1_Bored_LowerBound()
        {
            Assert.Equal(InterestState.Bored, new InterestMeter(1).GetState());
        }

        // Mutation: would catch if boundary at 4 was Lukewarm instead of Bored
        [Fact]
        public void Edge_S6_Interest4_Bored_UpperBound()
        {
            Assert.Equal(InterestState.Bored, new InterestMeter(4).GetState());
        }

        // Mutation: would catch if boundary at 5 was Bored instead of Lukewarm
        [Fact]
        public void Edge_S6_Interest5_Lukewarm_LowerBound()
        {
            Assert.Equal(InterestState.Lukewarm, new InterestMeter(5).GetState());
        }

        // Mutation: would catch if boundary at 9 was Interested instead of Lukewarm
        [Fact]
        public void Edge_S6_Interest9_Lukewarm_UpperBound()
        {
            Assert.Equal(InterestState.Lukewarm, new InterestMeter(9).GetState());
        }

        // Mutation: would catch if boundary at 10 was Lukewarm instead of Interested
        [Fact]
        public void Edge_S6_Interest10_Interested_LowerBound()
        {
            Assert.Equal(InterestState.Interested, new InterestMeter(10).GetState());
        }

        // Mutation: would catch if boundary at 15 was VeryIntoIt instead of Interested
        [Fact]
        public void Edge_S6_Interest15_Interested_UpperBound()
        {
            Assert.Equal(InterestState.Interested, new InterestMeter(15).GetState());
        }

        // Mutation: would catch if boundary at 16 was Interested instead of VeryIntoIt
        [Fact]
        public void Edge_S6_Interest16_VeryIntoIt_LowerBound()
        {
            Assert.Equal(InterestState.VeryIntoIt, new InterestMeter(16).GetState());
        }

        // Mutation: would catch if boundary at 20 was AlmostThere instead of VeryIntoIt
        [Fact]
        public void Edge_S6_Interest20_VeryIntoIt_UpperBound()
        {
            Assert.Equal(InterestState.VeryIntoIt, new InterestMeter(20).GetState());
        }

        // Mutation: would catch if boundary at 21 was VeryIntoIt instead of AlmostThere
        [Fact]
        public void Edge_S6_Interest21_AlmostThere_LowerBound()
        {
            Assert.Equal(InterestState.AlmostThere, new InterestMeter(21).GetState());
        }

        // Mutation: would catch if boundary at 24 was DateSecured instead of AlmostThere
        [Fact]
        public void Edge_S6_Interest24_AlmostThere_UpperBound()
        {
            Assert.Equal(InterestState.AlmostThere, new InterestMeter(24).GetState());
        }

        // =====================================================================
        // §6 — InterestMeter clamping edge cases
        // =====================================================================

        // Mutation: would catch if Apply did not clamp at 25
        [Fact]
        public void Edge_S6_InterestMeter_Clamp_AtMax()
        {
            var meter = new InterestMeter(24);
            meter.Apply(5);
            Assert.Equal(25, meter.Current);
        }

        // Mutation: would catch if Apply did not clamp at 0
        [Fact]
        public void Edge_S6_InterestMeter_Clamp_AtMin()
        {
            var meter = new InterestMeter(2);
            meter.Apply(-10);
            Assert.Equal(0, meter.Current);
        }

        // Mutation: would catch if Apply(0) changed the current value
        [Fact]
        public void Edge_S6_InterestMeter_Apply_Zero_NoChange()
        {
            var meter = new InterestMeter(10);
            meter.Apply(0);
            Assert.Equal(10, meter.Current);
        }
    }
}

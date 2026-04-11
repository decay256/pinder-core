// Test Engineer validation tests for Issue #445
// Verifies spec acceptance criteria and edge cases for Rules DSL integration
// These tests are written from the spec document, NOT from implementation source

using Xunit;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;
using Pinder.Core.Progression;

namespace Pinder.Core.Tests.RulesSpec
{
    /// <summary>
    /// Validates the Rules DSL integration spec (issue-445-spec.md).
    /// Each test verifies a specific acceptance criterion or edge case
    /// from the spec document.
    /// </summary>
    public class RulesSpecValidationTests
    {
        // =====================================================================
        // Helper: constructs RollResult for failure scenarios
        // =====================================================================
        private static RollResult MakeFailure(FailureTier tier, int missMargin)
        {
            int dc = 15;
            int usedDieRoll = tier == FailureTier.Legendary ? 1 : System.Math.Max(2, System.Math.Min(19, dc - missMargin));
            int statMod = tier == FailureTier.Legendary ? 0 : (dc - missMargin) - usedDieRoll;

            return new RollResult(
                dieRoll: usedDieRoll, secondDieRoll: null, usedDieRoll: usedDieRoll,
                stat: StatType.Charm, statModifier: statMod, levelBonus: 0,
                dc: dc, tier: tier, activatedTrap: null, externalBonus: 0
            );
        }

        // =====================================================================
        // Helper: constructs RollResult for success scenarios
        // =====================================================================
        private static RollResult MakeSuccess(int beatMargin, bool nat20 = false)
        {
            int dc = 13;
            if (nat20)
                return new RollResult(20, null, 20, StatType.Charm, 0, 0, dc, FailureTier.None, null, 0);

            int total = dc + beatMargin;
            int roll = System.Math.Min(19, total);
            int mod = total - roll;
            return new RollResult(roll, null, roll, StatType.Charm, mod, 0, dc, FailureTier.None, null, 0);
        }

        // =====================================================================
        // Helper: constructs RollResult for risk tier scenarios
        // =====================================================================
        private static RollResult MakeRisk(int need, bool success)
        {
            int dc = 13;
            int statMod = dc - need;
            int roll = success ? 19 : 2;
            var tier = success ? FailureTier.None : FailureTier.Fumble;
            return new RollResult(roll, null, roll, StatType.Charm, statMod, 0, dc, tier, null, 0);
        }

        // =====================================================================
        // AC-1 verification: file structure conformance
        // =====================================================================

        // Mutation: would catch if RulesSpecTests.cs has fewer than 54 [Fact] attributes
        [Fact]
        public void AC1_RulesSpecTests_File_Has_54_Facts()
        {
            var path = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "tests", "Pinder.Core.Tests", "RulesSpec", "RulesSpecTests.cs");
            // Normalize to absolute
            path = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(path))
            {
                // Try alternative path from project root
                path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "RulesSpec", "RulesSpecTests.cs"));
            }
            Assert.True(System.IO.File.Exists(path), $"RulesSpecTests.cs not found at expected location");
            var content = System.IO.File.ReadAllText(path);
            int factCount = System.Text.RegularExpressions.Regex.Matches(content, @"\[Fact").Count;
            Assert.Equal(54, factCount);
        }

        // Mutation: would catch if source attribution header is missing
        [Fact]
        public void AC3_RulesSpecTests_Has_Source_Attribution_Header()
        {
            var path = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "tests", "Pinder.Core.Tests", "RulesSpec", "RulesSpecTests.cs");
            path = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(path))
            {
                path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "RulesSpec", "RulesSpecTests.cs"));
            }
            Assert.True(System.IO.File.Exists(path));
            var content = System.IO.File.ReadAllText(path);
            Assert.Contains("Auto-generated from rules/extracted/rules-v3-enriched.yaml", content);
            Assert.Contains("rules/tools/generate_tests.py", content);
        }

        // Mutation: would catch if skipped tests use wrong skip message
        [Fact]
        public void AC2_Skipped_Tests_Have_Descriptive_Skip_Message()
        {
            var path = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                "tests", "Pinder.Core.Tests", "RulesSpec", "RulesSpecTests.cs");
            path = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(path))
            {
                path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "RulesSpec", "RulesSpecTests.cs"));
            }
            Assert.True(System.IO.File.Exists(path));
            var content = System.IO.File.ReadAllText(path);
            var skipMatches = System.Text.RegularExpressions.Regex.Matches(content, @"\[Fact\(Skip\s*=");
            Assert.Equal(17, skipMatches.Count);
        }

        // =====================================================================
        // §5 — Failure Scale: boundary edge cases
        // =====================================================================

        // Mutation: would catch if Fumble lower boundary (miss by 1) returned wrong value
        [Fact]
        public void Edge_S5_Fumble_MissBy1_LowerBound()
        {
            var result = MakeFailure(FailureTier.Fumble, 1);
            Assert.Equal(-1, FailureScale.GetInterestDelta(result));
        }

        // Mutation: would catch if Fumble upper boundary (miss by 2) returned wrong value
        [Fact]
        public void Edge_S5_Fumble_MissBy2_UpperBound()
        {
            var result = MakeFailure(FailureTier.Fumble, 2);
            Assert.Equal(-1, FailureScale.GetInterestDelta(result));
        }

        // Mutation: would catch if Misfire lower boundary (miss by 3) was categorized as Fumble
        [Fact]
        public void Edge_S5_Misfire_MissBy3_LowerBound()
        {
            var result = MakeFailure(FailureTier.Misfire, 3);
            Assert.Equal(-1, FailureScale.GetInterestDelta(result));
        }

        // Mutation: would catch if Misfire upper boundary (miss by 5) was categorized as TropeTrap
        [Fact]
        public void Edge_S5_Misfire_MissBy5_UpperBound()
        {
            var result = MakeFailure(FailureTier.Misfire, 5);
            Assert.Equal(-1, FailureScale.GetInterestDelta(result));
        }

        // Mutation: would catch if TropeTrap lower boundary (miss by 6) returned -1 instead of -2
        [Fact]
        public void Edge_S5_TropeTrap_MissBy6_LowerBound()
        {
            var result = MakeFailure(FailureTier.TropeTrap, 6);
            Assert.Equal(-2, FailureScale.GetInterestDelta(result));
        }

        // Mutation: would catch if TropeTrap upper boundary (miss by 9) returned -3 instead of -2
        [Fact]
        public void Edge_S5_TropeTrap_MissBy9_UpperBound()
        {
            var result = MakeFailure(FailureTier.TropeTrap, 9);
            Assert.Equal(-2, FailureScale.GetInterestDelta(result));
        }

        // Mutation: would catch if Catastrophe boundary (miss by 10) returned -2 instead of -3
        [Fact]
        public void Edge_S5_Catastrophe_MissBy10_LowerBound()
        {
            var result = MakeFailure(FailureTier.Catastrophe, 10);
            Assert.Equal(-3, FailureScale.GetInterestDelta(result));
        }

        // =====================================================================
        // §5 — Success Scale: boundary edge cases
        // =====================================================================

        // Mutation: would catch if beat-by-1 boundary returned 0 instead of 1
        [Fact]
        public void Edge_S5_Success_BeatBy1_LowerBound()
        {
            var result = MakeSuccess(1);
            Assert.Equal(1, SuccessScale.GetInterestDelta(result));
        }

        // Mutation: would catch if beat-by-4 boundary returned 2 instead of 1
        [Fact]
        public void Edge_S5_Success_BeatBy4_UpperBound()
        {
            var result = MakeSuccess(4);
            Assert.Equal(1, SuccessScale.GetInterestDelta(result));
        }

        // Mutation: would catch if beat-by-5 boundary returned 1 instead of 2
        [Fact]
        public void Edge_S5_Success_BeatBy5_LowerBound()
        {
            var result = MakeSuccess(5);
            Assert.Equal(2, SuccessScale.GetInterestDelta(result));
        }

        // Mutation: would catch if beat-by-9 boundary returned 3 instead of 2
        [Fact]
        public void Edge_S5_Success_BeatBy9_UpperBound()
        {
            var result = MakeSuccess(9);
            Assert.Equal(2, SuccessScale.GetInterestDelta(result));
        }

        // Mutation: would catch if beat-by-10 boundary returned 2 instead of 3
        [Fact]
        public void Edge_S5_Success_BeatBy10_LowerBound()
        {
            var result = MakeSuccess(10);
            Assert.Equal(3, SuccessScale.GetInterestDelta(result));
        }

        // Mutation: would catch if Nat20 always returned +4 was broken
        [Fact]
        public void Edge_S5_Nat20_AlwaysPlusFour_RegardlessOfMargin()
        {
            // Nat 20 with dc=13: margin is 7, but should always be +4
            var result = MakeSuccess(7, nat20: true);
            Assert.Equal(4, SuccessScale.GetInterestDelta(result));
        }

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

        // =====================================================================
        // §7 — Shadow Threshold: boundary edge cases
        // =====================================================================

        // Mutation: would catch if shadow=0 returned tier 1 instead of 0
        [Fact]
        public void Edge_S7_Shadow0_Tier0()
        {
            Assert.Equal(0, ShadowThresholdEvaluator.GetThresholdLevel(0));
        }

        // Mutation: would catch if shadow=5 was tier 1 instead of 0
        [Fact]
        public void Edge_S7_Shadow5_Tier0_UpperBound()
        {
            Assert.Equal(0, ShadowThresholdEvaluator.GetThresholdLevel(5));
        }

        // Mutation: would catch if shadow=6 was tier 0 instead of 1
        [Fact]
        public void Edge_S7_Shadow6_Tier1_LowerBound()
        {
            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(6));
        }

        // Mutation: would catch if shadow=11 was tier 2 instead of 1
        [Fact]
        public void Edge_S7_Shadow11_Tier1_UpperBound()
        {
            Assert.Equal(1, ShadowThresholdEvaluator.GetThresholdLevel(11));
        }

        // Mutation: would catch if shadow=12 was tier 1 instead of 2
        [Fact]
        public void Edge_S7_Shadow12_Tier2_LowerBound()
        {
            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(12));
        }

        // Mutation: would catch if shadow=17 was tier 3 instead of 2
        [Fact]
        public void Edge_S7_Shadow17_Tier2_UpperBound()
        {
            Assert.Equal(2, ShadowThresholdEvaluator.GetThresholdLevel(17));
        }

        // Mutation: would catch if shadow=18 was tier 2 instead of 3
        [Fact]
        public void Edge_S7_Shadow18_Tier3_LowerBound()
        {
            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(18));
        }

        // Mutation: would catch if very high shadow (100) was not tier 3
        [Fact]
        public void Edge_S7_Shadow100_StillTier3()
        {
            Assert.Equal(3, ShadowThresholdEvaluator.GetThresholdLevel(100));
        }

        // =====================================================================
        // §10 — Progression: edge cases
        // =====================================================================

        // Mutation: would catch if XP just below level 2 threshold returned 2
        [Fact]
        public void Edge_S10_XP49_StillLevel1()
        {
            Assert.Equal(1, LevelTable.GetLevel(49));
        }

        // Mutation: would catch if XP at exact level 2 threshold returned 1
        [Fact]
        public void Edge_S10_XP50_ExactLevel2()
        {
            Assert.Equal(2, LevelTable.GetLevel(50));
        }

        // Mutation: would catch if XP just below level 3 threshold returned 3
        [Fact]
        public void Edge_S10_XP149_StillLevel2()
        {
            Assert.Equal(2, LevelTable.GetLevel(149));
        }

        // Mutation: would catch if very high XP didn't map to level 11
        [Fact]
        public void Edge_S10_XP9999_Level11()
        {
            Assert.Equal(11, LevelTable.GetLevel(9999));
        }

        // =====================================================================
        // §5 — ExternalBonus = 0 default verification
        // =====================================================================

        // Mutation: would catch if externalBonus != 0 changed success determination unexpectedly
        [Fact]
        public void Edge_S5_ExternalBonus_Zero_Default_NoEffect()
        {
            // With externalBonus=0, FinalTotal should equal Total
            var result = new RollResult(
                dieRoll: 10, secondDieRoll: null, usedDieRoll: 10,
                stat: StatType.Charm, statModifier: 2, levelBonus: 0,
                dc: 13, tier: FailureTier.Fumble, activatedTrap: null, externalBonus: 0
            );
            // Total = 10 + 2 = 12, FinalTotal = 12 + 0 = 12
            // With 0 external bonus, FinalTotal should be same as base total
            Assert.Equal(12, result.FinalTotal);
        }

        // =====================================================================
        // §5 — RiskTierBonus: failure always returns 0
        // =====================================================================

        // Mutation: would catch if Bold failure returned 2 instead of 0
        [Fact]
        public void Edge_S5_RiskBonus_BoldFailure_Zero()
        {
            var result = MakeRisk(18, false);
            Assert.Equal(0, RiskTierBonus.GetInterestBonus(result));
        }

        // Safe success now returns +1
        [Fact]
        public void Edge_S5_RiskBonus_SafeSuccess_Zero()
        {
            var result = MakeRisk(3, true);
            Assert.Equal(1, RiskTierBonus.GetInterestBonus(result)); // Safe now returns +1
        }
    }
}

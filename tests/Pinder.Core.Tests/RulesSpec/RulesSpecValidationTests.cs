// Test Engineer validation tests for Issue #445
// Verifies spec acceptance criteria and edge cases for Rules DSL integration
// These tests are written from the spec document, NOT from implementation source

using Xunit;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Conversation;

namespace Pinder.Core.Tests.RulesSpec
{
    /// <summary>
    /// Validates the Rules DSL integration spec (issue-445-spec.md).
    /// Each test verifies a specific acceptance criterion or edge case
    /// from the spec document.
    /// </summary>
    [Trait("Category", "Rules")]
    public partial class RulesSpecValidationTests
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
                return new RollResult(20, null, 20, StatType.Charm, 0, 0, dc, FailureTier.Success, null, 0);

            int total = dc + beatMargin;
            int roll = System.Math.Min(19, total);
            int mod = total - roll;
            return new RollResult(roll, null, roll, StatType.Charm, mod, 0, dc, FailureTier.Success, null, 0);
        }

        // =====================================================================
        // Helper: constructs RollResult for risk tier scenarios
        // =====================================================================
        private static RollResult MakeRisk(int need, bool success)
        {
            int dc = 13;
            int statMod = dc - need;
            int roll = success ? 19 : 2;
            var tier = success ? FailureTier.Success : FailureTier.Fumble;
            return new RollResult(roll, null, roll, StatType.Charm, statMod, 0, dc, tier, null, 0);
        }

        // =====================================================================
        // AC-1 verification: file structure conformance
        // =====================================================================

        // Mutation: would catch if generated executable guardrails are dropped
        [Fact]
        public void AC1_RulesSpecTests_File_Has_37_Executable_Facts()
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
            Assert.Equal(37, factCount);
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

        // Mutation: would catch if qualitative placeholders are reintroduced as skipped facts
        [Fact]
        public void AC2_RulesSpecTests_Has_No_Skipped_Facts()
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
            Assert.Empty(skipMatches);
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
    }
}

using System;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;

namespace Pinder.Core.Tests
{
    public partial class Issue351_PickReasoningTests
    {
        // ================================================================
        // AC2: Option score table
        // ================================================================

        [Fact]
        // Mutation: would catch if table header is missing or has wrong columns
        public void FormatScoreTable_HasCorrectHeader()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 5f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("| Option | Stat | Pct | Expected ΔI | Score |", result);
            Assert.Contains("|---|---|---|---|---|", result);
        }

        [Fact]
        // Mutation: would catch if chosen option does NOT get ✓ marker
        public void FormatScoreTable_ChosenOptionHasCheckmark()
        {
            var options = new[] { Opt(StatType.Charm), Opt(StatType.Rizz) };
            var d = Decision(0, "test", Score(0, 8f, 0.5f, 1f), Score(1, 3f, 0.2f, 0.5f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("A ✓", result);
            Assert.DoesNotContain("B ✓", result);
        }

        [Fact]
        // Mutation: would catch if checkmark is placed on wrong option when pick != 0
        public void FormatScoreTable_CheckmarkOnCorrectOptionWhenPickIsNotFirst()
        {
            var options = new[] { Opt(StatType.Charm), Opt(StatType.Rizz), Opt(StatType.Honesty) };
            var d = Decision(2, "test", Score(0, 5f, 0.4f, 1f), Score(1, 3f, 0.2f, 0.5f), Score(2, 7f, 0.6f, 2f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.DoesNotContain("A ✓", result);
            Assert.DoesNotContain("B ✓", result);
            Assert.Contains("C ✓", result);
        }

        [Fact]
        // Mutation: would catch if stat label is lowercase or wrong
        public void FormatScoreTable_StatLabelsAreUppercase()
        {
            var options = new[]
            {
                Opt(StatType.Charm), Opt(StatType.Rizz),
                Opt(StatType.Honesty), Opt(StatType.Chaos)
            };
            var scores = new[]
            {
                Score(0, 1f, 0.1f, 0.1f), Score(1, 1f, 0.1f, 0.1f),
                Score(2, 1f, 0.1f, 0.1f), Score(3, 1f, 0.1f, 0.1f)
            };
            var d = Decision(0, "test", scores);
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("CHARM", result);
            Assert.Contains("RIZZ", result);
            Assert.Contains("HONESTY", result);
            Assert.Contains("CHAOS", result);
        }

        [Fact]
        // Mutation: would catch if SelfAwareness is rendered as "SELFAWARENESS" instead of "SA"
        public void FormatScoreTable_SelfAwarenessShowsAsSA()
        {
            var options = new[] { Opt(StatType.SelfAwareness) };
            var d = Decision(0, "test", Score(0, 1f, 0.1f, 0.1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("SA", result);
        }

        [Fact]
        // Mutation: would catch if percentage uses raw float instead of Math.Round(chance*100)
        public void FormatScoreTable_PercentageIsRoundedInteger()
        {
            var options = new[] { Opt(StatType.Charm) };
            // 0.456 * 100 = 45.6 → rounds to 46
            var d = Decision(0, "test", Score(0, 1f, 0.456f, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("46%", result);
            Assert.DoesNotContain("45.6", result);
        }

        [Fact]
        // Mutation: would catch if ExpectedInterestGain lacks + prefix
        public void FormatScoreTable_ExpectedGainHasPlusSign()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 1f, 0.5f, 1.8f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("+1.8", result);
        }

        [Fact]
        // Mutation: would catch if ExpectedInterestGain uses wrong decimal format (e.g. 2 decimals)
        public void FormatScoreTable_ExpectedGainOneDecimalPlace()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 1f, 0.5f, 1.0f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("+1.0", result);
        }

        [Fact]
        // Mutation: would catch if chosen option score is not bold
        public void FormatScoreTable_ChosenScoreIsBold()
        {
            var options = new[] { Opt(StatType.Charm), Opt(StatType.Rizz) };
            var d = Decision(0, "test", Score(0, 8.3f, 0.45f, 1.8f), Score(1, 1.2f, 0.05f, 0.9f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("**8.3**", result);
            // Non-chosen score should NOT be bold
            Assert.DoesNotContain("**1.2**", result);
        }

        [Fact]
        // Mutation: would catch if bonuses are separated by spaces instead of concatenated
        public void FormatScoreTable_BonusesConcatenatedWithoutSpaces()
        {
            var options = new[] { Opt(StatType.Honesty) };
            var d = Decision(0, "test", Score(0, 5f, 0.3f, 1.4f, "📖", "🔗"));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("📖🔗", result);
            Assert.DoesNotContain("📖 🔗", result);
        }

        [Fact]
        // Mutation: would catch if bonuses are omitted from the Expected ΔI column
        public void FormatScoreTable_BonusesAppearInExpectedGainColumn()
        {
            var options = new[] { Opt(StatType.Honesty) };
            var d = Decision(0, "test", Score(0, 6.1f, 0.3f, 1.4f, "📖", "🔗"));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("+1.4 📖🔗", result);
        }

        [Fact]
        // Mutation: would catch if table always renders 4 rows regardless of options count
        public void FormatScoreTable_TwoOptions_OnlyTwoRows()
        {
            var options = new[] { Opt(StatType.Rizz), Opt(StatType.Chaos) };
            var d = Decision(1, "test", Score(0, 4f, 0.3f, 1f), Score(1, 2f, 0.1f, 0.5f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("| A |", result);
            Assert.Contains("| B ✓", result);
            Assert.DoesNotContain("| C ", result);
            Assert.DoesNotContain("| D ", result);
        }

        [Fact]
        // Mutation: would catch if single option table is broken
        public void FormatScoreTable_SingleOption_OneRow()
        {
            var options = new[] { Opt(StatType.Rizz) };
            var d = Decision(0, "test", Score(0, 5f, 0.8f, 2f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("A ✓", result);
            Assert.DoesNotContain("| B ", result);
        }

        [Fact]
        // Mutation: would catch if missing scores crash instead of showing dashes
        public void FormatScoreTable_FewerScoresThanOptions_ShowsDashes()
        {
            var options = new[]
            {
                Opt(StatType.Charm), Opt(StatType.Rizz), Opt(StatType.Honesty)
            };
            // Only 2 scores for 3 options
            var d = Decision(0, "test", Score(0, 5f, 0.45f, 1f), Score(1, 3f, 0.2f, 0.5f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("| C | HONESTY | — | — | — |", result);
        }

        [Fact]
        // Mutation: would catch if NaN SuccessChance renders as "NaN%" instead of "0%"
        public void FormatScoreTable_NaNSuccessChance_ShowsZeroPercent()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 1f, float.NaN, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("0%", result);
            Assert.DoesNotContain("NaN", result);
        }

        [Fact]
        // Mutation: would catch if negative SuccessChance shows negative percentage
        public void FormatScoreTable_NegativeSuccessChance_ShowsZeroPercent()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 1f, -0.5f, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("0%", result);
        }

        [Fact]
        // Mutation: would catch if NaN Score renders as "NaN" instead of "0.0"
        public void FormatScoreTable_NaNScore_ShowsZero()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, float.NaN, 0.5f, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("0.0", result);
            Assert.DoesNotContain("NaN", result);
        }

        [Fact]
        // Mutation: would catch if negative Score shows raw negative value instead of 0.0
        public void FormatScoreTable_NegativeScore_ShowsZero()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, -2.5f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("**0.0**", result);
        }

        [Fact]
        // Mutation: would catch if full output doesn't match spec example structure
        public void FormatScoreTable_FullSpecExample1()
        {
            var options = new[]
            {
                Opt(StatType.Charm), Opt(StatType.Rizz),
                Opt(StatType.Honesty), Opt(StatType.Chaos)
            };
            var scores = new[]
            {
                Score(0, 8.3f, 0.45f, 1.8f),
                Score(1, 1.2f, 0.05f, 0.9f),
                Score(2, 6.1f, 0.30f, 1.4f, "📖", "🔗"),
                Score(3, 0.0f, 0.00f, 0.0f)
            };
            var d = Decision(0, "test", scores);
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            // Verify all rows from spec example
            Assert.Contains("A ✓", result);
            Assert.Contains("CHARM", result);
            Assert.Contains("45%", result);
            Assert.Contains("+1.8", result);
            Assert.Contains("**8.3**", result);

            Assert.Contains("| B |", result);
            Assert.Contains("RIZZ", result);
            Assert.Contains("5%", result);

            Assert.Contains("| C |", result);
            Assert.Contains("+1.4 📖🔗", result);
            Assert.Contains("6.1", result);

            Assert.Contains("| D |", result);
            Assert.Contains("CHAOS", result);
            Assert.Contains("0%", result);
            Assert.Contains("+0.0", result);
        }

        // ================================================================
        // Wit stat label
        // ================================================================

        [Fact]
        // Mutation: would catch if Wit stat is mislabeled
        public void FormatScoreTable_WitStatShowsWIT()
        {
            var options = new[] { Opt(StatType.Wit) };
            var d = Decision(0, "test", Score(0, 1f, 0.1f, 0.1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("WIT", result);
        }

        // ================================================================
        // Score with no bonuses has no trailing text after gain
        // ================================================================

        [Fact]
        // Mutation: would catch if empty BonusesApplied still appends space or brackets
        public void FormatScoreTable_NoBonuses_CleanExpectedGain()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 5f, 0.5f, 1.5f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            // Should contain +1.5 followed by space and pipe, no trailing emoji
            Assert.Contains("+1.5 |", result);
        }

        // ================================================================
        // Score formatting: one decimal place
        // ================================================================

        [Fact]
        // Mutation: would catch if Score uses integer formatting instead of F1
        public void FormatScoreTable_ScoreFormattedToOneDecimal()
        {
            var options = new[] { Opt(StatType.Charm) };
            var d = Decision(0, "test", Score(0, 10.0f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("10.0", result);
        }

        // ================================================================
        // Letter labels follow A, B, C, D sequence
        // ================================================================

        [Fact]
        // Mutation: would catch if letter assignment uses wrong sequence or starts at wrong letter
        public void FormatScoreTable_LetterLabelsFollowSequence()
        {
            var options = new[]
            {
                Opt(StatType.Charm), Opt(StatType.Rizz),
                Opt(StatType.Honesty), Opt(StatType.Chaos)
            };
            var scores = new[]
            {
                Score(0, 1f, 0.1f, 0.1f), Score(1, 1f, 0.1f, 0.1f),
                Score(2, 1f, 0.1f, 0.1f), Score(3, 1f, 0.1f, 0.1f)
            };
            var d = Decision(3, "test", scores);
            var result = PlaytestFormatter.FormatScoreTable(d, options);

            Assert.Contains("| A |", result);
            Assert.Contains("| B |", result);
            Assert.Contains("| C |", result);
            Assert.Contains("D ✓", result);
        }
    }
}

using System;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #351: Show pick reasoning in playtest output.
    /// Validates FormatReasoningBlock and FormatScoreTable from PlaytestFormatter.
    /// Spec: docs/specs/issue-351-spec.md
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public partial class Issue351_PickReasoningTests
    {
        // ── Helper factories (test-only utilities) ──────────────────────

        private static DialogueOption Opt(StatType stat) =>
            new DialogueOption(stat, "test text");

        private static OptionScore Score(int idx, float score, float chance, float gain, params string[] bonuses) =>
            new OptionScore(idx, score, chance, gain, bonuses.Length > 0 ? bonuses : Array.Empty<string>());

        private static PlayerDecision Decision(int pick, string reasoning, params OptionScore[] scores) =>
            new PlayerDecision(pick, reasoning, scores);

        // ================================================================
        // AC1: Reasoning block after each pick
        // ================================================================

        [Fact]
        // Mutation: would catch if header format string omits agent type name
        public void FormatReasoningBlock_IncludesAgentTypeInHeader()
        {
            var d = Decision(0, "Pick A", Score(0, 1f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatReasoningBlock(d, "ScoringPlayerAgent");
            // Header label removed in #746 — only blockquote content remains
            Assert.DoesNotContain("**Player reasoning", result);
            Assert.Contains("> Pick A", result);
        }

        [Fact]
        // Mutation: would catch if lines are not prefixed with "> "
        public void FormatReasoningBlock_EachLineIsBlockquoted()
        {
            var d = Decision(0, "Line one.\nLine two.\nLine three.", Score(0, 1f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatReasoningBlock(d, "TestAgent");

            Assert.Contains("> Line one.", result);
            Assert.Contains("> Line two.", result);
            Assert.Contains("> Line three.", result);
        }

        [Fact]
        // Mutation: would catch if reasoning is split on wrong delimiter (e.g. ". " instead of "\n")
        public void FormatReasoningBlock_SplitsOnNewline()
        {
            var d = Decision(0, "A\nB\nC", Score(0, 1f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatReasoningBlock(d, "Agent");

            var lines = result.Split('\n');
            int blockquoteCount = 0;
            foreach (var line in lines)
            {
                if (line.TrimEnd().StartsWith("> ") || line.TrimEnd() == ">")
                    blockquoteCount++;
            }
            Assert.True(blockquoteCount >= 3, $"Expected at least 3 blockquoted lines, got {blockquoteCount}");
        }

        // ================================================================
        // AC3: Reasoning is pass-through (not hardcoded)
        // ================================================================

        [Fact]
        // Mutation: would catch if reasoning text is replaced with hardcoded string
        public void FormatReasoningBlock_DisplaysExactReasoningText()
        {
            var uniqueText = "Unique reasoning XYZ-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var d = Decision(0, uniqueText, Score(0, 1f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatReasoningBlock(d, "Agent");

            Assert.Contains(uniqueText, result);
        }

        // ================================================================
        // AC4: Works for both agent types (no branching on type)
        // ================================================================

        [Fact]
        // Mutation: would catch if formatting branches on "ScoringPlayerAgent" vs "LlmPlayerAgent"
        public void FormatReasoningBlock_WorksWithAnyAgentName()
        {
            var d = Decision(0, "reason", Score(0, 1f, 0.5f, 1f));

            var r1 = PlaytestFormatter.FormatReasoningBlock(d, "ScoringPlayerAgent");
            var r2 = PlaytestFormatter.FormatReasoningBlock(d, "LlmPlayerAgent");
            var r3 = PlaytestFormatter.FormatReasoningBlock(d, "CustomAgent");

            // Header label removed in #746 — agent name no longer in output
            Assert.DoesNotContain("(ScoringPlayerAgent)", r1);
            Assert.DoesNotContain("(LlmPlayerAgent)", r2);
            Assert.DoesNotContain("(CustomAgent)", r3);

            // All should contain the same reasoning
            Assert.Contains("> reason", r1);
            Assert.Contains("> reason", r2);
            Assert.Contains("> reason", r3);
        }

        // ================================================================
        // Edge case: Empty/null reasoning
        // ================================================================

        [Fact]
        // Mutation: would catch if empty reasoning shows blank instead of placeholder
        public void FormatReasoningBlock_EmptyReasoning_ShowsPlaceholder()
        {
            var d = Decision(0, "", Score(0, 1f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatReasoningBlock(d, "Agent");

            Assert.Contains("> (no reasoning provided)", result);
        }

        [Fact]
        // Mutation: would catch if PlayerDecision constructor accepts null reasoning
        public void PlayerDecision_NullReasoning_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, null!, new[] { Score(0, 1f, 0.5f, 1f) }));
        }

        // ================================================================
        // Edge case: Null decision
        // ================================================================

        [Fact]
        // Mutation: would catch if null decision throws instead of returning empty
        public void FormatReasoningBlock_NullDecision_ReturnsEmpty()
        {
            var result = PlaytestFormatter.FormatReasoningBlock(null, "Agent");
            Assert.Equal("", result);
        }

        [Fact]
        // Mutation: would catch if null decision throws instead of returning empty
        public void FormatScoreTable_NullDecision_ReturnsEmpty()
        {
            var result = PlaytestFormatter.FormatScoreTable(null, new[] { Opt(StatType.Charm) });
            Assert.Equal("", result);
        }

        // ================================================================
        // Edge case: Scores array mismatch
        // ================================================================

        [Fact]
        // Mutation: would catch if PlayerDecision constructor accepts null scores
        public void PlayerDecision_NullScores_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlayerDecision(0, "test", null!));
        }

        // ================================================================
        // Edge case: OptionIndex out of range
        // ================================================================

        [Fact]
        // Mutation: would catch if PlayerDecision accepts out-of-range option index
        public void PlayerDecision_OptionIndexOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerDecision(5, "test", new[] { Score(0, 5f, 0.45f, 1f), Score(1, 3f, 0.2f, 0.5f) }));
        }

        // ================================================================
        // Edge case: Multi-paragraph reasoning
        // ================================================================

        [Fact]
        // Mutation: would catch if empty lines between paragraphs are dropped
        public void FormatReasoningBlock_MultiParagraph_EmptyLinesAreBlockquoted()
        {
            var d = Decision(0, "Para one.\n\nPara two.", Score(0, 1f, 0.5f, 1f));
            var result = PlaytestFormatter.FormatReasoningBlock(d, "LlmPlayerAgent");

            Assert.Contains("> Para one.", result);
            Assert.Contains("> Para two.", result);
            // The empty line between paragraphs should still be present as a blockquote line
            var lines = result.Split('\n');
            bool hasEmptyBlockquote = false;
            foreach (var line in lines)
            {
                if (line.TrimEnd() == ">" || line.TrimEnd() == "> ")
                    hasEmptyBlockquote = true;
            }
            Assert.True(hasEmptyBlockquote, "Expected an empty blockquote line between paragraphs");
        }
    }
}

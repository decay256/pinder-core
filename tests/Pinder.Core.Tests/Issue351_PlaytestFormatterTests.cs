using System;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;

namespace Pinder.Core.Tests
{
    [Trait("Category", "SessionRunner")]
    public class Issue351_PlaytestFormatterTests
    {
        // ── Helper factories ────────────────────────────────────────────

        private static DialogueOption MakeOption(StatType stat)
        {
            return new DialogueOption(stat, "test text");
        }

        private static OptionScore MakeScore(int index, float score, float successChance, float expectedGain, string[]? bonuses = null)
        {
            return new OptionScore(index, score, successChance, expectedGain, bonuses ?? Array.Empty<string>());
        }

        private static PlayerDecision MakeDecision(int pick, string reasoning, OptionScore[] scores)
        {
            return new PlayerDecision(pick, reasoning, scores);
        }

        // ── FormatReasoningBlock tests ──────────────────────────────────

        [Fact]
        public void FormatReasoningBlock_ScoringAgent_FormatsAsBlockquote()
        {
            var decision = MakeDecision(0, "Line one.\nLine two.\nPick: A", new[]
            {
                MakeScore(0, 8.3f, 0.45f, 1.8f),
            });

            var result = PlaytestFormatter.FormatReasoningBlock(decision, "ScoringPlayerAgent");

            Assert.Contains("**Player reasoning (ScoringPlayerAgent):**", result);
            Assert.Contains("> Line one.", result);
            Assert.Contains("> Line two.", result);
            Assert.Contains("> Pick: A", result);
        }

        [Fact]
        public void FormatReasoningBlock_EmptyReasoning_ShowsPlaceholder()
        {
            var decision = MakeDecision(0, "", new[]
            {
                MakeScore(0, 1.0f, 0.5f, 1.0f),
            });

            var result = PlaytestFormatter.FormatReasoningBlock(decision, "ScoringPlayerAgent");

            Assert.Contains("> (no reasoning provided)", result);
        }

        [Fact]
        public void FormatReasoningBlock_NullDecision_ReturnsEmpty()
        {
            var result = PlaytestFormatter.FormatReasoningBlock(null, "ScoringPlayerAgent");
            Assert.Equal("", result);
        }

        [Fact]
        public void FormatReasoningBlock_LlmAgent_ShowsAgentName()
        {
            var decision = MakeDecision(0, "LLM reasoning", new[]
            {
                MakeScore(0, 5.0f, 0.5f, 1.0f),
            });

            var result = PlaytestFormatter.FormatReasoningBlock(decision, "LlmPlayerAgent");

            Assert.Contains("**Player reasoning (LlmPlayerAgent):**", result);
        }

        // ── FormatScoreTable tests ──────────────────────────────────────

        [Fact]
        public void FormatScoreTable_FourOptions_RendersMarkdownTable()
        {
            var options = new[]
            {
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Chaos),
            };

            var scores = new[]
            {
                MakeScore(0, 8.3f, 0.45f, 1.8f),
                MakeScore(1, 1.2f, 0.05f, 0.9f),
                MakeScore(2, 6.1f, 0.30f, 1.4f, new[] { "📖", "🔗" }),
                MakeScore(3, 0.0f, 0.00f, 0.0f),
            };

            var decision = MakeDecision(0, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            // Header
            Assert.Contains("| Option | Stat | Pct | Expected ΔI | Score |", result);
            Assert.Contains("|---|---|---|---|---|", result);

            // Chosen option has ✓ and bold score
            Assert.Contains("A ✓", result);
            Assert.Contains("**8.3**", result);

            // Non-chosen options
            Assert.Contains("| B | RIZZ | 5% |", result);
            Assert.Contains("| C | HONESTY | 30% | +1.4 📖🔗 |", result);
            Assert.Contains("| D | CHAOS | 0% |", result);
        }

        [Fact]
        public void FormatScoreTable_ChosenOptionC_MarksCorrectRow()
        {
            var options = new[]
            {
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty),
            };

            var scores = new[]
            {
                MakeScore(0, 5.0f, 0.45f, 1.0f),
                MakeScore(1, 3.0f, 0.20f, 0.5f),
                MakeScore(2, 7.0f, 0.60f, 2.0f),
            };

            var decision = MakeDecision(2, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            // A and B should NOT have ✓
            Assert.Contains("| A |", result);
            Assert.Contains("| B |", result);
            // C should have ✓ and bold score
            Assert.Contains("C ✓", result);
            Assert.Contains("**7.0**", result);
        }

        [Fact]
        public void FormatScoreTable_NullDecision_ReturnsEmpty()
        {
            var result = PlaytestFormatter.FormatScoreTable(null, new[] { MakeOption(StatType.Charm) });
            Assert.Equal("", result);
        }

        [Fact]
        public void FormatScoreTable_BonusesConcatenatedWithoutSpaces()
        {
            var options = new[] { MakeOption(StatType.Honesty) };
            var scores = new[] { MakeScore(0, 5.0f, 0.30f, 1.4f, new[] { "📖", "🔗" }) };
            var decision = MakeDecision(0, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            Assert.Contains("📖🔗", result);
        }

        [Fact]
        public void FormatScoreTable_FewerScoresThanOptions_ShowsDash()
        {
            var options = new[]
            {
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty),
            };

            // Only 2 scores for 3 options
            var scores = new[]
            {
                MakeScore(0, 5.0f, 0.45f, 1.0f),
                MakeScore(1, 3.0f, 0.20f, 0.5f),
            };

            var decision = MakeDecision(0, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            // Third row should have dashes
            Assert.Contains("| C | HONESTY | — | — | — |", result);
        }

        [Fact]
        public void FormatScoreTable_PercentageRounding()
        {
            var options = new[] { MakeOption(StatType.Charm) };
            // 0.456 * 100 = 45.6, rounds to 46
            var scores = new[] { MakeScore(0, 1.0f, 0.456f, 1.0f) };
            var decision = MakeDecision(0, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            Assert.Contains("46%", result);
        }

        [Fact]
        public void FormatScoreTable_NegativeScore_ShowsZero()
        {
            var options = new[] { MakeOption(StatType.Charm) };
            var scores = new[] { MakeScore(0, -1.5f, 0.5f, 1.0f) };
            var decision = MakeDecision(0, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            Assert.Contains("**0.0**", result);
        }

        [Fact]
        public void FormatScoreTable_TwoOptions_TwoRows()
        {
            var options = new[]
            {
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Chaos),
            };

            var scores = new[]
            {
                MakeScore(0, 4.0f, 0.30f, 1.0f),
                MakeScore(1, 2.0f, 0.10f, 0.5f),
            };

            var decision = MakeDecision(1, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            // Should have exactly 2 data rows (A and B)
            Assert.Contains("| A | RIZZ |", result);
            Assert.Contains("| B ✓ | CHAOS |", result);
            Assert.DoesNotContain("| C |", result);
        }

        [Fact]
        public void FormatReasoningBlock_MultiParagraph_AllLinesBlockquoted()
        {
            var decision = MakeDecision(0, "Para one.\n\nPara two.\nLine three.", new[]
            {
                MakeScore(0, 1.0f, 0.5f, 1.0f),
            });

            var result = PlaytestFormatter.FormatReasoningBlock(decision, "LlmPlayerAgent");

            // Empty line between paragraphs should also be blockquoted
            Assert.Contains("> Para one.", result);
            Assert.Contains("> ", result); // empty blockquote line
            Assert.Contains("> Para two.", result);
            Assert.Contains("> Line three.", result);
        }

        [Fact]
        public void FormatScoreTable_AllStatTypes_CorrectLabels()
        {
            var options = new[]
            {
                MakeOption(StatType.Charm),
                MakeOption(StatType.Rizz),
                MakeOption(StatType.Honesty),
                MakeOption(StatType.Chaos),
                MakeOption(StatType.Wit),
                MakeOption(StatType.SelfAwareness),
            };

            var scores = new[]
            {
                MakeScore(0, 1.0f, 0.1f, 0.1f),
                MakeScore(1, 1.0f, 0.1f, 0.1f),
                MakeScore(2, 1.0f, 0.1f, 0.1f),
                MakeScore(3, 1.0f, 0.1f, 0.1f),
                MakeScore(4, 1.0f, 0.1f, 0.1f),
                MakeScore(5, 1.0f, 0.1f, 0.1f),
            };

            var decision = MakeDecision(0, "test", scores);

            var result = PlaytestFormatter.FormatScoreTable(decision, options);

            Assert.Contains("CHARM", result);
            Assert.Contains("RIZZ", result);
            Assert.Contains("HONESTY", result);
            Assert.Contains("CHAOS", result);
            Assert.Contains("WIT", result);
            Assert.Contains("SA", result);
        }
    }
}

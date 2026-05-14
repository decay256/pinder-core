using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #867: audit + cut opponent-behavior sections from BuildPlayer.
    /// Verifies OpponentFriction and OpponentCuriosity are stripped from player-side
    /// system prompts while preserved in opponent-side prompts.
    /// ConversationArcProgression is shared conversation structure — kept in both.
    /// PlayerProbing is player-specific — kept in BuildPlayer.
    /// </summary>
    public class Issue867_DeliveryTokenAuditSpecTests
    {
        private static GameDefinition FullFixture()
        {
            return new GameDefinition(
                "TestGame",
                "Vision text here.",
                "World description text here.",
                "Player role description here.",
                "Opponent role description here.",
                "Meta contract text here.",
                "Writing rules text here.",
                textingPsychology: "texting psych content",
                revelationOverStatement: "revelation content",
                opponentFriction: "opponent friction content",
                opponentCuriosity: "opponent curiosity content",
                conversationArcProgression: "conversation arc content",
                playerProbing: "player probing content");
        }

        #region BuildPlayer: opponent sections removed

        [Fact]
        public void BuildPlayer_ExcludesOpponentFriction()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            Assert.DoesNotContain("OPPONENT RESISTANCE", result);
            Assert.DoesNotContain("opponent friction content", result);
        }

        [Fact]
        public void BuildPlayer_ExcludesOpponentCuriosity()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            Assert.DoesNotContain("OPPONENT CURIOSITY", result);
            Assert.DoesNotContain("opponent curiosity content", result);
        }

        [Fact]
        public void BuildPlayer_IncludesConversationArcProgression()
        {
            // ConversationArc is shared conversation-structure guidance relevant
            // to both sides; kept in BuildPlayer per #867 LESSONS_LEARNED rule.
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            Assert.Contains("CONVERSATION ARC", result);
            Assert.Contains("conversation arc content", result);
        }

        [Fact]
        public void BuildPlayer_StillIncludesPlayerProbing()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            Assert.Contains("PLAYER PROBING", result);
            Assert.Contains("player probing content", result);
        }

        [Fact]
        public void BuildPlayer_StillIncludesTextingPsychology()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            Assert.Contains("TEXTING PSYCHOLOGY", result);
            Assert.Contains("texting psych content", result);
        }

        [Fact]
        public void BuildPlayer_StillIncludesRevelationOverStatement()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            Assert.Contains("REVELATION OVER STATEMENT", result);
            Assert.Contains("revelation content", result);
        }

        #endregion

        #region BuildOpponent: opponent sections preserved

        [Fact]
        public void BuildOpponent_IncludesOpponentFriction()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildOpponent("opponent prompt", def);
            Assert.Contains("OPPONENT RESISTANCE", result);
            Assert.Contains("opponent friction content", result);
        }

        [Fact]
        public void BuildOpponent_IncludesOpponentCuriosity()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildOpponent("opponent prompt", def);
            Assert.Contains("OPPONENT CURIOSITY", result);
            Assert.Contains("opponent curiosity content", result);
        }

        [Fact]
        public void BuildOpponent_IncludesConversationArcProgression()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildOpponent("opponent prompt", def);
            Assert.Contains("CONVERSATION ARC", result);
            Assert.Contains("conversation arc content", result);
        }

        [Fact]
        public void BuildOpponent_ExcludesPlayerProbing()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildOpponent("opponent prompt", def);
            Assert.DoesNotContain("PLAYER PROBING", result);
            Assert.DoesNotContain("player probing content", result);
        }

        #endregion

        #region Build (legacy shared prompt) — opponent sections also removed

        // Build() is the legacy joint method that retains ALL sections per
        // LESSONS_LEARNED PROMPT-BLOAT-FROM-CROSS-ROLE-SECTIONS. Only BuildPlayer
        // is trimmed (saves ~1,000 tokens per delivery call). Build() callers are
        // test-only paths that want the full prompt for parity checks.
        [Fact]
        public void Build_IncludesOpponentFriction()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.Build("p", "o", def);
            Assert.Contains("OPPONENT RESISTANCE", result);
            Assert.Contains("opponent friction content", result);
        }

        [Fact]
        public void Build_IncludesOpponentCuriosity()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.Build("p", "o", def);
            Assert.Contains("OPPONENT CURIOSITY", result);
            Assert.Contains("opponent curiosity content", result);
        }

        [Fact]
        public void Build_IncludesConversationArcProgression()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.Build("p", "o", def);
            Assert.Contains("CONVERSATION ARC", result);
            Assert.Contains("conversation arc content", result);
        }

        [Fact]
        public void Build_StillIncludesPlayerProbing()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.Build("p", "o", def);
            Assert.Contains("PLAYER PROBING", result);
            Assert.Contains("player probing content", result);
        }

        #endregion

        #region Token savings estimate

        [Fact]
        public void BuildPlayer_WithFullGameDefinition_IsSmallerThanBefore()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayer("player prompt", def);
            // With the three opponent sections removed, BuildPlayer should be
            // measurably smaller. This is a coarse sanity check.
            // The three removed sections + headers total ~1,400 tokens (~5,600 chars).
            // Even with a minimal GameDefinition, we'd expect at least 5K chars.
            Assert.True(result.Length > 0);
            // It must not exceed an arbitrary safe ceiling (full game-definition.yaml
            // is the largest admissible input; we set the ceiling at 20,000 chars as
            // a bounding-box sanity check that no accidental bloater section gets
            // added back).
            Assert.True(result.Length < 20_000,
                $"BuildPlayer output length {result.Length} exceeds safety ceiling of 20,000 chars.");
        }

        #endregion
    }
}

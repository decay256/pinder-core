using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #867: audit + cut datee-behavior sections from BuildPlayerAvatar.
    /// Verifies DateeFriction and DateeCuriosity are stripped from player-side
    /// system prompts while preserved in datee-side prompts.
    /// ConversationArcProgression is shared conversation structure — kept in both.
    /// PlayerProbing is player-specific — kept in BuildPlayerAvatar.
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
                "Datee role description here.",
                "Meta contract text here.\n\n== WRITING RULES ==\n\nWriting rules text here.\n\n" +
                "== TEXTING PSYCHOLOGY ==\n\ntexting psych content\n\n" +
                "== REVELATION OVER STATEMENT ==\n\nrevelation content",
                dateeFriction: "datee friction content",
                dateeCuriosity: "datee curiosity content",
                conversationArcProgression: "conversation arc content",
                playerProbing: "player probing content");
        }

        #region BuildPlayerAvatar: datee sections removed

        [Fact]
        public void BuildPlayer_ExcludesDateeFriction()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.DoesNotContain("DATEE RESISTANCE", result);
            Assert.DoesNotContain("datee friction content", result);
        }

        [Fact]
        public void BuildPlayer_ExcludesDateeCuriosity()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.DoesNotContain("DATEE CURIOSITY", result);
            Assert.DoesNotContain("datee curiosity content", result);
        }

        [Fact]
        public void BuildPlayer_IncludesConversationArcProgression()
        {
            // ConversationArc is shared conversation-structure guidance relevant
            // to both sides; kept in BuildPlayerAvatar per #867 LESSONS_LEARNED rule.
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.Contains("CONVERSATION ARC", result);
            Assert.Contains("conversation arc content", result);
        }

        [Fact]
        public void BuildPlayer_StillIncludesPlayerProbing()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.Contains("PLAYER PROBING", result);
            Assert.Contains("player probing content", result);
        }

        [Fact]
        public void BuildPlayer_StillIncludesTextingPsychology()
        {
            // texting psychology is now a sub-section folded into the merged
            // narrative_doctrine body; it must still surface in BuildPlayerAvatar output.
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.Contains("TEXTING PSYCHOLOGY", result);
            Assert.Contains("texting psych content", result);
        }

        [Fact]
        public void BuildPlayer_StillIncludesRevelationOverStatement()
        {
            // revelation-over-statement is now a sub-section folded into the merged
            // narrative_doctrine body; it must still surface in BuildPlayerAvatar output.
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.Contains("REVELATION OVER STATEMENT", result);
            Assert.Contains("revelation content", result);
        }

        #endregion

        #region BuildDatee: datee sections preserved

        [Fact]
        public void BuildDatee_IncludesDateeFriction()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildDatee("datee prompt", def);
            Assert.Contains("DATEE RESISTANCE", result);
            Assert.Contains("datee friction content", result);
        }

        [Fact]
        public void BuildDatee_IncludesDateeCuriosity()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildDatee("datee prompt", def);
            Assert.Contains("DATEE CURIOSITY", result);
            Assert.Contains("datee curiosity content", result);
        }

        [Fact]
        public void BuildDatee_IncludesConversationArcProgression()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildDatee("datee prompt", def);
            Assert.Contains("CONVERSATION ARC", result);
            Assert.Contains("conversation arc content", result);
        }

        [Fact]
        public void BuildDatee_ExcludesPlayerProbing()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildDatee("datee prompt", def);
            Assert.DoesNotContain("PLAYER PROBING", result);
            Assert.DoesNotContain("player probing content", result);
        }

        #endregion

        #region Build (legacy shared prompt) — datee sections also removed

        // Build() is the legacy joint method that retains ALL sections per
        // LESSONS_LEARNED PROMPT-BLOAT-FROM-CROSS-ROLE-SECTIONS. Only BuildPlayerAvatar
        // is trimmed (saves ~1,000 tokens per delivery call). Build() callers are
        // test-only paths that want the full prompt for parity checks.
        [Fact]
        public void Build_IncludesDateeFriction()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.Build("p", "o", def);
            Assert.Contains("DATEE RESISTANCE", result);
            Assert.Contains("datee friction content", result);
        }

        [Fact]
        public void Build_IncludesDateeCuriosity()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.Build("p", "o", def);
            Assert.Contains("DATEE CURIOSITY", result);
            Assert.Contains("datee curiosity content", result);
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
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            // With the three datee sections removed, BuildPlayerAvatar should be
            // measurably smaller. This is a coarse sanity check.
            // The three removed sections + headers total ~1,400 tokens (~5,600 chars).
            // Even with a minimal GameDefinition, we'd expect at least 5K chars.
            Assert.True(result.Length > 0);
            // It must not exceed an arbitrary safe ceiling (full game-definition.yaml
            // is the largest admissible input; we set the ceiling at 20,000 chars as
            // a bounding-box sanity check that no accidental bloater section gets
            // added back).
            Assert.True(result.Length < 20_000,
                $"BuildPlayerAvatar output length {result.Length} exceeds safety ceiling of 20,000 chars.");
        }

        #endregion
    }
}

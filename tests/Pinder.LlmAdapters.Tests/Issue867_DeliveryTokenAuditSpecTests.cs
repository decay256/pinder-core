using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #1124 supersedes #867: the two sessions now share ONE canonical GM
    /// puppeteer template, so the shared GM base (including the conversation-
    /// dynamic sections) is identical for both BuildPlayerAvatar and BuildDatee.
    /// The only per-session difference is the injected character-spec block.
    ///
    /// These tests pin the new shared-base contract and keep the original
    /// token-ceiling sanity check.
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

        #region Shared GM base is identical for both sessions (#1124)

        [Fact]
        public void SharedBase_IdenticalForBothSessions()
        {
            var def = FullFixture();
            var player = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            var datee = SessionSystemPromptBuilder.BuildDatee("datee prompt", def);

            var header = SessionSystemPromptBuilder.CharacterSpecHeader;
            var playerBase = player.Substring(0, player.IndexOf(header, StringComparison.Ordinal));
            var dateeBase = datee.Substring(0, datee.IndexOf(header, StringComparison.Ordinal));

            Assert.Equal(dateeBase, playerBase);
        }

        [Fact]
        public void SharedBase_IncludesConversationDynamicSections()
        {
            // Under the single-puppeteer model the GM needs the full conversation
            // dynamic regardless of which character it portrays, so these live in
            // the shared base and appear in BOTH sessions.
            var def = FullFixture();
            foreach (var result in new[]
            {
                SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def),
                SessionSystemPromptBuilder.BuildDatee("datee prompt", def),
            })
            {
                Assert.Contains("DATEE RESISTANCE", result);
                Assert.Contains("datee friction content", result);
                Assert.Contains("DATEE CURIOSITY", result);
                Assert.Contains("datee curiosity content", result);
                Assert.Contains("CONVERSATION ARC", result);
                Assert.Contains("conversation arc content", result);
                Assert.Contains("PLAYER PROBING", result);
                Assert.Contains("player probing content", result);
            }
        }

        [Fact]
        public void SharedBase_IncludesNarrativeDoctrineSubSections()
        {
            // texting psychology + revelation-over-statement are folded into the
            // merged narrative_doctrine body; they must surface in both sessions.
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.Contains("TEXTING PSYCHOLOGY", result);
            Assert.Contains("texting psych content", result);
            Assert.Contains("REVELATION OVER STATEMENT", result);
            Assert.Contains("revelation content", result);
        }

        #endregion

        #region Character spec only contains its own profile

        [Fact]
        public void PlayerSession_SpecContainsPlayerRoleAndProfile()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("PLAYER_PROFILE_MARKER", def);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var spec = result.Substring(specIdx);
            Assert.Contains("Player role description here.", spec);
            Assert.Contains("PLAYER_PROFILE_MARKER", spec);
        }

        [Fact]
        public void DateeSession_SpecContainsDateeRoleAndProfile()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildDatee("DATEE_PROFILE_MARKER", def);
            var specIdx = result.IndexOf(SessionSystemPromptBuilder.CharacterSpecHeader, StringComparison.Ordinal);
            var spec = result.Substring(specIdx);
            Assert.Contains("Datee role description here.", spec);
            Assert.Contains("DATEE_PROFILE_MARKER", spec);
        }

        #endregion

        #region Token savings / bounding-box sanity

        [Fact]
        public void BuildPlayer_WithFullGameDefinition_StaysUnderCeiling()
        {
            var def = FullFixture();
            var result = SessionSystemPromptBuilder.BuildPlayerAvatar("player prompt", def);
            Assert.True(result.Length > 0);
            // Bounding-box sanity check: no accidental bloater section creeps in.
            Assert.True(result.Length < 20_000,
                $"BuildPlayerAvatar output length {result.Length} exceeds safety ceiling of 20,000 chars.");
        }

        #endregion
    }
}

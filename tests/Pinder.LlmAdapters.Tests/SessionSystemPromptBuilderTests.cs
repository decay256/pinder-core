using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class SessionSystemPromptBuilderTests
    {
        private const string PlayerAvatarPrompt = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
        private const string DateePrompt = "You are Sable. Fast-talking. Uses omg and emoji. Level 5 Journeyman.";

        [Fact]
        public void Build_ContainsBothCharacterPrompts()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains(PlayerAvatarPrompt, result);
            Assert.Contains(DateePrompt, result);
        }

        [Fact]
        public void Build_ContainsGameVision()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains("comedy dating RPG", result);
        }

        [Fact]
        public void Build_ContainsWorldDescription()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains("dating server", result);
        }

        [Fact]
        public void Build_ContainsMetaContract()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains("break character", result);
        }

        [Fact]
        public void Build_ContainsWritingRules()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains("texting register", result);
        }

        [Fact]
        public void Build_ContainsNarrativeDoctrineHeader()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
        }

        [Fact]
        public void Build_HasFiveSections()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== WORLD RULES ==", result);
            Assert.Contains("== PLAYER CHARACTER ==", result);
            Assert.Contains("== DATEE CHARACTER ==", result);
            Assert.Contains("== NARRATIVE DOCTRINE ==", result);
        }

        [Fact]
        public void Build_SectionsInCorrectOrder()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt);
            var visionIdx = result.IndexOf("== GAME VISION ==");
            var worldIdx = result.IndexOf("== WORLD RULES ==");
            var playerIdx = result.IndexOf("== PLAYER CHARACTER ==");
            var dateeIdx = result.IndexOf("== DATEE CHARACTER ==");
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==");

            // Variable character sections come LAST: static game/doctrine material first,
            // then PLAYER CHARACTER and DATEE CHARACTER at the tail.
            Assert.True(visionIdx < worldIdx, "GAME VISION should come before WORLD RULES");
            Assert.True(worldIdx < metaIdx, "WORLD RULES should come before NARRATIVE DOCTRINE");
            Assert.True(metaIdx < playerIdx, "NARRATIVE DOCTRINE should come before PLAYER CHARACTER");
            Assert.True(playerIdx < dateeIdx, "PLAYER CHARACTER should come before DATEE CHARACTER");
        }

        [Fact]
        public void Build_NullGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt, null);
            Assert.Contains("Pinder", result);
            Assert.Contains("comedy dating RPG", result);
        }

        [Fact]
        public void Build_CustomGameDef_UsesProvidedValues()
        {
            var custom = new GameDefinition(
                "CustomGame",
                "Custom vision text",
                "Custom world desc",
                "Custom player role",
                "Custom datee role",
                "Custom meta contract Custom writing rules");

            var result = SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, DateePrompt, custom);
            Assert.Contains("Custom vision text", result);
            Assert.Contains("Custom world desc", result);
            Assert.Contains("Custom meta contract", result);
            Assert.Contains("Custom writing rules", result);
        }

        [Fact]
        public void Build_NullPlayerPrompt_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.Build(null!, DateePrompt));
        }

        [Fact]
        public void Build_NullDateePrompt_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.Build(PlayerAvatarPrompt, null!));
        }

        [Fact]
        public void Build_EmptyPrompts_ProducesValidOutput()
        {
            var result = SessionSystemPromptBuilder.Build("", "");
            Assert.Contains("== PLAYER CHARACTER ==", result);
            Assert.Contains("== DATEE CHARACTER ==", result);
        }

        [Fact]
        public void Build_MetaContractIncludesWritingRules()
        {
            var custom = new GameDefinition(
                "G", "V", "W", "P", "O",
                "MetaSection WritingSection");

            var result = SessionSystemPromptBuilder.Build("p", "o", custom);
            var metaIdx = result.IndexOf("== NARRATIVE DOCTRINE ==");
            var afterMeta = result.Substring(metaIdx);
            Assert.Contains("MetaSection", afterMeta);
            Assert.Contains("WritingSection", afterMeta);
        }

        // Regression: #867 — datee-only sections must NOT leak into BuildPlayerAvatar.
        [Fact]
        public void BuildPlayer_ExcludesDateeOnlySections()
        {
            var gd = new GameDefinition(
                "T", "V", "W", "P", "O", "ND",
                dateeFriction: "datee resists the player",
                dateeCuriosity: "datee probes player's bio",
                conversationArcProgression: "both sides move the convo forward",
                playerProbing: "player follows up on datee's reveals");

            var playerResult = SessionSystemPromptBuilder.BuildPlayerAvatar("p", gd);
            var dateeResult = SessionSystemPromptBuilder.BuildDatee("o", gd);

            // BuildPlayerAvatar must NOT contain datee-only sections (DateeFriction,
            // DateeCuriosity). ConversationArcProgression is SHARED structure —
            // both sides participate in arc progression — kept in BuildPlayerAvatar.
            // PlayerProbing is player-specific guidance — kept in BuildPlayerAvatar.
            // See #867 LESSONS_LEARNED PROMPT-BLOAT-FROM-CROSS-ROLE-SECTIONS.
            Assert.DoesNotContain("DATEE RESISTANCE", playerResult);
            Assert.DoesNotContain("DATEE CURIOSITY", playerResult);
            Assert.DoesNotContain("datee resists", playerResult);
            Assert.DoesNotContain("datee probes", playerResult);

            // BuildDatee MUST contain all datee-side sections.
            Assert.Contains("DATEE RESISTANCE", dateeResult);
            Assert.Contains("DATEE CURIOSITY", dateeResult);
            Assert.Contains("CONVERSATION ARC", dateeResult);
            Assert.Contains("datee resists", dateeResult);
            Assert.Contains("datee probes", dateeResult);
            Assert.Contains("both sides move", dateeResult);

            // BuildPlayerAvatar keeps shared structure + player-side sections.
            Assert.Contains("CONVERSATION ARC", playerResult);
            Assert.Contains("both sides move", playerResult);
            Assert.Contains("PLAYER PROBING", playerResult);
            Assert.Contains("player follows", playerResult);

            // Token ceiling: BuildPlayerAvatar must be shorter than BuildDatee
            // (it excludes the two datee-only sections).
            Assert.True(playerResult.Length < dateeResult.Length,
                $"BuildPlayerAvatar ({playerResult.Length} chars) should be shorter than BuildDatee ({dateeResult.Length} chars)");
        }
    }
}

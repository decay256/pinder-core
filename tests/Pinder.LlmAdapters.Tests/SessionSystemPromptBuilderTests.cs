using System;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class SessionSystemPromptBuilderTests
    {
        private const string PlayerPrompt = "You are Velvet. Lowercase-with-intent. Ironic. Level 7 Veteran.";
        private const string OpponentPrompt = "You are Sable. Fast-talking. Uses omg and emoji. Level 5 Journeyman.";

        [Fact]
        public void Build_ContainsBothCharacterPrompts()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            Assert.Contains(PlayerPrompt, result);
            Assert.Contains(OpponentPrompt, result);
        }

        [Fact]
        public void Build_ContainsGameVision()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            Assert.Contains("comedy dating RPG", result);
        }

        [Fact]
        public void Build_ContainsWorldDescription()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            Assert.Contains("dating server", result);
        }

        [Fact]
        public void Build_ContainsMetaContract()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            Assert.Contains("break character", result);
        }

        [Fact]
        public void Build_ContainsWritingRules()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            Assert.Contains("texting register", result);
        }

        [Fact]
        public void Build_HasFiveSections()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            Assert.Contains("== GAME VISION ==", result);
            Assert.Contains("== WORLD RULES ==", result);
            Assert.Contains("== PLAYER CHARACTER ==", result);
            Assert.Contains("== OPPONENT CHARACTER ==", result);
            Assert.Contains("== META CONTRACT ==", result);
        }

        [Fact]
        public void Build_SectionsInCorrectOrder()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt);
            var visionIdx = result.IndexOf("== GAME VISION ==");
            var worldIdx = result.IndexOf("== WORLD RULES ==");
            var playerIdx = result.IndexOf("== PLAYER CHARACTER ==");
            var opponentIdx = result.IndexOf("== OPPONENT CHARACTER ==");
            var metaIdx = result.IndexOf("== META CONTRACT ==");

            Assert.True(visionIdx < worldIdx, "GAME VISION should come before WORLD RULES");
            Assert.True(worldIdx < playerIdx, "WORLD RULES should come before PLAYER CHARACTER");
            Assert.True(playerIdx < opponentIdx, "PLAYER CHARACTER should come before OPPONENT CHARACTER");
            Assert.True(opponentIdx < metaIdx, "OPPONENT CHARACTER should come before META CONTRACT");
        }

        [Fact]
        public void Build_NullGameDef_UsesPinderDefaults()
        {
            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt, null);
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
                "Custom opponent role",
                "Custom meta contract",
                "Custom writing rules");

            var result = SessionSystemPromptBuilder.Build(PlayerPrompt, OpponentPrompt, custom);
            Assert.Contains("Custom vision text", result);
            Assert.Contains("Custom world desc", result);
            Assert.Contains("Custom meta contract", result);
            Assert.Contains("Custom writing rules", result);
        }

        [Fact]
        public void Build_NullPlayerPrompt_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.Build(null!, OpponentPrompt));
        }

        [Fact]
        public void Build_NullOpponentPrompt_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SessionSystemPromptBuilder.Build(PlayerPrompt, null!));
        }

        [Fact]
        public void Build_EmptyPrompts_ProducesValidOutput()
        {
            var result = SessionSystemPromptBuilder.Build("", "");
            Assert.Contains("== PLAYER CHARACTER ==", result);
            Assert.Contains("== OPPONENT CHARACTER ==", result);
        }

        [Fact]
        public void Build_MetaContractIncludesWritingRules()
        {
            var custom = new GameDefinition(
                "G", "V", "W", "P", "O",
                "MetaSection",
                "WritingSection");

            var result = SessionSystemPromptBuilder.Build("p", "o", custom);
            var metaIdx = result.IndexOf("== META CONTRACT ==");
            var afterMeta = result.Substring(metaIdx);
            Assert.Contains("MetaSection", afterMeta);
            Assert.Contains("WritingSection", afterMeta);
        }

        // Regression: #867 — opponent-only sections must NOT leak into BuildPlayer.
        [Fact]
        public void BuildPlayer_ExcludesOpponentOnlySections()
        {
            var gd = new GameDefinition(
                "T", "V", "W", "P", "O", "M", "WR",
                opponentFriction: "opponent resists the player",
                opponentCuriosity: "opponent probes player's bio",
                conversationArcProgression: "both sides move the convo forward",
                playerProbing: "player follows up on opponent's reveals");

            var playerResult = SessionSystemPromptBuilder.BuildPlayer("p", gd);
            var opponentResult = SessionSystemPromptBuilder.BuildOpponent("o", gd);

            // BuildPlayer must NOT contain opponent-only sections (OpponentFriction,
            // OpponentCuriosity, ConversationArcProgression — all three describe
            // how the opponent should behave). PlayerProbing IS player-specific guidance
            // — kept in BuildPlayer. See #867.
            // ConversationArc is SHARED structure — kept in both. Only opponent-only
            // sections (OpponentFriction, OpponentCuriosity) are stripped.
            Assert.DoesNotContain("OPPONENT RESISTANCE", playerResult);
            Assert.DoesNotContain("OPPONENT CURIOSITY", playerResult);
            Assert.DoesNotContain("opponent resists", playerResult);
            Assert.DoesNotContain("opponent probes", playerResult);

            // BuildOpponent MUST contain all opponent-side sections.
            Assert.Contains("OPPONENT RESISTANCE", opponentResult);
            Assert.Contains("OPPONENT CURIOSITY", opponentResult);
            Assert.Contains("CONVERSATION ARC", opponentResult);
            Assert.Contains("opponent resists", opponentResult);
            Assert.Contains("opponent probes", opponentResult);
            Assert.Contains("both sides move", opponentResult);

            // BuildPlayer keeps shared structure + player-side sections.
            Assert.Contains("CONVERSATION ARC", playerResult);
            Assert.Contains("both sides move", playerResult);
            Assert.Contains("PLAYER PROBING", playerResult);
            Assert.Contains("player follows", playerResult);

            // Token ceiling: BuildPlayer must be shorter than BuildOpponent
            // (it excludes the two opponent-only sections).
            Assert.True(playerResult.Length < opponentResult.Length,
                $"BuildPlayer ({playerResult.Length} chars) should be shorter than BuildOpponent ({opponentResult.Length} chars)");
        }
    }
}

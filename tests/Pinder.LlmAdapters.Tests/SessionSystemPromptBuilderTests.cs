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
    }
}

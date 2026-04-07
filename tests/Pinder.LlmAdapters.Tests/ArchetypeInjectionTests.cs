using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Tests that active archetype directives are injected into LLM prompts (#649).
    /// </summary>
    public class ArchetypeInjectionTests
    {
        private static DialogueContext MakeDialogueContext(string activeArchetypeDirective = null)
        {
            return new DialogueContext(
                playerPrompt: "You are a test player.",
                opponentPrompt: "You are a test opponent.",
                conversationHistory: Array.Empty<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerName: "TestPlayer",
                opponentName: "TestOpponent",
                currentTurn: 1,
                activeArchetypeDirective: activeArchetypeDirective);
        }

        private static OpponentContext MakeOpponentContext(string activeArchetypeDirective = null)
        {
            return new OpponentContext(
                playerPrompt: "You are a test player.",
                opponentPrompt: "You are a test opponent.",
                conversationHistory: Array.Empty<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "hey",
                interestBefore: 10,
                interestAfter: 12,
                responseDelayMinutes: 2.0,
                playerName: "TestPlayer",
                opponentName: "TestOpponent",
                currentTurn: 1,
                activeArchetypeDirective: activeArchetypeDirective);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_WithArchetypeDirective_InjectsIt()
        {
            var ctx = MakeDialogueContext("ACTIVE ARCHETYPE: The Peacock (clear)\nShows off constantly.");
            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.Contains("ACTIVE ARCHETYPE: The Peacock (clear)", prompt);
            Assert.Contains("Shows off constantly.", prompt);
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_WithoutArchetypeDirective_DoesNotInject()
        {
            var ctx = MakeDialogueContext(null);
            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(ctx);
            Assert.DoesNotContain("ACTIVE ARCHETYPE:", prompt);
        }

        [Fact]
        public void BuildOpponentPrompt_WithArchetypeDirective_InjectsIt()
        {
            var ctx = MakeOpponentContext("ACTIVE ARCHETYPE: The Love Bomber (dominant)\nOverwhelming affection.");
            string prompt = SessionDocumentBuilder.BuildOpponentPrompt(ctx);
            Assert.Contains("ACTIVE ARCHETYPE: The Love Bomber (dominant)", prompt);
            Assert.Contains("Overwhelming affection.", prompt);
        }

        [Fact]
        public void BuildOpponentPrompt_WithoutArchetypeDirective_DoesNotInject()
        {
            var ctx = MakeOpponentContext(null);
            string prompt = SessionDocumentBuilder.BuildOpponentPrompt(ctx);
            Assert.DoesNotContain("ACTIVE ARCHETYPE:", prompt);
        }
    }
}

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
    [Collection("PromptTraceSingleton")]
    public class ArchetypeInjectionTests
    {
        private static DialogueContext MakeDialogueContext(string activeArchetypeDirective = null)
        {
            return new DialogueContext(
                playerAvatarPrompt: "You are a test player.",
                dateePrompt: "You are a test datee.",
                conversationHistory: Array.Empty<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerName: "TestPlayer",
                dateeName: "TestDatee",
                currentTurn: 1,
                activeArchetypeDirective: activeArchetypeDirective, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  });
        }

        private static DateeContext MakeDateeContext(string activeArchetypeDirective = null)
        {
            return new DateeContext(
                dateePrompt: "You are a test datee.",
                conversationHistory: Array.Empty<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "hey",
                interestBefore: 10,
                interestAfter: 12,
                responseDelayMinutes: 2.0,
                playerName: "TestPlayer",
                dateeName: "TestDatee",
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
        public void BuildDateePrompt_WithArchetypeDirective_InjectsIt()
        {
            var ctx = MakeDateeContext("ACTIVE ARCHETYPE: The Love Bomber (dominant)\nOverwhelming affection.");
            string prompt = SessionDocumentBuilder.BuildDateePrompt(ctx);
            Assert.Contains("ACTIVE ARCHETYPE: The Love Bomber (dominant)", prompt);
            Assert.Contains("Overwhelming affection.", prompt);
        }

        [Fact]
        public void BuildDateePrompt_WithoutArchetypeDirective_DoesNotInject()
        {
            var ctx = MakeDateeContext(null);
            string prompt = SessionDocumentBuilder.BuildDateePrompt(ctx);
            Assert.DoesNotContain("ACTIVE ARCHETYPE:", prompt);
        }
    }
}

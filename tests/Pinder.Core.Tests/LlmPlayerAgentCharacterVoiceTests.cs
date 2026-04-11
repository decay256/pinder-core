using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for LlmPlayerAgent character-driven improvements (#492).
    /// </summary>
    [Trait("Category", "Core")]
    public class LlmPlayerAgentCharacterVoiceTests
    {
        private static StatBlock MakeStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 4 }, { StatType.Rizz, 1 }, { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static TurnStart MakeTurnStart()
        {
            var opts = new[]
            {
                new DialogueOption(StatType.Charm, "Hey gorgeous"),
                new DialogueOption(StatType.Honesty, "I'm actually nervous")
            };
            var state = new GameStateSnapshot(12, InterestState.Interested, 0, Array.Empty<string>(), 3);
            return new TurnStart(opts, state);
        }

        [Fact]
        public void BuildPrompt_WithPlayerSystemPrompt_IncludesCharacterContext()
        {
            var apiOpts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(apiOpts, new ScoringPlayerAgent(), "Gerald", "Velvet");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 3,
                playerSystemPrompt: "You are Gerald, a nervous overachiever.",
                playerName: "Gerald",
                opponentName: "Velvet");

            string prompt = agent.BuildPrompt(turn, context);

            // Should use player name from context
            Assert.Contains("Gerald", prompt);
            Assert.Contains("Velvet", prompt);
        }

        [Fact]
        public void BuildPrompt_WithRecentHistory_IncludesConversation()
        {
            var apiOpts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(apiOpts, new ScoringPlayerAgent(), "Gerald", "Velvet");
            var turn = MakeTurnStart();
            var history = new List<(string Sender, string Text)>
            {
                ("Gerald", "Hey there!"),
                ("Velvet", "Oh hey, what's up?"),
                ("Gerald", "Just thinking about you."),
                ("Velvet", "That's sweet.")
            };
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 3,
                playerName: "Gerald",
                opponentName: "Velvet",
                recentHistory: history.AsReadOnly());

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Recent Conversation", prompt);
            Assert.Contains("Hey there!", prompt);
            Assert.Contains("That's sweet.", prompt);
        }

        [Fact]
        public void BuildPrompt_WithoutHistory_NoConversationSection()
        {
            var apiOpts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(apiOpts, new ScoringPlayerAgent(), "Gerald", "Velvet");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 3);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.DoesNotContain("Recent Conversation", prompt);
        }

        [Fact]
        public void BuildPrompt_CharacterVoice_AsksForGenuineChoice()
        {
            var apiOpts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(apiOpts, new ScoringPlayerAgent(), "Gerald", "Velvet");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 3,
                playerName: "Gerald");

            string prompt = agent.BuildPrompt(turn, context);

            // Should ask for character-driven reasoning, not just strategic
            Assert.Contains("genuinely type", prompt);
            Assert.Contains("character's voice", prompt);
        }
    }
}

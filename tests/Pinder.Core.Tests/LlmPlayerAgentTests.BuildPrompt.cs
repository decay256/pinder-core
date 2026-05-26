using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class LlmPlayerAgentTests
    {
        // ── BuildPrompt tests ─────────────────────────────────────

        [Fact]
        public void BuildPrompt_ContainsInterestAndState()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Interest: 12/25 (Interested)", prompt);
            Assert.Contains("Momentum: 2 consecutive wins", prompt);
            Assert.Contains("Turn: 5", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsAllOptions()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("A) [CHARM", prompt);
            Assert.Contains("B) [RIZZ", prompt);
            Assert.Contains("C) [HONESTY", prompt);
            Assert.Contains("D) [CHAOS", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsBonusIcons()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            // Option C has tell bonus
            Assert.Contains("📖", prompt);
            // Option D has combo
            Assert.Contains("⭐", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsCharacterNames()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Sable", prompt);
            Assert.Contains("Brick", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsRulesReminder()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("## Rules Reminder", prompt);
            Assert.Contains("submit_choice", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsShadowValues()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Denial: 3/18", prompt);
            Assert.Contains("Fixation: 1/18", prompt);
            Assert.Contains("Despair: 4/18", prompt);
        }

        [Fact]
        public void BuildPrompt_NullShadows_ShowsUnknown()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 5);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Shadow tracking unavailable", prompt);
        }

        [Fact]
        public void BuildPrompt_BoredState_ShowsDisadvantage()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 3, InterestState.Bored, 0,
                Array.Empty<string>(), 0, null, 2);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("grants disadvantage", prompt);
        }

        [Fact]
        public void BuildPrompt_VeryIntoIt_ShowsAdvantage()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 18, InterestState.VeryIntoIt, 0,
                Array.Empty<string>(), 0, null, 8);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("grants advantage", prompt);
        }

        [Fact]
        public void BuildPrompt_MomentumAt3_ShowsBonus()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 3,
                Array.Empty<string>(), 0, null, 5);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("(+2 to next roll)", prompt);
        }

        [Fact]
        public void BuildPrompt_MomentumAt5_ShowsHigherBonus()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 5,
                Array.Empty<string>(), 0, null, 5);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("(+3 to next roll)", prompt);
        }

        [Fact]
        public void BuildPrompt_ActiveTraps_ShowsTrapNames()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                new[] { "Madness", "Dread" }, 0, null, 5);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Active traps: Madness, Dread", prompt);
        }

        [Fact]
        public void BuildPrompt_NoTraps_ShowsNone()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Active traps: none", prompt);
        }

        [Fact]
        public void BuildPrompt_CallbackOption_ShowsIcon()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey", callbackTurnNumber: 2),
                new DialogueOption(StatType.Rizz, "Yo")
            };
            var turn = MakeTurnStart(options);
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("🔗", prompt);
        }

        [Fact]
        public void BuildPrompt_WeaknessWindow_ShowsIcon()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey", hasWeaknessWindow: true),
                new DialogueOption(StatType.Rizz, "Yo")
            };
            var turn = MakeTurnStart(options);
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("🔓", prompt);
        }

        [Fact]
        public void BuildPrompt_SingleOption_ShowsOnlyA()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var options = new[] { new DialogueOption(StatType.Rizz, "Only option") };
            var turn = MakeTurnStart(options);
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("A) [RIZZ", prompt);
            Assert.DoesNotContain("B)", prompt);
        }

        [Fact]
        public void BuildPrompt_ShowsIntendedText()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("\"Hey gorgeous\"", prompt);
            Assert.Contains("\"I'm nervous\"", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsShadowThresholdRules()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("T1", prompt);
            Assert.Contains("T2", prompt);
            Assert.Contains("T3", prompt);
            Assert.Contains("Shadow Threshold Rules", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsStrategySection()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("PRIMARY GOAL", prompt);
            Assert.Contains("SECONDARY", prompt);
            Assert.Contains("NARRATIVE GAMBLE", prompt);
        }

        [Fact]
        public void BuildPrompt_HighShadow_ShowsThresholdWarning()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0,
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 13 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 5 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                },
                turnNumber: 5);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("T2", prompt);
            Assert.Contains("Madness: 13/18", prompt);
            Assert.Contains("approaching T1", prompt); // Denial at 5
        }
    }
}

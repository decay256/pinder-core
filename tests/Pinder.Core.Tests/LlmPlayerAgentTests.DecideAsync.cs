using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class LlmPlayerAgentTests
    {
        // ── DecideAsync validation tests ─────────────────────────────────

        [Fact]
        public async Task DecideAsync_NullTurn_Throws()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent());
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                agent.DecideAsync(null!, MakeContext()));
        }

        [Fact]
        public async Task DecideAsync_NullContext_Throws()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent());
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                agent.DecideAsync(MakeTurnStart(), null!));
        }

        [Fact]
        public async Task DecideAsync_EmptyOptions_Throws()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent());
            var turn = new TurnStart(Array.Empty<DialogueOption>(),
                new GameStateSnapshot(12, InterestState.Interested, 0, Array.Empty<string>(), 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                agent.DecideAsync(turn, MakeContext()));
        }

        [Fact]
        public async Task DecideAsync_ApiFailure_FallsBackToScoringAgent()
        {
            // Use an invalid API key that will cause the client constructor to fail on first call
            // Since the API key "test-key" isn't valid, any actual HTTP call will fail
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");

            var turn = MakeTurnStart();
            var context = MakeContext();

            // This will fail because we can't reach the Anthropic API with a fake key
            // It should fall back to ScoringPlayerAgent
            var decision = await agent.DecideAsync(turn, context);

            Assert.NotNull(decision);
            Assert.Contains("[LLM fallback:", decision.Reasoning);
            Assert.InRange(decision.OptionIndex, 0, turn.Options.Length - 1);
            Assert.Equal(turn.Options.Length, decision.Scores.Length);
        }

        [Fact]
        public async Task DecideAsync_Fallback_ScoresAlwaysPopulated()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");

            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            // Scores should always be populated (from ScoringPlayerAgent), even on LLM failure
            Assert.NotNull(decision.Scores);
            Assert.Equal(4, decision.Scores.Length);
            for (int i = 0; i < decision.Scores.Length; i++)
            {
                Assert.Equal(i, decision.Scores[i].OptionIndex);
                Assert.InRange(decision.Scores[i].SuccessChance, 0.0f, 1.0f);
            }
        }

        [Fact]
        public async Task DecideAsync_Fallback_LastExplanationIsEmpty()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");

            var turn = MakeTurnStart();
            var context = MakeContext();

            // Fake API key → falls back to scoring agent
            var decision = await agent.DecideAsync(turn, context);

            // On fallback, LastExplanation should be empty
            Assert.Equal("", agent.LastExplanation);
        }

        [Fact]
        public async Task DecideAsync_Fallback_ReturnsValidIndex()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");

            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            // Should return a valid index within range
            Assert.InRange(decision.OptionIndex, 0, turn.Options.Length - 1);
        }
    }
}

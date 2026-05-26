using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public partial class LlmPlayerAgentTests
    {
        // Helper to build standard test fixtures
        private static StatBlock MakeStats(int charm = 4, int rizz = 1, int honesty = 3, int chaos = 2, int wit = 2, int sa = 3)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, wit },
                    { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        private static DialogueOption[] MakeOptions()
        {
            return new[]
            {
                new DialogueOption(StatType.Charm, "Hey gorgeous"),
                new DialogueOption(StatType.Rizz, "Your curves are math"),
                new DialogueOption(StatType.Honesty, "I'm nervous", hasTellBonus: true),
                new DialogueOption(StatType.Chaos, "I fought a raccoon", comboName: "WitChaosSA")
            };
        }

        private static TurnStart MakeTurnStart(DialogueOption[]? options = null)
        {
            var opts = options ?? MakeOptions();
            var state = new GameStateSnapshot(12, InterestState.Interested, 2, Array.Empty<string>(), 5);
            return new TurnStart(opts, state);
        }

        private static PlayerAgentContext MakeContext()
        {
            return new PlayerAgentContext(
                playerStats: MakeStats(),
                opponentStats: MakeStats(charm: 2, rizz: 3, honesty: 2, chaos: 2, wit: 2, sa: 2),
                currentInterest: 12,
                interestState: InterestState.Interested,
                momentumStreak: 2,
                activeTrapNames: Array.Empty<string>(),
                sessionHorniness: 4,
                shadowValues: new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Denial, 3 },
                    { ShadowStatType.Fixation, 1 },
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Despair, 4 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 2 }
                },
                turnNumber: 5);
        }

        // ── Constructor tests ─────────────────────────────────────

        [Fact]
        public void Constructor_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(null!, new ScoringPlayerAgent()));
        }

        [Fact]
        public void Constructor_NullFallback_Throws()
        {
            // Note: this will throw ArgumentException from AnthropicClient if API key is empty,
            // but the null check for fallback should fire first since options is valid
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(options, null!));
        }

        // ── ParsePick tests ─────────────────────────────────────

        [Theory]
        [InlineData("PICK: A", 4, 0)]
        [InlineData("PICK: B", 4, 1)]
        [InlineData("PICK: C", 4, 2)]
        [InlineData("PICK: D", 4, 3)]
        [InlineData("PICK: [A]", 4, 0)]
        [InlineData("PICK: [B]", 4, 1)]
        [InlineData("PICK:[C]", 4, 2)]
        [InlineData("pick: a", 4, 0)]
        [InlineData("pick: [d]", 4, 3)]
        [InlineData("Pick: B", 4, 1)]
        public void ParsePick_ValidFormats_ReturnsCorrectIndex(string input, int optionCount, int expected)
        {
            int? result = LlmPlayerAgent.ParsePick(input, optionCount);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParsePick_MultiplePickLines_UsesLast()
        {
            string text = "I think A is good.\nPICK: A\nActually, C is better.\nPICK: C";
            int? result = LlmPlayerAgent.ParsePick(text, 4);
            Assert.Equal(2, result);
        }

        [Theory]
        [InlineData("No pick here", 4)]
        [InlineData("", 4)]
        [InlineData("PICK: E", 4)]  // out of range
        [InlineData("PICK: D", 3)]  // D is index 3 but only 3 options
        public void ParsePick_InvalidInput_ReturnsNull(string input, int optionCount)
        {
            int? result = LlmPlayerAgent.ParsePick(input, optionCount);
            Assert.Null(result);
        }

        [Fact]
        public void ParsePick_NullInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick(null!, 4));
        }

        [Fact]
        public void ParsePick_EmptyInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("", 4));
        }

        [Fact]
        public void ParsePick_SingleOption_AcceptsOnlyA()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK: A", 1));
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: B", 1));
        }

        [Fact]
        public void LastExplanation_InitiallyEmpty()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");

            Assert.Equal("", agent.LastExplanation);
        }
    }
}

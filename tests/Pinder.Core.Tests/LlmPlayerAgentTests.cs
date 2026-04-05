using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    public class LlmPlayerAgentTests
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
                    { ShadowStatType.Horniness, 0 },
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
                    { ShadowStatType.Horniness, 4 },
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
        public void BuildPrompt_ContainsPlayerName()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            // Player name appears in task instruction
            Assert.Contains("Sable", prompt);
        }

        [Fact]
        public void BuildSystemMessage_ContainsCharacterNames()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick",
                playerSystemPrompt: "You are a dramatic character.");
            string systemMsg = agent.BuildSystemMessage();

            Assert.Contains("Sable", systemMsg);
            Assert.Contains("Brick", systemMsg);
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
            Assert.Contains("PICK: [A/B/C/D]", prompt);
        }

        [Fact]
        public void BuildPrompt_ContainsShadowValues()
        {
            var options = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(options, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Denial 3", prompt);
            Assert.Contains("Fixation 1", prompt);
            Assert.Contains("Horniness 4", prompt);
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

            Assert.Contains("Shadow levels: unknown", prompt);
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

        // ── BuildPrompt single option test ─────────────────────────────────

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

        // ── BuildPrompt option text formatting ─────────────────────────────

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

        // ── BuildSystemMessage tests ─────────────────────────────────────

        [Fact]
        public void BuildSystemMessage_NoCharacterContext_ReturnsGeneric()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            string systemMsg = agent.BuildSystemMessage();

            Assert.Contains("strategic player in Pinder", systemMsg);
            Assert.DoesNotContain("personality and voice", systemMsg);
        }

        [Fact]
        public void BuildSystemMessage_WithSystemPrompt_IncludesCharacterIdentity()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick",
                playerSystemPrompt: "You are Sable, a dramatic artisanal penis.");
            string systemMsg = agent.BuildSystemMessage();

            Assert.Contains("You are playing as Sable", systemMsg);
            Assert.Contains("talking to Brick", systemMsg);
            Assert.Contains("artisanal penis", systemMsg);
            Assert.Contains("personality and voice", systemMsg);
        }

        [Fact]
        public void BuildSystemMessage_WithTextingStyle_IncludesStyle()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick",
                playerTextingStyle: "lowercase, ironic, precise");
            string systemMsg = agent.BuildSystemMessage();

            Assert.Contains("lowercase, ironic, precise", systemMsg);
            Assert.Contains("personality and voice", systemMsg);
        }

        [Fact]
        public void BuildSystemMessage_WithBoth_IncludesAll()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick",
                playerSystemPrompt: "Dramatic character.", playerTextingStyle: "lowercase vibes");
            string systemMsg = agent.BuildSystemMessage();

            Assert.Contains("Dramatic character.", systemMsg);
            Assert.Contains("lowercase vibes", systemMsg);
            Assert.Contains("character fit", systemMsg);
        }

        // ── Conversation history tests ─────────────────────────────────────

        [Fact]
        public void BuildPrompt_WithConversationHistory_IncludesHistory()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var history = new List<(string Sender, string Text)>
            {
                ("Sable", "hey there"),
                ("Brick", "oh hi lol"),
                ("Sable", "nice hat"),
                ("Brick", "thanks i grew it myself")
            };
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(charm: 2, rizz: 3, honesty: 2, chaos: 2, wit: 2, sa: 2),
                12, InterestState.Interested, 2, Array.Empty<string>(), 4,
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Denial, 3 }, { ShadowStatType.Fixation, 1 },
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 4 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 2 }
                },
                5, conversationHistory: history);

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("## Conversation So Far", prompt);
            Assert.Contains("Sable: hey there", prompt);
            Assert.Contains("Brick: oh hi lol", prompt);
            Assert.Contains("Brick: thanks i grew it myself", prompt);
        }

        [Fact]
        public void BuildPrompt_NullConversationHistory_OmitsSection()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext(); // null conversation history by default

            string prompt = agent.BuildPrompt(turn, context);

            Assert.DoesNotContain("Conversation So Far", prompt);
        }

        [Fact]
        public void BuildPrompt_EmptyConversationHistory_OmitsSection()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 1,
                conversationHistory: new List<(string, string)>());

            string prompt = agent.BuildPrompt(turn, context);

            Assert.DoesNotContain("Conversation So Far", prompt);
        }

        // ── Scoring advisory tests ─────────────────────────────────────

        [Fact]
        public void BuildPrompt_WithScoringDecision_IncludesAdvisory()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();
            var scores = new[]
            {
                new OptionScore(0, 1.2f, 0.60f, 0.4f, Array.Empty<string>()),
                new OptionScore(1, 2.8f, 0.75f, 1.1f, Array.Empty<string>()),
                new OptionScore(2, 0.9f, 0.40f, -0.1f, Array.Empty<string>()),
                new OptionScore(3, 2.1f, 0.65f, 0.7f, Array.Empty<string>())
            };
            var scoringDecision = new PlayerDecision(1, "EV pick", scores);

            string prompt = agent.BuildPrompt(turn, context, scoringDecision);

            Assert.Contains("## Scoring Agent Advisory", prompt);
            Assert.Contains("← scorer pick", prompt);
            Assert.Contains("Score: 2.80", prompt);
        }

        [Fact]
        public void BuildPrompt_WithoutScoringDecision_OmitsAdvisory()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.DoesNotContain("Scoring Agent Advisory", prompt);
        }

        // ── Task instruction tests ─────────────────────────────────────

        [Fact]
        public void BuildPrompt_ContainsCharacterAwareTaskInstruction()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("fits Sable's personality", prompt);
            Assert.Contains("narrative moment", prompt);
            Assert.Contains("PICK: [A/B/C/D]", prompt);
        }

        // ── Constructor with character context tests ─────────────────────

        [Fact]
        public void Constructor_WithCharacterContext_DoesNotThrow()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick",
                playerSystemPrompt: "You are Sable.", playerTextingStyle: "lowercase ironic");
            Assert.NotNull(agent);
        }

        [Fact]
        public void Constructor_NullPromptAndStyle_DefaultsToEmpty()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            using var agent = new LlmPlayerAgent(opts, new ScoringPlayerAgent(), "Sable", "Brick",
                playerSystemPrompt: null!, playerTextingStyle: null!);
            // Should not throw and should fall back to generic system message
            string systemMsg = agent.BuildSystemMessage();
            Assert.Contains("strategic player in Pinder", systemMsg);
        }

        // ── PlayerAgentContext conversation history tests ─────────────────

        [Fact]
        public void PlayerAgentContext_ConversationHistory_DefaultsToNull()
        {
            var context = MakeContext();
            Assert.Null(context.ConversationHistory);
        }

        [Fact]
        public void PlayerAgentContext_ConversationHistory_CanBeSet()
        {
            var history = new List<(string, string)> { ("A", "hello"), ("B", "hi") };
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 1,
                conversationHistory: history);
            Assert.NotNull(context.ConversationHistory);
            Assert.Equal(2, context.ConversationHistory!.Count);
        }
    }
}

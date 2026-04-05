using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #492 — LlmPlayerAgent: Sonnet makes option choices
    /// based on character fit and narrative moment.
    /// Spec: docs/specs/issue-492-spec.md
    /// </summary>
    public class LlmPlayerAgentSpecTests
    {
        // ── Test fixtures ──────────────────────────────────────────

        private static StatBlock MakeStats(int charm = 4, int rizz = 1, int honesty = 3,
            int chaos = 2, int wit = 2, int sa = 3)
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

        private static PlayerAgentContext MakeContext(
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            int turnNumber = 5)
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
                turnNumber: turnNumber,
                conversationHistory: conversationHistory);
        }

        private static LlmPlayerAgent MakeAgent(
            string playerName = "Sable",
            string opponentName = "Brick",
            string playerSystemPrompt = "",
            string playerTextingStyle = "")
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            return new LlmPlayerAgent(opts, new ScoringPlayerAgent(),
                playerName, opponentName, playerSystemPrompt, playerTextingStyle);
        }

        // ── AC1: LlmPlayerAgent implements IPlayerAgent ────────────────

        // Mutation: would catch if LlmPlayerAgent didn't implement IPlayerAgent interface
        [Fact]
        public void AC1_LlmPlayerAgent_ImplementsIPlayerAgent()
        {
            using var agent = MakeAgent();
            Assert.IsAssignableFrom<IPlayerAgent>(agent);
        }

        // Mutation: would catch if constructor didn't accept character context params
        [Fact]
        public void AC1_Constructor_AcceptsCharacterContext()
        {
            using var agent = MakeAgent(
                playerName: "Velvet",
                opponentName: "Sable",
                playerSystemPrompt: "You are Velvet, a refined seducer.",
                playerTextingStyle: "lowercase-with-intent, precise, ironic");
            Assert.NotNull(agent);
        }

        // ── AC2: Reasoning block in output ─────────────────────────────

        // Mutation: would catch if fallback didn't populate Reasoning field
        [Fact]
        public async Task AC2_FallbackDecision_HasReasoningText()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.NotNull(decision.Reasoning);
            Assert.NotEmpty(decision.Reasoning);
        }

        // Mutation: would catch if fallback reasoning didn't include LLM fallback marker
        [Fact]
        public async Task AC2_FallbackDecision_ReasoningContainsFallbackMarker()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.Contains("[LLM fallback:", decision.Reasoning);
        }

        // ── AC4: LLM call failure falls back to ScoringPlayerAgent ─────

        // Mutation: would catch if fallback didn't produce a valid option index
        [Fact]
        public async Task AC4_Fallback_ReturnsValidOptionIndex()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.InRange(decision.OptionIndex, 0, turn.Options.Length - 1);
        }

        // Mutation: would catch if Scores array was empty or wrong length on fallback
        [Fact]
        public async Task AC4_Fallback_ScoresArrayMatchesOptionCount()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.Equal(turn.Options.Length, decision.Scores.Length);
        }

        // Mutation: would catch if fallback didn't include reason detail in brackets
        [Fact]
        public async Task AC4_Fallback_ReasoningHasClosingBracket()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            // [LLM fallback: <reason>] must have closing bracket
            Assert.Contains("]", decision.Reasoning);
        }

        // ── System message: character-aware vs generic ─────────────────

        // Mutation: would catch if system message didn't include player name when provided
        [Fact]
        public void SystemMessage_WithCharacterContext_ContainsPlayerName()
        {
            using var agent = MakeAgent(playerName: "Velvet", opponentName: "Brick",
                playerSystemPrompt: "You are Velvet, the seducer.");

            string sysMsg = agent.BuildSystemMessage();

            Assert.Contains("Velvet", sysMsg);
        }

        // Mutation: would catch if system message didn't include opponent name
        [Fact]
        public void SystemMessage_WithCharacterContext_ContainsOpponentName()
        {
            using var agent = MakeAgent(playerName: "Velvet", opponentName: "Brick",
                playerSystemPrompt: "You are Velvet, the seducer.");

            string sysMsg = agent.BuildSystemMessage();

            Assert.Contains("Brick", sysMsg);
        }

        // Mutation: would catch if system message didn't include system prompt content
        [Fact]
        public void SystemMessage_WithSystemPrompt_ContainsPromptContent()
        {
            using var agent = MakeAgent(playerSystemPrompt: "You are a sophisticated flirt with dry humor.");

            string sysMsg = agent.BuildSystemMessage();

            Assert.Contains("sophisticated flirt", sysMsg);
        }

        // Mutation: would catch if system message didn't include texting style when provided
        [Fact]
        public void SystemMessage_WithTextingStyle_ContainsStyleText()
        {
            using var agent = MakeAgent(playerTextingStyle: "lowercase-with-intent, precise, ironic");

            string sysMsg = agent.BuildSystemMessage();

            Assert.Contains("lowercase-with-intent", sysMsg);
        }

        // Mutation: would catch if system message didn't fall back to generic when both empty
        [Fact]
        public void SystemMessage_BothEmpty_FallsBackToGeneric()
        {
            using var agent = MakeAgent(playerSystemPrompt: "", playerTextingStyle: "");

            string sysMsg = agent.BuildSystemMessage();

            // Generic prompt should contain strategic/player language, not character-specific
            Assert.DoesNotContain("personality", sysMsg.ToLowerInvariant().Split("choose")[0]);
            // But it should still be a valid non-empty system message
            Assert.NotEmpty(sysMsg);
        }

        // Mutation: would catch if character-aware system msg didn't mention character fit priority
        [Fact]
        public void SystemMessage_WithCharacterContext_MentionsCharacterFit()
        {
            using var agent = MakeAgent(playerName: "Sable",
                playerSystemPrompt: "You are Sable, chaotic energy.");

            string sysMsg = agent.BuildSystemMessage();

            // Spec: "character fit and narrative moment take priority over pure optimization"
            Assert.Contains("character", sysMsg.ToLowerInvariant());
        }

        // ── Prompt: conversation history ───────────────────────────────

        // Mutation: would catch if conversation history wasn't included in prompt
        [Fact]
        public void BuildPrompt_WithHistory_ContainsConversationMessages()
        {
            using var agent = MakeAgent();
            var history = new List<(string, string)>
            {
                ("Sable", "hey, what's your deal?"),
                ("Brick", "i bench press feelings"),
                ("Sable", "that's either deep or concerning")
            };
            var context = MakeContext(conversationHistory: history);
            var turn = MakeTurnStart();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("hey, what's your deal?", prompt);
            Assert.Contains("i bench press feelings", prompt);
            Assert.Contains("that's either deep or concerning", prompt);
        }

        // Mutation: would catch if sender names weren't included with messages
        [Fact]
        public void BuildPrompt_WithHistory_ContainsSenderNames()
        {
            using var agent = MakeAgent();
            var history = new List<(string, string)>
            {
                ("Sable", "hello there"),
                ("Brick", "hey")
            };
            var context = MakeContext(conversationHistory: history);
            var turn = MakeTurnStart();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Sable", prompt);
            Assert.Contains("Brick", prompt);
        }

        // Mutation: would catch if null history caused a crash instead of omission
        [Fact]
        public void BuildPrompt_NullHistory_DoesNotCrash()
        {
            using var agent = MakeAgent();
            var context = MakeContext(conversationHistory: null);
            var turn = MakeTurnStart();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.NotEmpty(prompt);
        }

        // Mutation: would catch if empty list wasn't treated same as null (section omitted)
        [Fact]
        public void BuildPrompt_EmptyHistory_OmitsConversationSection()
        {
            using var agent = MakeAgent();
            var context = MakeContext(conversationHistory: new List<(string, string)>());
            var turn = MakeTurnStart();

            string prompt = agent.BuildPrompt(turn, context);

            // Should not contain conversation section header when empty
            Assert.DoesNotContain("CONVERSATION SO FAR", prompt);
        }

        // ── Prompt: scoring advisory ───────────────────────────────────

        // Mutation: would catch if scoring advisory wasn't included when scoring decision provided
        [Fact]
        public void BuildPrompt_WithScoringDecision_ContainsScorerPick()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var scores = new[]
            {
                new OptionScore(0, 1.2f, 0.60f, 0.4f, Array.Empty<string>()),
                new OptionScore(1, 2.8f, 0.75f, 1.1f, Array.Empty<string>()),
                new OptionScore(2, 0.9f, 0.40f, -0.1f, Array.Empty<string>()),
                new OptionScore(3, 2.1f, 0.65f, 0.7f, Array.Empty<string>())
            };
            var scoringDecision = new PlayerDecision(1, "Wit is best EV", scores);

            string prompt = agent.BuildPrompt(turn, context, scoringDecision);

            // Spec: highest-scoring option marked with "← scorer pick"
            Assert.Contains("scorer pick", prompt.ToLowerInvariant());
        }

        // Mutation: would catch if advisory section title was missing
        [Fact]
        public void BuildPrompt_WithScoringDecision_ContainsAdvisoryHeader()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var scores = new[]
            {
                new OptionScore(0, 1.0f, 0.50f, 0.2f, Array.Empty<string>()),
                new OptionScore(1, 2.0f, 0.70f, 0.8f, Array.Empty<string>()),
                new OptionScore(2, 0.5f, 0.30f, -0.3f, Array.Empty<string>()),
                new OptionScore(3, 1.5f, 0.60f, 0.5f, Array.Empty<string>())
            };
            var scoringDecision = new PlayerDecision(1, "B is best", scores);

            string prompt = agent.BuildPrompt(turn, context, scoringDecision);

            Assert.Contains("Scoring Agent Advisory", prompt);
        }

        // Mutation: would catch if advisory was included when no scoring decision provided
        [Fact]
        public void BuildPrompt_WithoutScoringDecision_OmitsAdvisory()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context, scoringDecision: null);

            Assert.DoesNotContain("Scoring Agent Advisory", prompt);
        }

        // ── Prompt: task instruction ───────────────────────────────────

        // Mutation: would catch if task didn't include character name for personalized reasoning
        [Fact]
        public void BuildPrompt_TaskInstruction_ContainsPlayerNameForFit()
        {
            using var agent = MakeAgent(playerName: "Velvet");
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            // Spec: "Which option fits {playerName}'s personality right now?"
            Assert.Contains("Velvet", prompt);
        }

        // Mutation: would catch if task didn't include PICK format instruction
        [Fact]
        public void BuildPrompt_TaskInstruction_ContainsPickFormat()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("PICK:", prompt);
        }

        // Mutation: would catch if task didn't mention narrative/conversation context
        [Fact]
        public void BuildPrompt_TaskInstruction_MentionsNarrativeMoment()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("narrative", prompt.ToLowerInvariant());
        }

        // ── Prompt: game state ─────────────────────────────────────────

        // Mutation: would catch if interest value wasn't in prompt
        [Fact]
        public void BuildPrompt_ContainsCurrentInterest()
        {
            using var agent = MakeAgent();
            var context = MakeContext();
            var turn = MakeTurnStart();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("12", prompt);
        }

        // Mutation: would catch if turn number wasn't in prompt
        [Fact]
        public void BuildPrompt_ContainsTurnNumber()
        {
            using var agent = MakeAgent();
            var context = MakeContext(turnNumber: 7);
            var turn = MakeTurnStart();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("7", prompt);
        }

        // ── Prompt: options display ────────────────────────────────────

        // Mutation: would catch if option intended text wasn't included
        [Fact]
        public void BuildPrompt_ContainsOptionIntendedText()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("Hey gorgeous", prompt);
            Assert.Contains("I fought a raccoon", prompt);
        }

        // Mutation: would catch if tell bonus icon wasn't shown
        [Fact]
        public void BuildPrompt_ContainsTellBonusIndicator()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            // Option C has hasTellBonus: true — should show tell indicator
            Assert.Contains("tell", prompt.ToLowerInvariant());
        }

        // Mutation: would catch if combo name wasn't shown
        [Fact]
        public void BuildPrompt_ContainsComboIndicator()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            // Option D has comboName: "WitChaosSA" — should show combo indicator
            Assert.Contains("combo", prompt.ToLowerInvariant());
        }

        // ── ParsePick behavior ─────────────────────────────────────────

        // Mutation: would catch if ParsePick didn't handle PICK: A correctly
        [Theory]
        [InlineData("PICK: A", 4, 0)]
        [InlineData("PICK: B", 4, 1)]
        [InlineData("PICK: C", 4, 2)]
        [InlineData("PICK: D", 4, 3)]
        public void ParsePick_ValidLetters_ReturnsCorrectIndex(string input, int optionCount, int expected)
        {
            int? result = LlmPlayerAgent.ParsePick(input, optionCount);
            Assert.Equal(expected, result);
        }

        // Mutation: would catch if ParsePick didn't use the LAST PICK line
        [Fact]
        public void ParsePick_MultiplePickLines_UsesLastOne()
        {
            string input = "PICK: A\nActually no.\nPICK: C";
            int? result = LlmPlayerAgent.ParsePick(input, 4);
            Assert.Equal(2, result);
        }

        // Mutation: would catch if ParsePick accepted out-of-range letters
        [Fact]
        public void ParsePick_LetterBeyondOptionCount_ReturnsNull()
        {
            // Only 2 options (A, B) — PICK: C should be invalid
            int? result = LlmPlayerAgent.ParsePick("PICK: C", 2);
            Assert.Null(result);
        }

        // Mutation: would catch if ParsePick accepted empty/null input
        [Fact]
        public void ParsePick_EmptyInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("", 4));
        }

        [Fact]
        public void ParsePick_NullInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick(null!, 4));
        }

        // Mutation: would catch if ParsePick accepted garbage text
        [Fact]
        public void ParsePick_NoPickKeyword_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("I choose option B", 4));
        }

        // Mutation: would catch if single option didn't accept PICK: A
        [Fact]
        public void ParsePick_SingleOption_AcceptsOnlyA()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK: A", 1));
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: B", 1));
        }

        // ── Constructor error conditions ───────────────────────────────

        // Mutation: would catch if null options wasn't validated
        [Fact]
        public void Constructor_NullOptions_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(null!, new ScoringPlayerAgent()));
        }

        // Mutation: would catch if null fallback wasn't validated
        [Fact]
        public void Constructor_NullFallback_ThrowsArgumentNull()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "key" };
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(opts, null!));
        }

        // ── DecideAsync error conditions ───────────────────────────────

        // Mutation: would catch if null turn wasn't validated
        [Fact]
        public async Task DecideAsync_NullTurn_ThrowsArgumentNull()
        {
            using var agent = MakeAgent();
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                agent.DecideAsync(null!, MakeContext()));
        }

        // Mutation: would catch if null context wasn't validated
        [Fact]
        public async Task DecideAsync_NullContext_ThrowsArgumentNull()
        {
            using var agent = MakeAgent();
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                agent.DecideAsync(MakeTurnStart(), null!));
        }

        // Mutation: would catch if zero options wasn't validated
        [Fact]
        public async Task DecideAsync_ZeroOptions_ThrowsInvalidOperation()
        {
            using var agent = MakeAgent();
            var state = new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1);
            var emptyTurn = new TurnStart(Array.Empty<DialogueOption>(), state);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                agent.DecideAsync(emptyTurn, MakeContext()));
        }

        // ── Edge case: single option ───────────────────────────────────

        // Mutation: would catch if single option fallback didn't return index 0
        [Fact]
        public async Task DecideAsync_SingleOption_ReturnsIndexZero()
        {
            using var agent = MakeAgent();
            var singleOptions = new[] { new DialogueOption(StatType.Charm, "Only option") };
            var turn = MakeTurnStart(singleOptions);
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.Equal(0, decision.OptionIndex);
        }

        // Mutation: would catch if single option didn't produce scores
        [Fact]
        public async Task DecideAsync_SingleOption_HasOneScore()
        {
            using var agent = MakeAgent();
            var singleOptions = new[] { new DialogueOption(StatType.Charm, "Only option") };
            var turn = MakeTurnStart(singleOptions);
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.Single(decision.Scores);
        }

        // ── Edge case: Disposable ──────────────────────────────────────

        // Mutation: would catch if LlmPlayerAgent didn't implement IDisposable
        [Fact]
        public void Agent_ImplementsIDisposable()
        {
            using var agent = MakeAgent();
            Assert.IsAssignableFrom<IDisposable>(agent);
        }

        // ── PlayerAgentContext: conversation history ────────────────────

        // Mutation: would catch if ConversationHistory wasn't nullable/defaulting to null
        [Fact]
        public void PlayerAgentContext_ConversationHistory_DefaultsToNull()
        {
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 1);

            Assert.Null(context.ConversationHistory);
        }

        // Mutation: would catch if ConversationHistory wasn't stored when provided
        [Fact]
        public void PlayerAgentContext_ConversationHistory_PreservesEntries()
        {
            var history = new List<(string, string)>
            {
                ("Alice", "hello"),
                ("Bob", "hi there"),
                ("Alice", "what's up?")
            };
            var context = new PlayerAgentContext(
                MakeStats(), MakeStats(), 10, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 3,
                conversationHistory: history);

            Assert.NotNull(context.ConversationHistory);
            Assert.Equal(3, context.ConversationHistory!.Count);
            Assert.Equal("Alice", context.ConversationHistory[0].Sender);
            Assert.Equal("hello", context.ConversationHistory[0].Text);
            Assert.Equal("Bob", context.ConversationHistory[1].Sender);
        }

        // ── Prompt: complete structure verification ─────────────────────

        // Mutation: would catch if options section didn't use A/B/C/D labels
        [Fact]
        public void BuildPrompt_OptionsUsedLetterLabels()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            // Should contain A) B) C) D) or similar letter labeling
            Assert.Contains("A)", prompt);
            Assert.Contains("D)", prompt);
        }

        // Mutation: would catch if stat types weren't shown in options
        [Fact]
        public void BuildPrompt_OptionsShowStatType()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            string prompt = agent.BuildPrompt(turn, context);

            Assert.Contains("CHARM", prompt);
            Assert.Contains("CHAOS", prompt);
        }
    }
}

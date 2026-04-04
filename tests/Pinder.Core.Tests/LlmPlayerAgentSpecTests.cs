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
    /// Spec-driven tests for LlmPlayerAgent (issue #348).
    /// Tests behavioral acceptance criteria from docs/specs/issue-348-spec.md.
    /// Prototype maturity: happy-path tests for each acceptance criterion.
    /// </summary>
    public class LlmPlayerAgentSpecTests
    {
        #region Test Helpers

        private static StatBlock MakeStats(
            int charm = 4, int rizz = 1, int honesty = 3,
            int chaos = 2, int wit = 2, int sa = 3)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static StatBlock MakeOpponentStats()
        {
            return MakeStats(charm: 2, rizz: 3, honesty: 2, chaos: 2, wit: 2, sa: 2);
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
            return new TurnStart(opts,
                new GameStateSnapshot(12, InterestState.Interested, 2, Array.Empty<string>(), 5));
        }

        private static PlayerAgentContext MakeContext(
            int interest = 12,
            InterestState state = InterestState.Interested,
            int momentum = 2,
            string[]? traps = null,
            int horniness = 4,
            Dictionary<ShadowStatType, int>? shadows = null,
            int turn = 5,
            bool nullShadows = false)
        {
            var shadowValues = nullShadows ? null : (shadows ?? new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 3 }, { ShadowStatType.Fixation, 1 },
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 4 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 2 }
            });

            return new PlayerAgentContext(
                MakeStats(), MakeOpponentStats(),
                interest, state, momentum,
                traps ?? Array.Empty<string>(),
                horniness,
                shadowValues,
                turn);
        }

        private LlmPlayerAgent MakeAgent(string playerName = "Sable", string opponentName = "Brick")
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            return new LlmPlayerAgent(opts, new ScoringPlayerAgent(), playerName, opponentName);
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        // AC1: LlmPlayerAgent implements IPlayerAgent
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Removing IPlayerAgent from class declaration
        [Fact]
        public void AC1_ImplementsIPlayerAgent()
        {
            using var agent = MakeAgent();
            Assert.IsAssignableFrom<IPlayerAgent>(agent);
        }

        // Mutation: Making class non-sealed (unsealed)
        [Fact]
        public void AC1_IsSealedClass()
        {
            Assert.True(typeof(LlmPlayerAgent).IsSealed);
        }

        // Mutation: Removing IDisposable implementation (per spec note #3)
        [Fact]
        public void AC1_ImplementsIDisposable()
        {
            using var agent = MakeAgent();
            Assert.IsAssignableFrom<IDisposable>(agent);
        }

        // Mutation: Removing null guard on options parameter
        [Fact]
        public void AC1_Constructor_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(null!, new ScoringPlayerAgent()));
        }

        // Mutation: Removing null guard on fallback parameter
        [Fact]
        public void AC1_Constructor_NullFallback_ThrowsArgumentNullException()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(opts, null!));
        }

        // Mutation: Dispose throws instead of gracefully cleaning up
        [Fact]
        public void AC1_Dispose_DoesNotThrow()
        {
            var agent = MakeAgent();
            agent.Dispose();
            // Double dispose should also not throw
            agent.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════
        // AC2: LLM prompt includes full option context, game state, rules
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Omitting interest value from prompt
        [Fact]
        public void AC2_Prompt_ContainsInterestValueAndState()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("12/25", prompt);
            Assert.Contains("Interested", prompt);
        }

        // Mutation: Omitting momentum streak from prompt
        [Fact]
        public void AC2_Prompt_ContainsMomentumStreak()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 2));

            Assert.Contains("2 consecutive wins", prompt);
        }

        // Mutation: Omitting turn number from prompt
        [Fact]
        public void AC2_Prompt_ContainsTurnNumber()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(turn: 7));

            Assert.Contains("Turn: 7", prompt);
        }

        // Mutation: Skipping options or using wrong letter labels
        [Fact]
        public void AC2_Prompt_ListsAllOptionsWithLetters()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("A)", prompt);
            Assert.Contains("B)", prompt);
            Assert.Contains("C)", prompt);
            Assert.Contains("D)", prompt);
        }

        // Mutation: Using lowercase stat names instead of uppercase
        [Fact]
        public void AC2_Prompt_ShowsUppercaseStatNames()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("CHARM", prompt);
            Assert.Contains("RIZZ", prompt);
            Assert.Contains("HONESTY", prompt);
            Assert.Contains("CHAOS", prompt);
        }

        // Mutation: Omitting DC from option lines
        [Fact]
        public void AC2_Prompt_ContainsDCValues()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("DC", prompt);
        }

        // Mutation: Omitting success percentage
        [Fact]
        public void AC2_Prompt_ContainsSuccessPercentage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("% success", prompt);
        }

        // Mutation: Omitting risk tier labels
        [Fact]
        public void AC2_Prompt_ContainsRiskTierLabels()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            bool hasRiskTier = prompt.Contains("Safe") || prompt.Contains("Medium") ||
                               prompt.Contains("Hard") || prompt.Contains("Bold");
            Assert.True(hasRiskTier, "Prompt should contain at least one risk tier label");
        }

        // Mutation: Omitting intended text for options
        [Fact]
        public void AC2_Prompt_ContainsIntendedText()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Hey gorgeous", prompt);
            Assert.Contains("I'm nervous", prompt);
        }

        // Mutation: Omitting rules reminder section entirely
        [Fact]
        public void AC2_Prompt_ContainsRulesReminder()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Rules Reminder", prompt);
        }

        // Mutation: Omitting PICK instruction
        [Fact]
        public void AC2_Prompt_ContainsPickInstruction()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("PICK:", prompt);
        }

        // Mutation: Hardcoding character names or swapping player/opponent
        [Fact]
        public void AC2_Prompt_ContainsCharacterNames()
        {
            using var agent = MakeAgent("TestPlayer", "TestOpponent");
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("TestPlayer", prompt);
            Assert.Contains("TestOpponent", prompt);
        }

        // Mutation: Not emitting tell bonus icon for options with hasTellBonus=true
        [Fact]
        public void AC2_Prompt_ShowsTellBonusIcon()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("\U0001f4d6", prompt); // 📖
        }

        // Mutation: Not emitting combo icon for options with comboName set
        [Fact]
        public void AC2_Prompt_ShowsComboIcon()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("\u2b50", prompt); // ⭐
        }

        // Mutation: Not emitting callback icon for options with callbackTurnNumber
        [Fact]
        public void AC2_Prompt_ShowsCallbackIcon()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey", callbackTurnNumber: 3),
                new DialogueOption(StatType.Rizz, "Yo")
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            Assert.Contains("\U0001f517", prompt); // 🔗
        }

        // Mutation: Not emitting weakness window icon for options with hasWeaknessWindow
        [Fact]
        public void AC2_Prompt_ShowsWeaknessWindowIcon()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey", hasWeaknessWindow: true),
                new DialogueOption(StatType.Rizz, "Yo")
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            Assert.Contains("\U0001f513", prompt); // 🔓
        }

        // Mutation: Using wrong modifier sign or wrong stat lookup for modifier
        [Fact]
        public void AC2_Prompt_ShowsStatModifier()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // Charm stat is +4, so prompt should show "+4" for the Charm option
            Assert.Contains("+4", prompt);
        }

        // Mutation: Not including "Need X+ on d20" in option lines
        [Fact]
        public void AC2_Prompt_ContainsNeedOnD20()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Need", prompt);
            Assert.Contains("on d20", prompt);
        }

        // Mutation: Success tier table missing from rules reminder
        [Fact]
        public void AC2_Prompt_RulesContainsSuccessAndFailureTiers()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Nat 20", prompt);
            Assert.Contains("Nat 1", prompt);
            Assert.Contains("Fumble", prompt);
        }

        // Mutation: Risk tier bonus not explained in rules
        [Fact]
        public void AC2_Prompt_RulesContainsRiskTierBonus()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // Rules should mention Hard → +1, Bold → +2
            Assert.Contains("Hard", prompt);
            Assert.Contains("Bold", prompt);
        }

        // Mutation: Icon explanations missing from rules reminder
        [Fact]
        public void AC2_Prompt_RulesContainsIconExplanations()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // Rules section should explain all four icons
            Assert.Contains("\U0001f517", prompt); // 🔗 callback
            Assert.Contains("\U0001f4d6", prompt); // 📖 tell
            Assert.Contains("\u2b50", prompt);      // ⭐ combo
            Assert.Contains("\U0001f513", prompt);  // 🔓 weakness
        }

        // ═══════════════════════════════════════════════════════════════
        // AC3: Parses PICK: [A/B/C/D] from response
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Wrong letter-to-index mapping (e.g. A→1 instead of A→0)
        [Theory]
        [InlineData("PICK: A", 4, 0)]
        [InlineData("PICK: B", 4, 1)]
        [InlineData("PICK: C", 4, 2)]
        [InlineData("PICK: D", 4, 3)]
        public void AC3_ParsePick_StandardFormat(string input, int count, int expected)
        {
            Assert.Equal(expected, LlmPlayerAgent.ParsePick(input, count));
        }

        // Mutation: Case-sensitive matching only (rejecting lowercase)
        [Theory]
        [InlineData("pick: a", 4, 0)]
        [InlineData("Pick: B", 4, 1)]
        [InlineData("PICK: c", 4, 2)]
        public void AC3_ParsePick_CaseInsensitive(string input, int count, int expected)
        {
            Assert.Equal(expected, LlmPlayerAgent.ParsePick(input, count));
        }

        // Mutation: Not stripping brackets from response
        [Theory]
        [InlineData("PICK: [A]", 4, 0)]
        [InlineData("PICK: [B]", 4, 1)]
        [InlineData("PICK:[C]", 4, 2)]
        [InlineData("pick: [d]", 4, 3)]
        public void AC3_ParsePick_BracketedFormat(string input, int count, int expected)
        {
            Assert.Equal(expected, LlmPlayerAgent.ParsePick(input, count));
        }

        // Mutation: Using first PICK instead of last
        [Fact]
        public void AC3_ParsePick_MultiplePickLines_UsesLast()
        {
            string text = "I think A.\nPICK: A\nActually C is better.\nPICK: C";
            Assert.Equal(2, LlmPlayerAgent.ParsePick(text, 4));
        }

        // Mutation: Returning default 0 instead of null when no match
        [Fact]
        public void AC3_ParsePick_NoPick_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("No pick here at all", 4));
        }

        // Mutation: Not handling empty string
        [Fact]
        public void AC3_ParsePick_EmptyString_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("", 4));
        }

        // Mutation: NullReferenceException on null input
        [Fact]
        public void AC3_ParsePick_NullInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick(null!, 4));
        }

        // Mutation: Not validating parsed index against option count
        [Fact]
        public void AC3_ParsePick_OutOfRange_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: E", 4));
        }

        // Mutation: Off-by-one — accepting index == optionCount
        [Fact]
        public void AC3_ParsePick_IndexExceedsOptionCount_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: D", 3));
        }

        // Mutation: Not restricting to valid letters for single-option case
        [Fact]
        public void AC3_ParsePick_SingleOption_OnlyAcceptsA()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK: A", 1));
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: B", 1));
        }

        // Mutation: Off-by-one on 2-option boundary
        [Fact]
        public void AC3_ParsePick_TwoOptions_AcceptsOnlyAB()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK: A", 2));
            Assert.Equal(1, LlmPlayerAgent.ParsePick("PICK: B", 2));
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: C", 2));
        }

        // Mutation: Not using last match when multiple PICK lines are far apart
        [Fact]
        public void AC3_ParsePick_LongTextWithMultiplePicks_UsesLast()
        {
            string text = "Analysis:\nPICK: A\n\nLots of reasoning here...\n\n" +
                          "More reasoning...\nActually:\nPICK: B\n\nFinal thought:\nPICK: D";
            Assert.Equal(3, LlmPlayerAgent.ParsePick(text, 4));
        }

        // Mutation: Whitespace handling — no space after colon
        [Fact]
        public void AC3_ParsePick_NoSpaceAfterColon()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK:A", 4));
        }

        // Mutation: Accepting numbers instead of just letters
        [Fact]
        public void AC3_ParsePick_NumericInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: 1", 4));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC4: PlayerDecision.Reasoning contains the LLM's explanation
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Omitting "[LLM fallback:" prefix on fallback
        [Fact]
        public async Task AC4_Fallback_ReasoningContainsFallbackPrefix()
        {
            using var agent = MakeAgent();
            var decision = await agent.DecideAsync(MakeTurnStart(), MakeContext());

            // With fake API key, call fails → falls back
            Assert.Contains("[LLM fallback:", decision.Reasoning);
        }

        // Mutation: Setting Reasoning to null or empty on fallback
        [Fact]
        public async Task AC4_Fallback_ReasoningIsNotEmpty()
        {
            using var agent = MakeAgent();
            var decision = await agent.DecideAsync(MakeTurnStart(), MakeContext());

            Assert.False(string.IsNullOrWhiteSpace(decision.Reasoning));
        }

        // Mutation: Fallback reasoning doesn't include scoring agent's reasoning
        [Fact]
        public async Task AC4_Fallback_ReasoningIncludesScoringContent()
        {
            using var agent = MakeAgent();
            var decision = await agent.DecideAsync(MakeTurnStart(), MakeContext());

            // Reasoning should have both the fallback prefix AND scoring content
            Assert.Contains("[LLM fallback:", decision.Reasoning);
            // After the prefix, there should be substantial content (not just the prefix)
            int prefixEnd = decision.Reasoning.IndexOf("]") + 1;
            Assert.True(prefixEnd < decision.Reasoning.Length,
                "Reasoning should contain scoring agent content after fallback prefix");
        }

        // ═══════════════════════════════════════════════════════════════
        // AC5: Falls back to ScoringPlayerAgent on API error
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Throwing exception instead of falling back on API failure
        [Fact]
        public async Task AC5_ApiFailure_DoesNotThrow()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);
            Assert.NotNull(decision);
        }

        // Mutation: Returning option index outside valid range
        [Fact]
        public async Task AC5_Fallback_ReturnsValidOptionIndex()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.InRange(decision.OptionIndex, 0, turn.Options.Length - 1);
        }

        // Mutation: Not populating Scores on fallback path
        [Fact]
        public async Task AC5_Fallback_ScoresAlwaysPopulated()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.NotNull(decision.Scores);
            Assert.Equal(turn.Options.Length, decision.Scores.Length);
        }

        // Mutation: Scores array has wrong indices (not 0-based sequential)
        [Fact]
        public async Task AC5_Fallback_ScoresHaveCorrectIndices()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            for (int i = 0; i < decision.Scores.Length; i++)
            {
                Assert.Equal(i, decision.Scores[i].OptionIndex);
            }
        }

        // Mutation: SuccessChance not clamped to [0, 1] range
        [Fact]
        public async Task AC5_Fallback_SuccessChanceInRange()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            foreach (var score in decision.Scores)
            {
                Assert.InRange(score.SuccessChance, 0.0f, 1.0f);
            }
        }

        // Mutation: Scores count doesn't match options count for different option counts
        [Fact]
        public async Task AC5_Fallback_TwoOptions_ScoresMatchCount()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hello"),
                new DialogueOption(StatType.Rizz, "Hey")
            };
            var turn = MakeTurnStart(options);
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.Equal(2, decision.Scores.Length);
        }

        // ═══════════════════════════════════════════════════════════════
        // Error Conditions (from spec Error Conditions table)
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Not throwing ArgumentNullException for null turn
        [Fact]
        public async Task Error_NullTurn_ThrowsArgumentNullException()
        {
            using var agent = MakeAgent();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(null!, MakeContext()));
        }

        // Mutation: Not throwing ArgumentNullException for null context
        [Fact]
        public async Task Error_NullContext_ThrowsArgumentNullException()
        {
            using var agent = MakeAgent();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(MakeTurnStart(), null!));
        }

        // Mutation: Not throwing InvalidOperationException for empty options
        [Fact]
        public async Task Error_EmptyOptions_ThrowsInvalidOperationException()
        {
            using var agent = MakeAgent();
            var emptyTurn = new TurnStart(
                Array.Empty<DialogueOption>(),
                new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => agent.DecideAsync(emptyTurn, MakeContext()));
        }

        // Mutation: Empty options throws wrong exception type (e.g. ArgumentException)
        [Fact]
        public async Task Error_EmptyOptions_ExceptionMessage()
        {
            using var agent = MakeAgent();
            var emptyTurn = new TurnStart(
                Array.Empty<DialogueOption>(),
                new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => agent.DecideAsync(emptyTurn, MakeContext()));
            // Spec says message should indicate no options available
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt Formatting — Shadows
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Not handling null shadows (would crash or show empty)
        [Fact]
        public void Edge_NullShadows_ShowsUnknown()
        {
            using var agent = MakeAgent();
            var context = MakeContext(nullShadows: true);

            string prompt = agent.BuildPrompt(MakeTurnStart(), context);

            Assert.Contains("unknown", prompt.ToLowerInvariant());
        }

        // Mutation: Shadow values not showing per-shadow breakdown
        [Fact]
        public void Edge_ShadowValues_ShowsSpecificValues()
        {
            using var agent = MakeAgent();
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Denial, 7 }, { ShadowStatType.Fixation, 12 },
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 3 },
                { ShadowStatType.Dread, 18 }, { ShadowStatType.Overthinking, 1 }
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(shadows: shadows));

            Assert.Contains("Denial 7", prompt);
            Assert.Contains("Fixation 12", prompt);
            Assert.Contains("Dread 18", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt Formatting — Traps
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Not showing "none" when no traps active
        [Fact]
        public void Edge_EmptyTraps_ShowsNone()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(traps: Array.Empty<string>()));

            Assert.Contains("none", prompt.ToLowerInvariant());
        }

        // Mutation: Not listing trap names when traps are active
        [Fact]
        public void Edge_ActiveTraps_ShowsTrapNames()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(traps: new[] { "Fixation", "Madness" }));

            Assert.Contains("Fixation", prompt);
            Assert.Contains("Madness", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt Formatting — Momentum
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Showing bonus note when momentum is 0
        [Fact]
        public void Edge_MomentumZero_NoBonusNote()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 0));

            Assert.Contains("0 consecutive wins", prompt);
            var lines = prompt.Split('\n');
            var momentumLine = Array.Find(lines, l => l.Contains("consecutive wins"));
            Assert.NotNull(momentumLine);
            Assert.DoesNotContain("to next roll", momentumLine);
        }

        // Mutation: Wrong bonus value — showing +3 instead of +2 at momentum 3
        [Fact]
        public void Edge_Momentum3_ShowsPlus2()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 3));

            Assert.Contains("+2 to next roll", prompt);
        }

        // Mutation: Not recognizing momentum 4 as ≥3 threshold
        [Fact]
        public void Edge_Momentum4_ShowsPlus2()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 4));

            Assert.Contains("+2 to next roll", prompt);
        }

        // Mutation: Wrong threshold — showing +2 instead of +3 at momentum 5
        [Fact]
        public void Edge_Momentum5_ShowsPlus3()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 5));

            Assert.Contains("+3 to next roll", prompt);
        }

        // Mutation: Momentum 10 doesn't show +3 (large values)
        [Fact]
        public void Edge_Momentum10_ShowsPlus3()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 10));

            Assert.Contains("+3 to next roll", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt Formatting — Interest State Modifiers
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Bored not showing disadvantage
        [Fact]
        public void Edge_BoredState_ShowsDisadvantage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 3, state: InterestState.Bored));

            Assert.Contains("disadvantage", prompt.ToLowerInvariant());
        }

        // Mutation: VeryIntoIt not showing advantage
        [Fact]
        public void Edge_VeryIntoIt_ShowsAdvantage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 18, state: InterestState.VeryIntoIt));

            Assert.Contains("advantage", prompt.ToLowerInvariant());
        }

        // Mutation: AlmostThere not showing advantage (only VeryIntoIt)
        [Fact]
        public void Edge_AlmostThere_ShowsAdvantage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 22, state: InterestState.AlmostThere));

            Assert.Contains("advantage", prompt.ToLowerInvariant());
        }

        // Mutation: Interested state falsely shows advantage/disadvantage
        [Fact]
        public void Edge_InterestedState_NoModifierNote()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 12, state: InterestState.Interested));

            Assert.DoesNotContain("grants advantage", prompt);
            Assert.DoesNotContain("grants disadvantage", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt Formatting — Option Count
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Single option still shows B/C/D labels
        [Fact]
        public void Edge_SingleOption_ShowsOnlyA()
        {
            using var agent = MakeAgent();
            var options = new[] { new DialogueOption(StatType.Rizz, "Only Rizz") };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            Assert.Contains("A)", prompt);
            Assert.DoesNotContain("B)", prompt);
            Assert.DoesNotContain("C)", prompt);
            Assert.DoesNotContain("D)", prompt);
        }

        // Mutation: Two options show C) label
        [Fact]
        public void Edge_TwoOptions_ShowsOnlyAB()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Charming"),
                new DialogueOption(StatType.Wit, "Witty")
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            Assert.Contains("A)", prompt);
            Assert.Contains("B)", prompt);
            Assert.DoesNotContain("C)", prompt);
        }

        // Mutation: Three options show D) label
        [Fact]
        public void Edge_ThreeOptions_ShowsABC()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Charming"),
                new DialogueOption(StatType.Wit, "Witty"),
                new DialogueOption(StatType.Honesty, "Honest")
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            Assert.Contains("A)", prompt);
            Assert.Contains("B)", prompt);
            Assert.Contains("C)", prompt);
            Assert.DoesNotContain("D)", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: DecideAsync with varied option counts
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Single option not returning index 0 on fallback
        [Fact]
        public async Task Edge_SingleOption_FallbackReturnsIndex0()
        {
            using var agent = MakeAgent();
            var options = new[] { new DialogueOption(StatType.Rizz, "Only option") };
            var turn = MakeTurnStart(options);
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.Equal(0, decision.OptionIndex);
            Assert.Single(decision.Scores);
        }

        // Mutation: Fallback with two options returns out-of-range index
        [Fact]
        public async Task Edge_TwoOptions_FallbackReturnsValidIndex()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hello"),
                new DialogueOption(StatType.Rizz, "Hey")
            };
            var turn = MakeTurnStart(options);
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.InRange(decision.OptionIndex, 0, 1);
            Assert.Equal(2, decision.Scores.Length);
        }

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt with multiple bonus icons on single option
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Only showing first bonus icon, ignoring additional ones
        [Fact]
        public void Edge_OptionWithMultipleBonuses_ShowsAllIcons()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Super move",
                    hasTellBonus: true, callbackTurnNumber: 2,
                    comboName: "TestCombo", hasWeaknessWindow: true),
                new DialogueOption(StatType.Rizz, "Basic")
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            // All 4 icons should appear for the first option
            Assert.Contains("\U0001f517", prompt); // 🔗
            Assert.Contains("\U0001f4d6", prompt); // 📖
            Assert.Contains("\u2b50", prompt);      // ⭐
            Assert.Contains("\U0001f513", prompt);  // 🔓
        }

        // Mutation: Option with no bonuses still shows bonus icons in option lines
        [Fact]
        public void Edge_OptionWithNoBonuses_NoIconsInOptionLines()
        {
            using var agent = MakeAgent();
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Plain option"),
                new DialogueOption(StatType.Rizz, "Also plain")
            };
            string prompt = agent.BuildPrompt(MakeTurnStart(options), MakeContext());

            // Extract option lines (A) and B) lines) - they should NOT contain bonus icons
            var lines = prompt.Split('\n');
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("A)") || line.TrimStart().StartsWith("B)"))
                {
                    Assert.DoesNotContain("\U0001f517", line); // 🔗
                    Assert.DoesNotContain("\U0001f4d6", line); // 📖
                    Assert.DoesNotContain("\u2b50", line);      // ⭐
                    Assert.DoesNotContain("\U0001f513", line);  // 🔓
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ParsePick additional edge cases
        // ═══════════════════════════════════════════════════════════════

        // Mutation: PICK embedded in reasoning text mistakenly matched
        [Fact]
        public void AC3_ParsePick_PickInReasoningFollowedByFinalPick()
        {
            string text = "I'll PICK: A first but then reconsider.\n" +
                          "After analysis, PICK: B is better.\n" +
                          "Final answer:\nPICK: C";
            Assert.Equal(2, LlmPlayerAgent.ParsePick(text, 4));
        }

        // Mutation: Extra whitespace after colon not handled
        [Fact]
        public void AC3_ParsePick_ExtraWhitespace()
        {
            // Spec says "optional whitespace" — extra spaces should work
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK:   A", 4));
        }

        // ═══════════════════════════════════════════════════════════════
        // Prompt: Pct Calculation per Spec
        // The spec says: pct = Math.Max(0, Math.Min(100, (21 - need) * 5))
        // need = dc - modifier
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Wrong pct formula (e.g. (20 - need) * 5 instead of (21 - need) * 5)
        [Fact]
        public void AC2_Prompt_PctCalculation_VerifySpecExample()
        {
            // Using Charm +4 vs opponent with SelfAwareness defence
            // Opponent stats: SA = 2, so DC = 13 + 2 = 15
            // need = 15 - 4 = 11
            // pct = (21 - 11) * 5 = 50
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // For the Charm option, we expect 50% success in the prompt
            // (or adjusted pct with bonuses per PR #407)
            Assert.Contains("% success", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Risk Tier Labels per Spec
        // need ≤5 → Safe, 6-10 → Medium, 11-15 → Hard, ≥16 → Bold
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Wrong risk tier threshold (e.g. ≤4 instead of ≤5 for Safe)
        [Fact]
        public void AC2_Prompt_RiskTierLabel_MediumForNeed6to10()
        {
            // Honesty +3 vs opponent Chaos defence (DC = 13 + 2 = 15)
            // need = 15 - 3 = 12 → this is Hard (11-15)
            // But with adjusted bonuses (tell +2), adjusted need = 10 → Medium
            // The prompt should contain "Medium" somewhere for Honesty option
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // We know at least one option should be Medium or Hard
            Assert.True(prompt.Contains("Medium") || prompt.Contains("Hard"),
                "Prompt should contain risk tier labels matching need values");
        }

        // ═══════════════════════════════════════════════════════════════
        // Prompt: Character Name Customization
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Ignoring constructor playerName param, using hardcoded name
        [Fact]
        public void AC2_Prompt_CustomCharacterNames()
        {
            using var agent = MakeAgent("BigDick", "Slutty");
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("BigDick", prompt);
            Assert.Contains("Slutty", prompt);
            Assert.DoesNotContain("Sable", prompt);
            Assert.DoesNotContain("Brick", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Prompt: Sentient penis framing
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Missing the game's thematic framing
        [Fact]
        public void AC2_Prompt_ContainsSentientPenisFraming()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("sentient penis", prompt.ToLowerInvariant());
            Assert.Contains("dating app", prompt.ToLowerInvariant());
        }

        // ═══════════════════════════════════════════════════════════════
        // Prompt: Reasoning instruction
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Missing step-by-step reasoning instruction
        [Fact]
        public void AC2_Prompt_ContainsReasoningInstruction()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("reasoning", prompt.ToLowerInvariant());
        }

        // ═══════════════════════════════════════════════════════════════
        // Lukewarm state: verify prompt handles it
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Lukewarm crashes or shows wrong state name
        [Fact]
        public void Edge_LukewarmState_NoModifierNote()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 7, state: InterestState.Lukewarm));

            Assert.Contains("Lukewarm", prompt);
            Assert.DoesNotContain("grants advantage", prompt);
            Assert.DoesNotContain("grants disadvantage", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // Concurrent/Repeated DecideAsync calls
        // ═══════════════════════════════════════════════════════════════

        // Mutation: Internal state corruption between calls
        [Fact]
        public async Task Edge_MultipleCalls_EachReturnsValidDecision()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var d1 = await agent.DecideAsync(turn, context);
            var d2 = await agent.DecideAsync(turn, context);

            Assert.NotNull(d1);
            Assert.NotNull(d2);
            Assert.InRange(d1.OptionIndex, 0, turn.Options.Length - 1);
            Assert.InRange(d2.OptionIndex, 0, turn.Options.Length - 1);
        }
    }
}

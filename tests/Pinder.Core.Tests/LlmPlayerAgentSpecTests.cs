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
            int turn = 5)
        {
            return new PlayerAgentContext(
                MakeStats(), MakeOpponentStats(),
                interest, state, momentum,
                traps ?? Array.Empty<string>(),
                horniness,
                shadows ?? new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Denial, 3 }, { ShadowStatType.Fixation, 1 },
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 4 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 2 }
                },
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

        // Fails if: LlmPlayerAgent does not implement IPlayerAgent interface
        [Fact]
        public void AC1_ImplementsIPlayerAgent()
        {
            using var agent = MakeAgent();
            Assert.IsAssignableFrom<IPlayerAgent>(agent);
        }

        // Fails if: LlmPlayerAgent is not sealed
        [Fact]
        public void AC1_IsSealedClass()
        {
            Assert.True(typeof(LlmPlayerAgent).IsSealed);
        }

        // Fails if: LlmPlayerAgent does not implement IDisposable (per spec note #3)
        [Fact]
        public void AC1_ImplementsIDisposable()
        {
            using var agent = MakeAgent();
            Assert.IsAssignableFrom<IDisposable>(agent);
        }

        // Fails if: Constructor accepts null options without throwing
        [Fact]
        public void AC1_Constructor_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(null!, new ScoringPlayerAgent()));
        }

        // Fails if: Constructor accepts null fallback without throwing
        [Fact]
        public void AC1_Constructor_NullFallback_ThrowsArgumentNullException()
        {
            var opts = new Pinder.LlmAdapters.Anthropic.AnthropicOptions { ApiKey = "test-key" };
            Assert.Throws<ArgumentNullException>(() =>
                new LlmPlayerAgent(opts, null!));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC2: LLM prompt includes full option context, game state, rules
        // ═══════════════════════════════════════════════════════════════

        // Fails if: Prompt omits current interest value and state name
        [Fact]
        public void AC2_Prompt_ContainsInterestValueAndState()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("12/25", prompt);
            Assert.Contains("Interested", prompt);
        }

        // Fails if: Prompt omits momentum streak count
        [Fact]
        public void AC2_Prompt_ContainsMomentumStreak()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 2));

            Assert.Contains("2 consecutive wins", prompt);
        }

        // Fails if: Prompt omits turn number
        [Fact]
        public void AC2_Prompt_ContainsTurnNumber()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(turn: 7));

            Assert.Contains("Turn: 7", prompt);
        }

        // Fails if: Prompt doesn't list all 4 options with letter labels A-D
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

        // Fails if: Prompt omits stat names in uppercase
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

        // Fails if: Prompt omits DC values for each option
        [Fact]
        public void AC2_Prompt_ContainsDCValues()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("DC", prompt);
        }

        // Fails if: Prompt omits success percentage
        [Fact]
        public void AC2_Prompt_ContainsSuccessPercentage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // Should contain "% success" for at least one option
            Assert.Contains("% success", prompt);
        }

        // Fails if: Prompt omits risk tier labels
        [Fact]
        public void AC2_Prompt_ContainsRiskTierLabels()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // At least one risk tier should appear
            bool hasRiskTier = prompt.Contains("Safe") || prompt.Contains("Medium") ||
                               prompt.Contains("Hard") || prompt.Contains("Bold");
            Assert.True(hasRiskTier, "Prompt should contain at least one risk tier label");
        }

        // Fails if: Prompt omits intended text for options
        [Fact]
        public void AC2_Prompt_ContainsIntendedText()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Hey gorgeous", prompt);
            Assert.Contains("I'm nervous", prompt);
        }

        // Fails if: Prompt omits rules reminder section
        [Fact]
        public void AC2_Prompt_ContainsRulesReminder()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Rules Reminder", prompt);
        }

        // Fails if: Prompt omits PICK instruction
        [Fact]
        public void AC2_Prompt_ContainsPickInstruction()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("PICK:", prompt);
        }

        // Fails if: Prompt omits character names
        [Fact]
        public void AC2_Prompt_ContainsCharacterNames()
        {
            using var agent = MakeAgent("TestPlayer", "TestOpponent");
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("TestPlayer", prompt);
            Assert.Contains("TestOpponent", prompt);
        }

        // Fails if: Tell bonus icon is missing from option C
        [Fact]
        public void AC2_Prompt_ShowsTellBonusIcon()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("📖", prompt);
        }

        // Fails if: Combo icon is missing from option D
        [Fact]
        public void AC2_Prompt_ShowsComboIcon()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("⭐", prompt);
        }

        // Fails if: Callback icon not shown for option with callbackTurnNumber
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

            Assert.Contains("🔗", prompt);
        }

        // Fails if: Weakness window icon not shown for option with hasWeaknessWindow
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

            Assert.Contains("🔓", prompt);
        }

        // ═══════════════════════════════════════════════════════════════
        // AC3: Parses PICK: [A/B/C/D] from response
        // ═══════════════════════════════════════════════════════════════

        // Fails if: ParsePick doesn't accept standard format "PICK: A"
        [Theory]
        [InlineData("PICK: A", 4, 0)]
        [InlineData("PICK: B", 4, 1)]
        [InlineData("PICK: C", 4, 2)]
        [InlineData("PICK: D", 4, 3)]
        public void AC3_ParsePick_StandardFormat(string input, int count, int expected)
        {
            Assert.Equal(expected, LlmPlayerAgent.ParsePick(input, count));
        }

        // Fails if: ParsePick rejects case-insensitive input
        [Theory]
        [InlineData("pick: a", 4, 0)]
        [InlineData("Pick: B", 4, 1)]
        [InlineData("PICK: c", 4, 2)]
        public void AC3_ParsePick_CaseInsensitive(string input, int count, int expected)
        {
            Assert.Equal(expected, LlmPlayerAgent.ParsePick(input, count));
        }

        // Fails if: ParsePick rejects bracketed format
        [Theory]
        [InlineData("PICK: [A]", 4, 0)]
        [InlineData("PICK: [B]", 4, 1)]
        [InlineData("PICK:[C]", 4, 2)]
        [InlineData("pick: [d]", 4, 3)]
        public void AC3_ParsePick_BracketedFormat(string input, int count, int expected)
        {
            Assert.Equal(expected, LlmPlayerAgent.ParsePick(input, count));
        }

        // Fails if: ParsePick uses first PICK instead of last when multiple exist
        [Fact]
        public void AC3_ParsePick_MultiplePickLines_UsesLast()
        {
            string text = "I think A.\nPICK: A\nActually C is better.\nPICK: C";
            Assert.Equal(2, LlmPlayerAgent.ParsePick(text, 4));
        }

        // Fails if: ParsePick doesn't return null for missing PICK line
        [Fact]
        public void AC3_ParsePick_NoPick_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("No pick here at all", 4));
        }

        // Fails if: ParsePick doesn't return null for empty string
        [Fact]
        public void AC3_ParsePick_EmptyString_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("", 4));
        }

        // Fails if: ParsePick doesn't return null for null input
        [Fact]
        public void AC3_ParsePick_NullInput_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick(null!, 4));
        }

        // Fails if: ParsePick accepts out-of-range letter E for 4 options
        [Fact]
        public void AC3_ParsePick_OutOfRange_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: E", 4));
        }

        // Fails if: ParsePick accepts D (index 3) when only 3 options exist
        [Fact]
        public void AC3_ParsePick_IndexExceedsOptionCount_ReturnsNull()
        {
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: D", 3));
        }

        // Fails if: ParsePick accepts B when only 1 option exists (single option edge case)
        [Fact]
        public void AC3_ParsePick_SingleOption_OnlyAcceptsA()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK: A", 1));
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: B", 1));
        }

        // Fails if: ParsePick accepts C when only 2 options exist
        [Fact]
        public void AC3_ParsePick_TwoOptions_AcceptsOnlyAB()
        {
            Assert.Equal(0, LlmPlayerAgent.ParsePick("PICK: A", 2));
            Assert.Equal(1, LlmPlayerAgent.ParsePick("PICK: B", 2));
            Assert.Null(LlmPlayerAgent.ParsePick("PICK: C", 2));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC4: PlayerDecision.Reasoning contains the LLM's explanation
        // ═══════════════════════════════════════════════════════════════

        // Fails if: Fallback reasoning doesn't contain the "[LLM fallback:" prefix
        [Fact]
        public async Task AC4_Fallback_ReasoningContainsFallbackPrefix()
        {
            using var agent = MakeAgent();
            var decision = await agent.DecideAsync(MakeTurnStart(), MakeContext());

            // With test-key, API call fails → falls back
            Assert.Contains("[LLM fallback:", decision.Reasoning);
        }

        // Fails if: Fallback reasoning is empty or null
        [Fact]
        public async Task AC4_Fallback_ReasoningIsNotEmpty()
        {
            using var agent = MakeAgent();
            var decision = await agent.DecideAsync(MakeTurnStart(), MakeContext());

            Assert.False(string.IsNullOrWhiteSpace(decision.Reasoning));
        }

        // ═══════════════════════════════════════════════════════════════
        // AC5: Falls back to ScoringPlayerAgent on API error
        // ═══════════════════════════════════════════════════════════════

        // Fails if: API failure causes an exception instead of fallback
        [Fact]
        public async Task AC5_ApiFailure_DoesNotThrow()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            // With fake API key, HTTP call will fail - should fallback, not throw
            var decision = await agent.DecideAsync(turn, context);
            Assert.NotNull(decision);
        }

        // Fails if: Fallback returns option index outside valid range
        [Fact]
        public async Task AC5_Fallback_ReturnsValidOptionIndex()
        {
            using var agent = MakeAgent();
            var turn = MakeTurnStart();
            var context = MakeContext();

            var decision = await agent.DecideAsync(turn, context);

            Assert.InRange(decision.OptionIndex, 0, turn.Options.Length - 1);
        }

        // Fails if: Fallback doesn't populate Scores array
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

        // Fails if: Scores don't have correct option indices (0-based sequential)
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

        // Fails if: SuccessChance not clamped to [0, 1]
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

        // ═══════════════════════════════════════════════════════════════
        // Error Conditions
        // ═══════════════════════════════════════════════════════════════

        // Fails if: DecideAsync doesn't throw ArgumentNullException for null turn
        [Fact]
        public async Task Error_NullTurn_ThrowsArgumentNullException()
        {
            using var agent = MakeAgent();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(null!, MakeContext()));
        }

        // Fails if: DecideAsync doesn't throw ArgumentNullException for null context
        [Fact]
        public async Task Error_NullContext_ThrowsArgumentNullException()
        {
            using var agent = MakeAgent();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => agent.DecideAsync(MakeTurnStart(), null!));
        }

        // Fails if: DecideAsync doesn't throw InvalidOperationException for empty options
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

        // ═══════════════════════════════════════════════════════════════
        // Edge Cases: Prompt Formatting
        // ═══════════════════════════════════════════════════════════════

        // Fails if: Null shadows don't produce "unknown" in prompt
        [Fact]
        public void Edge_NullShadows_ShowsUnknown()
        {
            using var agent = MakeAgent();
            var context = MakeContext(shadows: null);
            // Remove the shadows by creating context with null explicitly
            var nullShadowCtx = new PlayerAgentContext(
                MakeStats(), MakeOpponentStats(), 12, InterestState.Interested, 0,
                Array.Empty<string>(), 0, null, 5);

            string prompt = agent.BuildPrompt(MakeTurnStart(), nullShadowCtx);

            Assert.Contains("unknown", prompt.ToLowerInvariant());
        }

        // Fails if: Empty traps don't produce "none" in prompt
        [Fact]
        public void Edge_EmptyTraps_ShowsNone()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(traps: Array.Empty<string>()));

            Assert.Contains("none", prompt.ToLowerInvariant());
        }

        // Fails if: Active traps aren't listed by name
        [Fact]
        public void Edge_ActiveTraps_ShowsTrapNames()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(traps: new[] { "Fixation", "Madness" }));

            Assert.Contains("Fixation", prompt);
            Assert.Contains("Madness", prompt);
        }

        // Fails if: Momentum 0 shows a momentum bonus note in the state section
        [Fact]
        public void Edge_MomentumZero_NoBonusNoteInStateSection()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 0));

            Assert.Contains("0 consecutive wins", prompt);
            // The momentum line itself should not contain a bonus note
            // Extract the momentum line specifically
            var lines = prompt.Split('\n');
            var momentumLine = Array.Find(lines, l => l.Contains("Momentum:") || l.Contains("consecutive wins"));
            Assert.NotNull(momentumLine);
            Assert.DoesNotContain("to next roll", momentumLine);
        }

        // Fails if: Momentum 3 doesn't show +2 bonus note
        [Fact]
        public void Edge_Momentum3_ShowsPlus2()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 3));

            Assert.Contains("+2 to next roll", prompt);
        }

        // Fails if: Momentum 5 doesn't show +3 bonus note
        [Fact]
        public void Edge_Momentum5_ShowsPlus3()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 5));

            Assert.Contains("+3 to next roll", prompt);
        }

        // Fails if: Momentum 4 doesn't show +2 (boundary: ≥3 and <5)
        [Fact]
        public void Edge_Momentum4_ShowsPlus2()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext(momentum: 4));

            Assert.Contains("+2 to next roll", prompt);
        }

        // Fails if: Bored state doesn't show "disadvantage" modifier note
        [Fact]
        public void Edge_BoredState_ShowsDisadvantage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 3, state: InterestState.Bored));

            Assert.Contains("disadvantage", prompt.ToLowerInvariant());
        }

        // Fails if: VeryIntoIt state doesn't show "advantage" modifier note
        [Fact]
        public void Edge_VeryIntoIt_ShowsAdvantage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 18, state: InterestState.VeryIntoIt));

            Assert.Contains("advantage", prompt.ToLowerInvariant());
        }

        // Fails if: AlmostThere state doesn't show "advantage" modifier note
        [Fact]
        public void Edge_AlmostThere_ShowsAdvantage()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 22, state: InterestState.AlmostThere));

            Assert.Contains("advantage", prompt.ToLowerInvariant());
        }

        // Fails if: Interested state shows a modifier note when it shouldn't
        [Fact]
        public void Edge_InterestedState_NoModifierNote()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(),
                MakeContext(interest: 12, state: InterestState.Interested));

            // Should not contain advantage/disadvantage for Interested state
            // Check that neither appears near the interest line
            Assert.DoesNotContain("grants advantage", prompt);
            Assert.DoesNotContain("grants disadvantage", prompt);
        }

        // Fails if: Single option prompt shows B) label
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

        // Fails if: Two-option prompt shows C) label
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

        // Fails if: Shadow values with specific numbers aren't shown
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
        // Edge Cases: DecideAsync with single option
        // ═══════════════════════════════════════════════════════════════

        // Fails if: Single option doesn't fall back gracefully and return index 0
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

        // ═══════════════════════════════════════════════════════════════
        // Prompt: Need and Pct calculations
        // ═══════════════════════════════════════════════════════════════

        // Fails if: Prompt shows "Need" field for each option
        [Fact]
        public void Prompt_ContainsNeedField()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Need", prompt);
            Assert.Contains("on d20", prompt);
        }

        // Fails if: Rules reminder missing success tier explanations
        [Fact]
        public void Prompt_RulesReminder_ContainsSuccessTiers()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Nat 20", prompt);
            Assert.Contains("Nat 1", prompt);
        }

        // Fails if: Rules reminder missing risk tier bonus explanation
        [Fact]
        public void Prompt_RulesReminder_ContainsRiskTierBonus()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("Hard", prompt);
            Assert.Contains("Bold", prompt);
        }

        // Fails if: Rules reminder missing momentum explanation
        [Fact]
        public void Prompt_RulesReminder_ContainsMomentumExplanation()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            // Rules should explain momentum bonus
            Assert.Contains("Momentum", prompt);
        }

        // Fails if: Rules reminder missing icon explanations
        [Fact]
        public void Prompt_RulesReminder_ContainsIconExplanations()
        {
            using var agent = MakeAgent();
            string prompt = agent.BuildPrompt(MakeTurnStart(), MakeContext());

            Assert.Contains("🔗", prompt);  // callback explanation
            Assert.Contains("📖", prompt);  // tell explanation
            Assert.Contains("⭐", prompt);  // combo explanation
            Assert.Contains("🔓", prompt);  // weakness explanation
        }
    }
}

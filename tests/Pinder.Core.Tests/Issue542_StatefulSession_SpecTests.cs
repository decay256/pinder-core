using System;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #542: GameSession creates LLM conversation session at start.
    /// Tests verify behavior from docs/specs/issue-542-spec.md acceptance criteria and edge cases.
    /// </summary>
    public class Issue542_StatefulSession_SpecTests
    {
        #region Test Infrastructure

        private static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            return TestHelpers.MakeStatBlock(allStats, allShadow);
        }

        private static CharacterProfile MakeProfile(string name, string? promptOverride = null, int allStats = 2)
        {
            return new CharacterProfile(
                stats: MakeStatBlock(allStats),
                assembledSystemPrompt: promptOverride ?? $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Mock stateful adapter that records StartConversation calls and supports
        /// full turn resolution (4 options, deliver, opponent response).
        /// </summary>
        private sealed class SpyStatefulAdapter : IStatefulLlmAdapter
        {
            public string? LastSystemPrompt { get; private set; }
            public int StartConversationCallCount { get; private set; }
            public bool HasActiveConversation => LastSystemPrompt != null;

            // Track method call counts to verify turn integration
            public int GetDialogueOptionsCallCount { get; private set; }
            public int DeliverMessageCallCount { get; private set; }
            public int GetOpponentResponseCallCount { get; private set; }
            public int GetInterestChangeBeatCallCount { get; private set; }

            public void StartConversation(string systemPrompt)
            {
                LastSystemPrompt = systemPrompt;
                StartConversationCallCount++;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                GetDialogueOptionsCallCount++;
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Honesty, "Real talk"),
                    new DialogueOption(StatType.Wit, "Clever line"),
                    new DialogueOption(StatType.Chaos, "Wild card")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                DeliverMessageCallCount++;
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                GetOpponentResponseCallCount++;
                return Task.FromResult(new OpponentResponse("Interesting..."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                GetInterestChangeBeatCallCount++;
                return Task.FromResult<string?>(null);
            }
        }

        #endregion

        #region AC1: IStatefulLlmAdapter interface contract

        // What: AC1 — IStatefulLlmAdapter extends ILlmAdapter
        // Mutation: Fails if IStatefulLlmAdapter does not inherit from ILlmAdapter
        [Fact]
        public void AC1_IStatefulLlmAdapter_Extends_ILlmAdapter()
        {
            Assert.True(typeof(ILlmAdapter).IsAssignableFrom(typeof(IStatefulLlmAdapter)));
        }

        // What: AC1 — IStatefulLlmAdapter defines StartConversation(string)
        // Mutation: Fails if StartConversation method is missing or has wrong signature
        [Fact]
        public void AC1_IStatefulLlmAdapter_Has_StartConversation_Method()
        {
            var method = typeof(IStatefulLlmAdapter).GetMethod("StartConversation");
            Assert.NotNull(method);
            Assert.Equal(typeof(void), method!.ReturnType);
            var parameters = method.GetParameters();
            Assert.Single(parameters);
            Assert.Equal(typeof(string), parameters[0].ParameterType);
        }

        // What: AC1 — IStatefulLlmAdapter defines HasActiveConversation property
        // Mutation: Fails if HasActiveConversation property is missing or wrong type
        [Fact]
        public void AC1_IStatefulLlmAdapter_Has_HasActiveConversation_Property()
        {
            var prop = typeof(IStatefulLlmAdapter).GetProperty("HasActiveConversation");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop!.PropertyType);
            Assert.True(prop.CanRead);
        }

        // What: AC1 — IStatefulLlmAdapter lives in Pinder.Core.Interfaces namespace
        // Mutation: Fails if interface is in wrong namespace
        [Fact]
        public void AC1_IStatefulLlmAdapter_Namespace_Is_PinderCore_Interfaces()
        {
            Assert.Equal("Pinder.Core.Interfaces", typeof(IStatefulLlmAdapter).Namespace);
        }

        #endregion

        #region AC3: GameSession detects stateful adapter and calls StartConversation

        // What: AC3 — GameSession detects IStatefulLlmAdapter via pattern match
        // Mutation: Fails if GameSession never calls StartConversation
        [Fact]
        public void AC3_Constructor_WithStatefulAdapter_CallsStartConversation_ExactlyOnce()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCallCount);
            Assert.True(adapter.HasActiveConversation);
        }

        // What: AC3 — GameSession checks _llm not config for stateful detection
        // Mutation: Fails if stateful detection depends on GameSessionConfig instead of adapter type
        [Fact]
        public void AC3_StatefulDetection_Independent_Of_Config()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            // null config — stateful detection should still work
            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry(), null);

            Assert.Equal(1, adapter.StartConversationCallCount);
        }

        // What: AC3 — 6-param constructor with explicit config still detects stateful
        // Mutation: Fails if 6-param constructor skips stateful detection
        [Fact]
        public void AC3_SixParamConstructor_WithConfig_CallsStartConversation()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);
            var config = new GameSessionConfig(startingInterest: 15);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry(), config);

            Assert.Equal(1, adapter.StartConversationCallCount);
        }

        #endregion

        #region AC4: System prompt format

        // What: AC4 — System prompt contains player prompt first, separator, then opponent prompt
        // Mutation: Fails if player/opponent order is swapped or separator is wrong
        [Fact]
        public void AC4_SystemPrompt_PlayerFirst_SeparatorThenOpponent()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "PLAYER_PROMPT_CONTENT");
            var opponent = MakeProfile("Sable", "OPPONENT_PROMPT_CONTENT");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.NotNull(adapter.LastSystemPrompt);
            var expected = "PLAYER_PROMPT_CONTENT\n\n---\n\nOPPONENT_PROMPT_CONTENT";
            Assert.Equal(expected, adapter.LastSystemPrompt);
        }

        // What: AC4 — System prompt uses \n\n---\n\n as separator (not just --- or \n---\n)
        // Mutation: Fails if separator format is wrong (e.g., single newline, missing dashes)
        [Fact]
        public void AC4_SystemPrompt_Separator_Is_DoubleNewline_TripleDash_DoubleNewline()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("A", "PlayerPrompt");
            var opponent = MakeProfile("B", "OpponentPrompt");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            // Verify the exact separator
            Assert.Contains("\n\n---\n\n", adapter.LastSystemPrompt);
            // Verify it's not some other pattern
            var parts = adapter.LastSystemPrompt!.Split(new[] { "\n\n---\n\n" }, StringSplitOptions.None);
            Assert.Equal(2, parts.Length);
            Assert.Equal("PlayerPrompt", parts[0]);
            Assert.Equal("OpponentPrompt", parts[1]);
        }

        // What: AC4 — System prompt uses actual AssembledSystemPrompt values
        // Mutation: Fails if GameSession passes DisplayName or some other field instead of AssembledSystemPrompt
        [Fact]
        public void AC4_SystemPrompt_Uses_AssembledSystemPrompt_Not_DisplayName()
        {
            var adapter = new SpyStatefulAdapter();
            // DisplayName is "Velvet" but AssembledSystemPrompt is different
            var player = MakeProfile("Velvet", "Full system prompt for Velvet with all character details");
            var opponent = MakeProfile("Sable", "Full system prompt for Sable with all character details");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.Contains("Full system prompt for Velvet with all character details", adapter.LastSystemPrompt);
            Assert.Contains("Full system prompt for Sable with all character details", adapter.LastSystemPrompt);
            // Should NOT contain just the display names as the full prompt
            Assert.NotEqual("Velvet\n\n---\n\nSable", adapter.LastSystemPrompt);
        }

        #endregion

        #region AC5: Backward compatibility with non-stateful adapters

        // What: AC5 — NullLlmAdapter does NOT implement IStatefulLlmAdapter
        // Mutation: Fails if NullLlmAdapter gains IStatefulLlmAdapter (would break all existing tests)
        [Fact]
        public void AC5_NullLlmAdapter_Is_Not_IStatefulLlmAdapter()
        {
            var adapter = new NullLlmAdapter();
            Assert.False(adapter is IStatefulLlmAdapter);
        }

        // What: AC5 — GameSession constructs normally with NullLlmAdapter (no StartConversation call)
        // Mutation: Fails if GameSession unconditionally calls StartConversation on any adapter
        [Fact]
        public void AC5_GameSession_WithNullLlmAdapter_Constructs_Without_Error()
        {
            var adapter = new NullLlmAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.NotNull(session);
        }

        // What: AC5 — Existing turn flow works unchanged with NullLlmAdapter
        // Mutation: Fails if stateful wiring somehow breaks the non-stateful code path
        [Fact]
        public async Task AC5_NullLlmAdapter_TurnFlow_Unchanged()
        {
            var adapter = new NullLlmAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5, 15, 50);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            var turnStart = await session.StartTurnAsync();
            Assert.NotNull(turnStart);
            Assert.NotNull(turnStart.Options);
            Assert.True(turnStart.Options.Length > 0);
        }

        #endregion

        #region Edge Cases

        // What: Edge case — Multiple GameSessions sharing one adapter, second replaces first session
        // Mutation: Fails if StartConversation throws instead of replacing on second call
        [Fact]
        public void EdgeCase_MultipleGameSessions_SameAdapter_SecondReplacesFirst()
        {
            var adapter = new SpyStatefulAdapter();
            var player1 = MakeProfile("Velvet", "Session1_Player");
            var opponent1 = MakeProfile("Sable", "Session1_Opponent");
            var player2 = MakeProfile("Brick", "Session2_Player");
            var opponent2 = MakeProfile("Zyx", "Session2_Opponent");
            var dice = new FixedDice(5, 5); // two horniness rolls, one per GameSession

            var session1 = new GameSession(player1, opponent1, adapter, dice, new NullTrapRegistry());
            Assert.Contains("Session1_Player", adapter.LastSystemPrompt);

            // Second GameSession with same adapter — should replace, not throw
            var session2 = new GameSession(player2, opponent2, adapter, dice, new NullTrapRegistry());
            Assert.Equal(2, adapter.StartConversationCallCount);
            Assert.Contains("Session2_Player", adapter.LastSystemPrompt);
            Assert.DoesNotContain("Session1_Player", adapter.LastSystemPrompt);
        }

        // What: Edge case — HasActiveConversation is false before StartConversation
        // Mutation: Fails if HasActiveConversation defaults to true
        [Fact]
        public void EdgeCase_HasActiveConversation_FalseBeforeStartConversation()
        {
            var adapter = new SpyStatefulAdapter();
            Assert.False(adapter.HasActiveConversation);
        }

        // What: Edge case — 5-param constructor (delegates to 6-param with null config) still detects stateful
        // Mutation: Fails if 5-param constructor bypasses stateful detection logic
        [Fact]
        public void EdgeCase_FiveParamConstructor_StillDetectsStateful()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            // 5-param constructor
            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCallCount);
            Assert.True(adapter.HasActiveConversation);
        }

        // What: Edge case — Empty assembled system prompt (CharacterProfile allows it)
        // Mutation: Fails if GameSession guards against empty prompts and skips StartConversation
        [Fact]
        public void EdgeCase_EmptyAssembledPrompt_StillCallsStartConversation()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "");
            var opponent = MakeProfile("Sable", "");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCallCount);
            // Prompt should still have the separator even if both parts are empty
            Assert.Equal("\n\n---\n\n", adapter.LastSystemPrompt);
        }

        #endregion

        #region Integration: Stateful adapter with full turn flow

        // What: Spec Example 1 — stateful adapter works through StartTurnAsync
        // Mutation: Fails if stateful wiring breaks the GetDialogueOptionsAsync call path
        [Fact]
        public async Task Integration_StatefulAdapter_StartTurnAsync_Works()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            // horniness roll (1d10), then d20 for roll, d100 for ghost check
            var dice = new FixedDice(5, 15, 50);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.True(adapter.HasActiveConversation);
            var turnStart = await session.StartTurnAsync();
            Assert.NotNull(turnStart);
            Assert.Equal(4, turnStart.Options.Length);
            Assert.Equal(1, adapter.GetDialogueOptionsCallCount);
        }

        // What: Spec Example 1 — full Speak turn uses all adapter methods via stateful session
        // Mutation: Fails if ResolveTurnAsync skips DeliverMessageAsync or GetOpponentResponseAsync
        [Fact]
        public async Task Integration_StatefulAdapter_FullSpeakTurn()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            // Sequence: horniness(1d10), d20=15, d100=50 for ghost, then d20=15 for resolve
            var dice = new FixedDice(5, 15, 50, 15);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            var turnStart = await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result);
            Assert.Equal(1, adapter.GetDialogueOptionsCallCount);
            Assert.Equal(1, adapter.DeliverMessageCallCount);
            Assert.Equal(1, adapter.GetOpponentResponseCallCount);
        }

        // What: Edge case — ReadAsync works with stateful adapter (self-contained, no StartTurnAsync needed)
        // Mutation: Fails if stateful wiring breaks the Read action path
        [Fact]
        public async Task Integration_StatefulAdapter_ReadAsync_Works()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            // horniness(1d10), then d20=15 for read roll
            var dice = new FixedDice(5, 15);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            var readResult = await session.ReadAsync();
            Assert.NotNull(readResult);
        }

        #endregion
    }
}

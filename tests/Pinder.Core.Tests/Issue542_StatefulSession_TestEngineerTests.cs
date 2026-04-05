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
    /// Test-engineer spec-driven tests for Issue #542.
    /// Written from docs/specs/issue-542-spec.md only — verifies behavioral acceptance criteria.
    /// Each test states which mutation it would catch.
    /// Maturity: prototype (happy-path per AC).
    /// </summary>
    public class Issue542_StatefulSession_TestEngineerTests
    {
        #region Test-only helpers (not copied from implementation)

        private static CharacterProfile MakeProfile(string name, string prompt, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: prompt,
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Spy adapter implementing IStatefulLlmAdapter to record StartConversation calls.
        /// Returns canned responses so GameSession turn flow succeeds.
        /// </summary>
        private sealed class SpyStatefulAdapter : IStatefulLlmAdapter
        {
            public string? ReceivedSystemPrompt { get; private set; }
            public int StartConversationCalls { get; private set; }
            public bool HasActiveConversation => ReceivedSystemPrompt != null;

            public void StartConversation(string systemPrompt)
            {
                ReceivedSystemPrompt = systemPrompt;
                StartConversationCalls++;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Honesty, "Truth"),
                    new DialogueOption(StatType.Wit, "Joke"),
                    new DialogueOption(StatType.Chaos, "Wild")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("Ok."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
        }

        /// <summary>
        /// Plain ILlmAdapter (non-stateful) — simulates NullLlmAdapter pattern.
        /// </summary>
        private sealed class PlainAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Honesty, "Truth"),
                    new DialogueOption(StatType.Wit, "Joke"),
                    new DialogueOption(StatType.Chaos, "Wild")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("Fine."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
        }

        #endregion

        #region AC1: IStatefulLlmAdapter interface shape

        // Mutation: fails if IStatefulLlmAdapter doesn't extend ILlmAdapter (missing inheritance)
        [Fact]
        public void AC1_Interface_Inherits_ILlmAdapter()
        {
            Assert.True(typeof(ILlmAdapter).IsAssignableFrom(typeof(IStatefulLlmAdapter)));
        }

        // Mutation: fails if StartConversation is removed or renamed
        [Fact]
        public void AC1_Interface_Has_StartConversation_Void_String()
        {
            var method = typeof(IStatefulLlmAdapter).GetMethod("StartConversation");
            Assert.NotNull(method);
            Assert.Equal(typeof(void), method!.ReturnType);
            Assert.Single(method.GetParameters());
            Assert.Equal(typeof(string), method.GetParameters()[0].ParameterType);
        }

        // Mutation: fails if HasActiveConversation property is missing or returns wrong type
        [Fact]
        public void AC1_Interface_Has_HasActiveConversation_Bool()
        {
            var prop = typeof(IStatefulLlmAdapter).GetProperty("HasActiveConversation");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop!.PropertyType);
        }

        // Mutation: fails if interface moved to wrong namespace
        [Fact]
        public void AC1_Interface_In_Correct_Namespace()
        {
            Assert.Equal("Pinder.Core.Interfaces", typeof(IStatefulLlmAdapter).Namespace);
        }

        #endregion

        #region AC3: GameSession constructor detects IStatefulLlmAdapter and calls StartConversation

        // Mutation: fails if constructor never calls StartConversation (dead code / removed check)
        [Fact]
        public void AC3_StatefulAdapter_StartConversation_Called_Once_On_Construction()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCalls);
        }

        // Mutation: fails if constructor calls StartConversation on non-stateful adapters
        [Fact]
        public void AC3_NonStatefulAdapter_No_StartConversation_Called()
        {
            // PlainAdapter implements ILlmAdapter only — no StartConversation to call
            var adapter = new PlainAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            // Should not throw — stateless path
            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());
        }

        // Mutation: fails if stateful detection depends on config being non-null
        [Fact]
        public void AC3_StatefulDetection_Works_With_Null_Config()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry(), null);

            Assert.Equal(1, adapter.StartConversationCalls);
        }

        // Mutation: fails if 5-param constructor skips the stateful check
        [Fact]
        public void AC3_FiveParamConstructor_Also_Detects_Stateful()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            // 5-param constructor (no config param) should still detect
            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.True(adapter.HasActiveConversation);
        }

        // Mutation: fails if 6-param with explicit config skips stateful detection
        [Fact]
        public void AC3_SixParamConstructor_WithConfig_Detects_Stateful()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");
            var config = new GameSessionConfig(startingInterest: 15);

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry(), config);

            Assert.Equal(1, adapter.StartConversationCalls);
        }

        #endregion

        #region AC4: System prompt includes both profiles with separator

        // Mutation: fails if prompt uses only player prompt (opponent dropped)
        [Fact]
        public void AC4_SystemPrompt_Contains_Player_Prompt()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet — lowercase-with-intent, precise, ironic.");
            var opponent = MakeProfile("Sable", "You are Sable — omg, fast-talk energy.");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Contains("You are Velvet — lowercase-with-intent, precise, ironic.", adapter.ReceivedSystemPrompt);
        }

        // Mutation: fails if prompt uses only opponent prompt (player dropped)
        [Fact]
        public void AC4_SystemPrompt_Contains_Opponent_Prompt()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet — lowercase-with-intent, precise, ironic.");
            var opponent = MakeProfile("Sable", "You are Sable — omg, fast-talk energy.");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Contains("You are Sable — omg, fast-talk energy.", adapter.ReceivedSystemPrompt);
        }

        // Mutation: fails if separator is wrong (e.g. single newline, no dashes, or different format)
        [Fact]
        public void AC4_SystemPrompt_Separator_Is_NewlineNewline_TripleDash_NewlineNewline()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "PLAYER_PROMPT");
            var opponent = MakeProfile("Sable", "OPPONENT_PROMPT");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Contains("\n\n---\n\n", adapter.ReceivedSystemPrompt);
        }

        // Mutation: fails if player and opponent are swapped in the prompt
        [Fact]
        public void AC4_SystemPrompt_Player_Comes_Before_Opponent()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "PLAYER_FIRST");
            var opponent = MakeProfile("Sable", "OPPONENT_SECOND");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            var prompt = adapter.ReceivedSystemPrompt!;
            int playerIdx = prompt.IndexOf("PLAYER_FIRST");
            int opponentIdx = prompt.IndexOf("OPPONENT_SECOND");
            Assert.True(playerIdx < opponentIdx, "Player prompt must appear before opponent prompt");
        }

        // Mutation: fails if prompt is built from DisplayName instead of AssembledSystemPrompt
        [Fact]
        public void AC4_SystemPrompt_Uses_AssembledSystemPrompt_Not_DisplayName()
        {
            var adapter = new SpyStatefulAdapter();
            // DisplayName="Velvet" but AssembledSystemPrompt is different
            var player = MakeProfile("Velvet", "Full player system prompt with detailed instructions.");
            var opponent = MakeProfile("Sable", "Full opponent system prompt with character details.");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Contains("Full player system prompt with detailed instructions.", adapter.ReceivedSystemPrompt);
            Assert.Contains("Full opponent system prompt with character details.", adapter.ReceivedSystemPrompt);
        }

        // Mutation: fails if prompt is exact format "player\n\n---\n\nopponent"
        [Fact]
        public void AC4_SystemPrompt_Exact_Format()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("V", "AAA");
            var opponent = MakeProfile("S", "BBB");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Equal("AAA\n\n---\n\nBBB", adapter.ReceivedSystemPrompt);
        }

        #endregion

        #region AC5: NullLlmAdapter backward compatibility

        // Mutation: fails if NullLlmAdapter somehow implements IStatefulLlmAdapter
        [Fact]
        public void AC5_NullLlmAdapter_Not_IStatefulLlmAdapter()
        {
            var adapter = new NullLlmAdapter();
            Assert.False(adapter is IStatefulLlmAdapter);
        }

        // Mutation: fails if GameSession constructor throws for non-stateful adapter
        [Fact]
        public void AC5_GameSession_WithNullLlmAdapter_Constructs_Without_Error()
        {
            var adapter = new NullLlmAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            var session = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());
            Assert.NotNull(session);
        }

        // Mutation: fails if non-stateful path breaks turn flow
        [Fact]
        public async Task AC5_NullLlmAdapter_StartTurnAsync_Still_Works()
        {
            var adapter = new NullLlmAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            var session = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());
            var turn = await session.StartTurnAsync();

            Assert.NotNull(turn);
            Assert.NotNull(turn.Options);
        }

        #endregion

        #region Edge cases from spec

        // Mutation: fails if HasActiveConversation returns true before StartConversation
        [Fact]
        public void Edge_HasActiveConversation_False_Before_StartConversation()
        {
            var adapter = new SpyStatefulAdapter();
            Assert.False(adapter.HasActiveConversation);
        }

        // Mutation: fails if second GameSession sharing same adapter throws instead of replacing
        [Fact]
        public void Edge_SecondGameSession_SameAdapter_Replaces_Session()
        {
            var adapter = new SpyStatefulAdapter();
            var player1 = MakeProfile("Velvet", "PROMPT_V1");
            var opponent1 = MakeProfile("Sable", "PROMPT_S1");
            var player2 = MakeProfile("Gerald", "PROMPT_G2");
            var opponent2 = MakeProfile("Brick", "PROMPT_B2");

            var _ = new GameSession(player1, opponent1, adapter, new FixedDice(10), new NullTrapRegistry());
            Assert.Contains("PROMPT_V1", adapter.ReceivedSystemPrompt);

            var __ = new GameSession(player2, opponent2, adapter, new FixedDice(10), new NullTrapRegistry());
            Assert.Contains("PROMPT_G2", adapter.ReceivedSystemPrompt);
            Assert.Equal(2, adapter.StartConversationCalls);
        }

        // Mutation: fails if empty AssembledSystemPrompt prevents StartConversation call
        [Fact]
        public void Edge_EmptyPrompt_StillCallsStartConversation()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "");
            var opponent = MakeProfile("Sable", "");

            var _ = new GameSession(player, opponent, adapter, new FixedDice(10), new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCalls);
            // Prompt is "\n\n---\n\n" (empty sections around separator)
            Assert.Contains("---", adapter.ReceivedSystemPrompt);
        }

        // Mutation: fails if stateful adapter breaks turn integration after construction
        [Fact]
        public async Task Edge_StatefulAdapter_FullTurn_Works()
        {
            var adapter = new SpyStatefulAdapter();
            var player = MakeProfile("Velvet", "You are Velvet.");
            var opponent = MakeProfile("Sable", "You are Sable.");

            // Supply plenty of dice values for all rolls the engine may need
            // (ghost check, speak roll, timing delay, shadow growth, etc.)
            var session = new GameSession(player, opponent, adapter,
                new FixedDice(10, 10, 10, 10, 10, 10, 10, 10, 10, 10), new NullTrapRegistry());

            var turn = await session.StartTurnAsync();
            Assert.NotNull(turn);
            Assert.True(turn.Options.Length >= 1);

            // Resolve first option
            var result = await session.ResolveTurnAsync(0);
            Assert.NotNull(result);
        }

        #endregion
    }
}

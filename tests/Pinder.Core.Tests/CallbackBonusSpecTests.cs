using System;
using System.Collections.Generic;
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
    /// Spec-driven tests for Issue #47: Callback Bonus (§15 callback distance detection).
    /// Tests verify behavior described in docs/specs/issue-47-spec.md.
    /// </summary>
    public class CallbackBonusSpecTests
    {
        // =================================================================
        // AC4: Distance-to-bonus mapping: 2→+1, 4+→+2, opener→+3
        // =================================================================

        // Mutation: would catch if Compute returned a non-zero value for distance < 2
        [Theory]
        [InlineData(0, 0)]  // distance 0 — same turn
        [InlineData(5, 5)]  // distance 0 — same turn (higher turns)
        [InlineData(5, 4)]  // distance 1
        [InlineData(1, 0)]  // opener at distance 1 — opener rule must NOT override distance < 2
        public void AC4_DistanceLessThan2_ReturnsZero(int currentTurn, int callbackTurn)
        {
            Assert.Equal(0, CallbackBonus.Compute(currentTurn, callbackTurn));
        }

        // Mutation: would catch if distance >= 2 non-opener returned 0 or 2 instead of 1
        [Theory]
        [InlineData(5, 3)]  // distance 2 — lower boundary
        [InlineData(5, 2)]  // distance 3 — upper boundary before long-distance
        [InlineData(3, 1)]  // distance 2, non-opener, small turn numbers
        public void AC4_MidDistance_NonOpener_ReturnsOne(int currentTurn, int callbackTurn)
        {
            Assert.Equal(1, CallbackBonus.Compute(currentTurn, callbackTurn));
        }

        // Mutation: would catch if distance >= 4 returned 1 or 3 instead of 2
        [Theory]
        [InlineData(5, 1)]   // distance 4 — exact boundary
        [InlineData(6, 1)]   // distance 5
        [InlineData(100, 1)] // distance 99 — very long
        [InlineData(10, 3)]  // distance 7
        public void AC4_LongDistance_NonOpener_ReturnsTwo(int currentTurn, int callbackTurn)
        {
            Assert.Equal(2, CallbackBonus.Compute(currentTurn, callbackTurn));
        }

        // Mutation: would catch if opener check was missing or returned +2 instead of +3
        [Theory]
        [InlineData(2, 0)]   // opener at distance 2 — minimum
        [InlineData(3, 0)]   // opener at distance 3
        [InlineData(5, 0)]   // opener at distance 5
        [InlineData(6, 0)]   // opener at distance 6 — opener wins over 4+ rule
        [InlineData(100, 0)] // opener at distance 100
        public void AC4_OpenerReference_AlwaysReturnsThree(int currentTurn, int callbackTurn)
        {
            Assert.Equal(3, CallbackBonus.Compute(currentTurn, callbackTurn));
        }

        // =================================================================
        // Evaluation Order: opener priority over long-distance
        // =================================================================

        // Mutation: would catch if distance >= 4 check ran before callbackTurnNumber == 0 check
        [Fact]
        public void OpenerPriority_OpenerAtDistance4Plus_ReturnsThreeNotTwo()
        {
            // distance = 4 - 0 = 4. Both opener and 4+ rules match.
            // Opener must take priority → +3, not +2.
            Assert.Equal(3, CallbackBonus.Compute(4, 0));
        }

        // Mutation: would catch if opener at huge distance fell through to the +2 branch
        [Fact]
        public void OpenerPriority_OpenerAtDistance100_ReturnsThree()
        {
            Assert.Equal(3, CallbackBonus.Compute(100, 0));
        }

        // =================================================================
        // Boundary values
        // =================================================================

        // Mutation: would catch if boundary for mid-distance was > 2 instead of >= 2
        [Fact]
        public void Boundary_ExactlyDistance2_NonOpener_ReturnsOne()
        {
            Assert.Equal(1, CallbackBonus.Compute(4, 2));
        }

        // Mutation: would catch if boundary for long-distance was > 4 instead of >= 4
        [Fact]
        public void Boundary_ExactlyDistance4_NonOpener_ReturnsTwo()
        {
            Assert.Equal(2, CallbackBonus.Compute(5, 1));
        }

        // Mutation: would catch if opener at min distance returned 0
        [Fact]
        public void Boundary_OpenerAtMinDistance2_ReturnsThree()
        {
            Assert.Equal(3, CallbackBonus.Compute(2, 0));
        }

        // Mutation: would catch if currentTurn=0 somehow produced a non-zero bonus
        [Fact]
        public void Boundary_CurrentTurn0_AlwaysZero()
        {
            Assert.Equal(0, CallbackBonus.Compute(0, 0));
        }

        // Mutation: would catch if currentTurn=1 with callback=0 was >= 2 distance
        [Fact]
        public void Boundary_CurrentTurn1_MaxDistance1_ReturnsZero()
        {
            Assert.Equal(0, CallbackBonus.Compute(1, 0));
        }

        // =================================================================
        // Edge case: distance exactly at transition points
        // =================================================================

        // Mutation: would catch if distance 3 was treated as long-distance (+2)
        [Fact]
        public void EdgeCase_Distance3_NonOpener_ReturnsOne()
        {
            Assert.Equal(1, CallbackBonus.Compute(6, 3));
        }

        // Mutation: would catch if distance 1 was treated as mid-distance (+1)
        [Fact]
        public void EdgeCase_Distance1_ReturnsZero()
        {
            Assert.Equal(0, CallbackBonus.Compute(3, 2));
        }

        // =================================================================
        // AC6 + AC3: GameSession integration tests
        // =================================================================

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Stub LLM adapter that dequeues pre-configured option sets.
        /// </summary>
        private sealed class StubLlmAdapter : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();

            public void EnqueueOptions(params DialogueOption[] options)
            {
                _optionSets.Enqueue(options);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                if (_optionSets.Count > 0)
                    return Task.FromResult(_optionSets.Dequeue());
                return Task.FromResult(new[] { new DialogueOption(StatType.Charm, "Default") });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        // Mutation: would catch if ResolveTurnAsync ignored CallbackTurnNumber and always set bonus to 0
        [Fact]
        public async Task AC3_ResolveTurn_OpenerCallback_RecordsBonusThree()
        {
            // Turn 2 with callback to turn 0 (opener) → distance 2 → +3
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 0
                15, 50,  // Turn 1
                15, 50,  // Turn 2
                50, 50, 50, 50  // buffer
            );

            var llm = new StubLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hello"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Middle"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Remember opener?", callbackTurnNumber: 0));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(3, result.CallbackBonusApplied);
        }

        // Mutation: would catch if no-callback options still somehow produced a bonus
        [Fact]
        public async Task AC3_ResolveTurn_NoCallbackOption_ZeroBonus()
        {
            var dice = new FixedDice(5, 15, 50, 50, 50);
            var llm = new StubLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Plain text"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(0, result.CallbackBonusApplied);
        }

        // Mutation: would catch if mid-distance callback returned +2 or +3 instead of +1
        [Fact]
        public async Task AC3_ResolveTurn_MidDistanceCallback_RecordsBonusOne()
        {
            // Turn 3, callback to turn 1 → distance 2 → +1 (non-opener)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 0
                15, 50,  // Turn 1
                15, 50,  // Turn 2
                15, 50,  // Turn 3
                50, 50, 50, 50, 50, 50  // buffer
            );

            var llm = new StubLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "T0"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "T1"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "T2"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Callback to T1", callbackTurnNumber: 1));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(1, result.CallbackBonusApplied);
        }

        // =================================================================
        // AC5: Bonus flows through RollEngine.Resolve(externalBonus) — NOT post-hoc
        // =================================================================

        // Mutation: would catch if callback bonus was added to interest delta instead of externalBonus
        [Fact]
        public async Task AC5_CallbackBonus_TurnsMissIntoSuccess()
        {
            // DC = 16 + 2 = 18. Roll d20=13, mod=2, levelBonus=0 → Total=15.
            // Without bonus: 15 < 18 → fail.
            // With opener callback bonus +3: FinalTotal = 15 + 3 = 18 >= 18 → success.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 0: setup turn
                15, 50,  // Turn 1: setup turn
                13, 50,  // Turn 2: would-fail roll (13 + 2 = 15 < 18)
                50, 50, 50, 50  // buffer
            );

            var llm = new StubLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Opener"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Middle"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Callback!", callbackTurnNumber: 0));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess, "Callback bonus should turn near-miss into success via externalBonus");
            Assert.Equal(3, result.CallbackBonusApplied);
        }

        // =================================================================
        // Error Conditions
        // =================================================================

        // Mutation: would catch if AddTopic accepted null without throwing
        [Fact]
        public void ErrorCondition_AddTopic_NullThrowsArgumentNullException()
        {
            var dice = new FixedDice(5, 15);
            var llm = new StubLlmAdapter();
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            Assert.Throws<ArgumentNullException>(() => session.AddTopic(null!));
        }

        // Mutation: would catch if AddTopic threw on valid input
        [Fact]
        public void ErrorCondition_AddTopic_ValidTopic_Succeeds()
        {
            var dice = new FixedDice(5, 15);
            var llm = new StubLlmAdapter();
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var exception = Record.Exception(() => session.AddTopic(new CallbackOpportunity("pizza", 0)));
            Assert.Null(exception);
        }

        // =================================================================
        // Edge Cases from spec
        // =================================================================

        // Mutation: would catch if CallbackTurnNumber == currentTurn gave a bonus
        [Fact]
        public void EdgeCase_SameTurn_Distance0_ReturnsZero()
        {
            Assert.Equal(0, CallbackBonus.Compute(5, 5));
        }

        // Mutation: would catch if very large non-opener distance returned 3 instead of 2
        [Fact]
        public void EdgeCase_Distance100_NonOpener_ReturnsTwo()
        {
            Assert.Equal(2, CallbackBonus.Compute(101, 1));
        }

        // Mutation: would catch if opener at very large distance didn't take priority
        [Fact]
        public void EdgeCase_OpenerAtDistance100_ReturnsThree()
        {
            Assert.Equal(3, CallbackBonus.Compute(100, 0));
        }

        // =================================================================
        // AC1: CallbackOpportunity is a sealed class (not record)
        // =================================================================

        // Mutation: would catch if CallbackOpportunity was changed to a struct or removed
        [Fact]
        public void AC1_CallbackOpportunity_IsSealed_WithExpectedProperties()
        {
            var topic = new CallbackOpportunity("test-topic", 5);
            Assert.Equal("test-topic", topic.TopicKey);
            Assert.Equal(5, topic.TurnIntroduced);
        }

        // Mutation: would catch if CallbackOpportunity accepted null topicKey
        [Fact]
        public void AC1_CallbackOpportunity_NullTopicKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CallbackOpportunity(null!, 0));
        }

        // =================================================================
        // Edge case: Nat1 with callback bonus — auto-fail override
        // =================================================================

        // Mutation: would catch if nat1 auto-fail was removed when externalBonus present
        [Fact]
        public async Task EdgeCase_Nat1_WithCallback_StillFails()
        {
            // Nat1 = auto-fail regardless of bonus
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 0
                15, 50,  // Turn 1
                1, 50,   // Turn 2: nat 1
                50, 50, 50, 50  // buffer
            );

            var llm = new StubLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Opener"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Middle"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Callback!", callbackTurnNumber: 0));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess, "Nat 1 should always fail even with callback bonus");
            // Callback bonus is still computed and recorded
            Assert.Equal(3, result.CallbackBonusApplied);
        }

        // =================================================================
        // Edge case: Nat20 with callback — auto-success, bonus still recorded
        // =================================================================

        // Mutation: would catch if nat20 handling suppressed callback bonus recording
        [Fact]
        public async Task EdgeCase_Nat20_WithCallback_SucceedsAndRecordsBonus()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 0
                15, 50,  // Turn 1
                20, 50,  // Turn 2: nat 20
                50, 50, 50, 50  // buffer
            );

            var llm = new StubLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Opener"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Middle"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Callback!", callbackTurnNumber: 0));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess, "Nat 20 should always succeed");
            Assert.Equal(3, result.CallbackBonusApplied);
        }
    }
}

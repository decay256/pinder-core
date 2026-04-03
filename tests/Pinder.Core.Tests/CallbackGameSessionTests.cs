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
    /// Integration tests for callback bonus flowing through GameSession.ResolveTurnAsync.
    /// Verifies that CallbackBonus is computed and passed as externalBonus to RollEngine.Resolve,
    /// and that TurnResult.CallbackBonusApplied reflects the bonus.
    /// </summary>
    public class CallbackGameSessionTests
    {
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
        /// LLM adapter that returns options with configurable CallbackTurnNumber.
        /// </summary>
        private sealed class CallbackTestLlmAdapter : ILlmAdapter
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

        [Fact]
        public async Task ResolveTurn_WithCallbackOption_AppliesCallbackBonus()
        {
            // Setup: roll = 13, stat mod = 2, level bonus = 0 → total = 15
            // DC = 13 + 2 = 15. Without bonus: 15 >= 15 → success.
            // With callback bonus: FinalTotal = 15 + bonus.
            // We want to verify the bonus is recorded.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                // Turn 0: d20=15, d100=50 (timing)
                15, 50,
                // Turn 1: d20=15, d100=50 (timing)
                15, 50,
                // Turn 2: d20=15, d100=50 (timing)
                15, 50,
                // Extra buffer
                50, 50, 50, 50
            );

            var llm = new CallbackTestLlmAdapter();
            // Turn 0: no callback
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hello there"));
            // Turn 1: no callback
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Nice day"));
            // Turn 2: callback referencing turn 0 (opener) → distance 2 → +3
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Remember hello?", callbackTurnNumber: 0));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turn 0
            await session.StartTurnAsync();
            var r0 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, r0.CallbackBonusApplied);

            // Turn 1
            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, r1.CallbackBonusApplied);

            // Turn 2: callback to opener
            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal(3, r2.CallbackBonusApplied);
        }

        [Fact]
        public async Task ResolveTurn_CallbackBonusTurnsMissIntoSuccess()
        {
            // DC = 13 + 2 = 15. Roll = 12. Total = 12 + 2 + 0 = 14.
            // Without bonus: 14 < 15 → fail.
            // With callback bonus +3 (opener at distance 2): FinalTotal = 14 + 3 = 17 >= 15 → success.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                // Turn 0: d20=15, d100=50 (timing) — success to keep interest up
                15, 50,
                // Turn 1: d20=15, d100=50 (timing) — success
                15, 50,
                // Turn 2: d20=12, d100=50 (timing) — would fail without bonus
                12, 50,
                // Extra buffer
                50, 50, 50, 50
            );

            var llm = new CallbackTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Opener"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Middle"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Callback!", callbackTurnNumber: 0));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turn 0 & 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: callback should turn miss into success
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess, "Callback bonus should turn near-miss into success");
            Assert.Equal(3, result.CallbackBonusApplied);
        }

        [Fact]
        public async Task ResolveTurn_NoCallbackOption_ZeroBonus()
        {
            var dice = new FixedDice(5, 15, 50);
            var llm = new CallbackTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Just chatting"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(0, result.CallbackBonusApplied);
        }

        [Fact]
        public async Task ResolveTurn_MidDistanceCallback_ReturnsOne()
        {
            // Turn 3, callback to turn 1 → distance 2 → +1 (non-opener)
            // Each turn needs d20 (roll) + d100 (timing delay)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 0: d20, d100
                15, 50,  // Turn 1: d20, d100
                15, 50,  // Turn 2: d20, d100
                15, 50,  // Turn 3: d20, d100
                50, 50, 50, 50, 50, 50, 50, 50  // extra buffer for any additional rolls
            );

            var llm = new CallbackTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "T0"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "T1"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "T2"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Callback to T1", callbackTurnNumber: 1));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Turn 3
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.Equal(1, result.CallbackBonusApplied);
        }

        [Fact]
        public void AddTopic_NullThrows()
        {
            var dice = new FixedDice(5, 15);
            var llm = new CallbackTestLlmAdapter();
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            Assert.Throws<ArgumentNullException>(() => session.AddTopic(null!));
        }

        [Fact]
        public void AddTopic_ValidTopic_DoesNotThrow()
        {
            var dice = new FixedDice(5, 15);
            var llm = new CallbackTestLlmAdapter();
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            session.AddTopic(new CallbackOpportunity("pizza", 0));
            // No exception means success
        }
    }
}

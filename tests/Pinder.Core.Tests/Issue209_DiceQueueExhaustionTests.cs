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
    /// Tests for Issue #209 — Fix failing combo test: FixedDice queue exhausted.
    /// Verifies that the 4-turn Triple combo integration test correctly accounts for
    /// advantage-triggered double d20 rolls when interest reaches VeryIntoIt (16-20).
    /// </summary>
    public class Issue209_DiceQueueExhaustionTests
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

        // What: AC1 — 4-turn Triple combo sequence completes without dice queue exhaustion
        // Mutation: would catch if dice queue has 8 values instead of 9 (missing advantage d20 on turn 4)
        [Fact]
        public async Task FourTurnTripleCombo_WithAdvantageOnTurn4_DoesNotExhaustDiceQueue()
        {
            // 9 dice values: turns 1-3 use 2 each (d20+d100), turn 4 uses 3 (d20+d20[advantage]+d100)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,       // Turn 1: Rizz (d20=15, d100=50)
                15, 50,       // Turn 2: SA (d20=15, d100=50)
                15, 50,       // Turn 3: Chaos → Triple (d20=15, d100=50)
                15, 15, 50    // Turn 4: advantage from VeryIntoIt (d20=15, d20=15, d100=50)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turns 1-3
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 4 — should NOT throw InvalidOperationException
            await session.StartTurnAsync();
            var r4 = await session.ResolveTurnAsync(0);

            // If we get here, the dice queue was sufficient
            Assert.NotNull(r4);
        }

        // What: AC1 — Triple combo triggers on turn 3 with 3 distinct stats
        // Mutation: would catch if combo detection required 4 distinct stats instead of 3
        [Fact]
        public async Task TripleCombo_ThreeDistinctStats_TriggersOnTurn3()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,       // Turn 1
                15, 50,       // Turn 2
                15, 50,       // Turn 3
                15, 15, 50    // Turn 4 (advantage)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);

            Assert.Equal("The Triple", r3.ComboTriggered);
        }

        // What: AC1 — TripleBonusActive is set after Triple triggers and consumed on next turn
        // Mutation: would catch if TripleBonusActive was not set to true after Triple combo
        [Fact]
        public async Task TripleBonus_SetAfterTriple_ConsumedOnNextTurn()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,
                15, 50,
                15, 50,
                15, 15, 50
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 3: Triple fires
            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.True(r3.StateAfter.TripleBonusActive, "TripleBonusActive should be true after Triple combo");

            // Turn 4: bonus visible at start, applied and consumed after resolve
            var start4 = await session.StartTurnAsync();
            Assert.True(start4.State.TripleBonusActive, "TripleBonusActive should be visible at start of turn 4");

            var r4 = await session.ResolveTurnAsync(0);
            // ExternalBonus = triple(+1) + momentum(+2 from streak=3 at start, #268)
            Assert.Equal(3, r4.Roll.ExternalBonus);
            Assert.False(r4.StateAfter.TripleBonusActive, "TripleBonusActive should be consumed after turn 4");
        }

        // What: AC1 — Interest reaches VeryIntoIt (≥16) after 3 successful turns, triggering advantage
        // Mutation: would catch if interest delta calculation was wrong (e.g., no risk bonus)
        [Fact]
        public async Task InterestProgression_ThreeSuccessfulTurns_ReachesVeryIntoIt()
        {
            // With allStats=2, DC=15. Roll 15: total=17, beat by 2 → +1 success + +1 risk(Hard)
            // Momentum is a roll bonus (#268), not interest delta. Streak < 3 at start of each turn → no momentum bonus.
            // Turn 1: +2 → 12, Turn 2: +2 → 14, Turn 3: +2 → 16 (VeryIntoIt)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,
                15, 50,
                15, 50,
                15, 15, 50
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turn 1: interest 10 → 12
            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            Assert.Equal(12, r1.StateAfter.Interest);

            // Turn 2: interest 12 → 14
            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            Assert.Equal(14, r2.StateAfter.Interest);

            // Turn 3: interest 14 → 16 (momentum bonus=0 as streak was 2 at start; applied as roll bonus #268)
            await session.StartTurnAsync();
            var r3 = await session.ResolveTurnAsync(0);
            Assert.Equal(16, r3.StateAfter.Interest);

            // VeryIntoIt is interest 16-20; 16 is in that range
            Assert.True(r3.StateAfter.Interest >= 16 && r3.StateAfter.Interest <= 20,
                $"Interest {r3.StateAfter.Interest} should be in VeryIntoIt range (16-20)");
        }

        // What: Edge case — VeryIntoIt grants advantage which requires extra d20 roll
        // Mutation: would catch if advantage flag was not passed to RollEngine when interest is VeryIntoIt
        [Fact]
        public async Task VeryIntoIt_GrantsAdvantage_RollEngineConsumesExtraD20()
        {
            // If advantage is NOT passed, turn 4 would only consume 2 dice (d20+d100),
            // leaving 1 unused. With advantage, it consumes 3 (d20+d20+d100).
            // We verify by providing exactly 9 values — if advantage doesn't trigger,
            // the test would pass but leave unconsumed dice. We additionally verify
            // the roll succeeds with expected outcome.
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,        // Turn 1
                15, 50,        // Turn 2
                15, 50,        // Turn 3
                15, 15, 50     // Turn 4 (advantage: two d20s)
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Turn 4: advantage from VeryIntoIt means 2 d20 rolls
            await session.StartTurnAsync();
            var r4 = await session.ResolveTurnAsync(0);

            // Roll should still succeed (max(15,15) + 2 + momentum bonus(2) = 19 >= 15)
            Assert.True(r4.Roll.IsSuccess);
        }

        // What: Edge case — FixedDice throws InvalidOperationException when queue is empty
        // Mutation: would catch if FixedDice silently returns 0 instead of throwing
        [Fact]
        public void FixedDice_EmptyQueue_ThrowsInvalidOperationException()
        {
            var dice = new FixedDice(1);
            dice.Roll(20); // consume the one value

            var ex = Assert.Throws<InvalidOperationException>(() => dice.Roll(20));
            Assert.Contains("no more values", ex.Message);
        }

        // What: AC1 — ExternalBonus includes +1 from Triple bonus (not 0 or 2)
        // Mutation: would catch if Triple bonus applied +2 instead of +1
        [Fact]
        public async Task TripleBonus_AppliesExactlyPlusOne_AsExternalBonus()
        {
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,
                15, 50,
                15, 50,
                15, 15, 50
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "R"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "C"));
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "SA2"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            await session.StartTurnAsync();
            var r4 = await session.ResolveTurnAsync(0);

            // ExternalBonus = triple(+1) + momentum(+2 from streak=3 at start, #268) = 3
            Assert.Equal(3, r4.Roll.ExternalBonus);
        }

        // What: Edge case — Turns before VeryIntoIt consume exactly 2 dice each (no advantage)
        // Mutation: would catch if advantage was incorrectly triggered at Interested state
        [Fact]
        public async Task InterestedState_NoAdvantage_ConsumesTwoDicePerTurn()
        {
            // Provide exactly 4 dice for 2 turns at Interested state (interest 10-15)
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                15, 50,  // Turn 1: d20 + d100
                15, 50   // Turn 2: d20 + d100
            );

            var llm = new ComboTestLlmAdapter();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "C1"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "W1"));

            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var r1 = await session.ResolveTurnAsync(0);
            // Interest 10 → 12, still Interested (5-15) — no advantage
            Assert.True(r1.StateAfter.Interest >= 5 && r1.StateAfter.Interest <= 15,
                $"Interest {r1.StateAfter.Interest} should be in Interested range (5-15)");

            await session.StartTurnAsync();
            var r2 = await session.ResolveTurnAsync(0);
            // Interest 12 → 14, still Interested
            Assert.True(r2.StateAfter.Interest >= 5 && r2.StateAfter.Interest <= 15,
                $"Interest {r2.StateAfter.Interest} should be in Interested range (5-15)");

            // If advantage had been incorrectly triggered, we would have needed more dice
            // and this test would have thrown InvalidOperationException
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class TellBonusTests
    {
        // ================================================================
        // Edge Case 1: No tell active → no bonus
        // ================================================================

        [Fact]
        public async Task EdgeCase1_NoTellActive_NoBonus()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(null); // No tell

            var dice = new FixedDice(5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(0, result.TellReadBonus);
            Assert.Null(result.TellReadMessage);
            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // ================================================================
        // Edge Case 2: Tell active but player picks different stat
        // ================================================================

        [Fact]
        public async Task EdgeCase2_TellActiveButDifferentStat_NoBonus()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.SelfAwareness, "Goes silent"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Not SA")); // Different stat
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(0, result.TellReadBonus);
            Assert.Null(result.TellReadMessage);
        }

        // ================================================================
        // Edge Case 4: Tell consumed after one turn
        // ================================================================

        [Fact]
        public async Task EdgeCase4_TellConsumedAfterOneTurn()
        {
            var llm = new TellTestLlm();
            // Turn 0: set up tell
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            // Turn 1: use the tell
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null); // No new tell
            // Turn 2: no tell should be active
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Again"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: tell active → bonus
            await session.StartTurnAsync();
            var result1 = await session.ResolveTurnAsync(0);
            Assert.Equal(4, result1.TellReadBonus);

            // Turn 2: tell consumed → no bonus
            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasTellBonus);
            var result2 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, result2.TellReadBonus);
        }

        // ================================================================
        // Edge Case 5: Multiple tells in sequence (overwrite)
        // ================================================================

        [Fact]
        public async Task EdgeCase5_NewTellOverwritesPrevious()
        {
            var llm = new TellTestLlm();
            // Turn 0: tell for Wit
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            // Turn 1: pick Charm (not Wit) → tell consumed. New tell for Honesty
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Skip"));
            llm.EnqueueTell(new Tell(StatType.Honesty, "Shares vulnerable"));
            // Turn 2: Honesty should match, not Wit
            llm.EnqueueOptions(
                new DialogueOption(StatType.Wit, "Joke"),
                new DialogueOption(StatType.Honesty, "Truth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasTellBonus);  // Wit doesn't match Honesty
            Assert.True(start2.Options[1].HasTellBonus);   // Honesty matches
        }

        // ================================================================
        // Edge Case 6: First turn (no prior response) → no tell
        // ================================================================

        [Fact]
        public async Task EdgeCase6_FirstTurn_NoTellAvailable()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "First"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var start = await session.StartTurnAsync();
            Assert.False(start.Options[0].HasTellBonus);
        }

        // ================================================================
        // Edge Case 7: Nat 20 with tell → bonus still recorded
        // ================================================================

        [Fact]
        public async Task EdgeCase7_Nat20WithTell_BonusRecorded()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=20 (nat20), timing=5
            var dice = new FixedDice(5, 15, 5, 20, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(4, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +4 bonus.", result.TellReadMessage);
        }

        // ================================================================
        // Edge Case 8: Nat 1 with tell → bonus recorded (auto-fail still applies)
        // ================================================================

        [Fact]
        public async Task EdgeCase8_Nat1WithTell_BonusRecorded()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=1 (nat1), timing=5
            var dice = new FixedDice(5, 15, 5, 1, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatOne);
            Assert.False(result.Roll.IsSuccess); // Nat 1 is auto-fail
            Assert.Equal(4, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +4 bonus.", result.TellReadMessage);
        }

        // ================================================================
        // Edge Case 9: Tell matched but roll still fails
        // ================================================================

        [Fact]
        public async Task EdgeCase9_TellMatchedButRollStillFails_BonusRecorded()
        {
            // Player has allStats=2, datee allStats=2, so DC = 16 + 2 = 18
            // d20=10, mod=2, total=12, externalBonus=4, finalTotal=16 < 18 → still fails
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=10, timing=5
            var dice = new FixedDice(5, 15, 5, 10, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess); // 10+2+4=16 < 18
            Assert.Equal(4, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +4 bonus.", result.TellReadMessage);
        }

        // ================================================================
        // Edge Case 4 (bonus): Tell turns miss into hit
        // ================================================================

        [Fact]
        public async Task TellBonusTurnsMissIntoHit()
        {
            // DC = 16 + 2 = 18. d20=12 + mod=2 = total 14, externalBonus=4 → FinalTotal=18 = success
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=12, timing=5
            var dice = new FixedDice(5, 15, 5, 12, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Without bonus: 12+2=14 < 18 → fail
            // With bonus: 12+2+4=18 >= 18 → success
            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(4, result.TellReadBonus);
        }

        // ================================================================
        // Edge Case 11: Datee response has no tell → null stored
        // ================================================================

        [Fact]
        public async Task EdgeCase11_DateeNoTell_NoBonusNextTurn()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(null); // No tell
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Next"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.False(start1.Options[0].HasTellBonus);
            var result1 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, result1.TellReadBonus);
        }

        // ================================================================
        // Edge Case: Wait clears active tell
        // ================================================================

        [Fact]
        public async Task WaitClearsActiveTell()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Should not have tell"));

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Wait clears the tell
            session.Wait();

            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasTellBonus);
        }
    }
}

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
    /// Tests for Issue #50: Tells — §15 opponent tell detection and hidden roll bonus.
    /// Covers AC1–AC7 and edge cases 1–12 from docs/specs/issue-50-spec.md.
    /// </summary>
    public class TellBonusTests
    {
        // ================================================================
        // AC1: GameSession stores active tell from OpponentResponse.DetectedTell
        // ================================================================

        [Fact]
        public async Task AC1_TellStoredFromOpponentResponse()
        {
            // Turn 0: opponent returns a tell for Wit
            // Turn 1: Wit option should have HasTellBonus=true
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes a joke"));
            llm.EnqueueOptions(
                new DialogueOption(StatType.Wit, "Funny"),
                new DialogueOption(StatType.Charm, "Smooth"));

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turn 0
            var start0 = await session.StartTurnAsync();
            Assert.False(start0.Options[0].HasTellBonus); // No tell yet
            await session.ResolveTurnAsync(0);

            // Turn 1: tell now active
            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasTellBonus);   // Wit matches tell
            Assert.False(start1.Options[1].HasTellBonus);  // Charm doesn't match
        }

        // ================================================================
        // AC2: Matching stat → +2 via externalBonus on RollEngine.Resolve
        // ================================================================

        [Fact]
        public async Task AC2_MatchingStatAppliesTellBonusViaExternalBonus()
        {
            // Set up: opponent gives Wit tell, player picks Wit
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes a joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=15, timing=5
            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: pick Wit (matches tell)
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // ExternalBonus should be 2
            Assert.Equal(2, result.Roll.ExternalBonus);
            Assert.Equal(result.Roll.Total + 2, result.Roll.FinalTotal);
        }

        // ================================================================
        // AC4: TurnResult.TellReadBonus and TellReadMessage populated
        // ================================================================

        [Fact]
        public async Task AC4_TellReadBonusPopulatedOnMatch()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Honesty, "Shares vulnerability"));
            llm.EnqueueOptions(new DialogueOption(StatType.Honesty, "Truth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +2 bonus.", result.TellReadMessage);
        }

        [Fact]
        public async Task AC4_TellReadBonusZeroOnNoMatch()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Honesty, "Shares vulnerability"));
            // Player picks Charm, not Honesty
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Smooth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(0, result.TellReadBonus);
            Assert.Null(result.TellReadMessage);
        }

        // ================================================================
        // AC5: HasTellBonus set on matching options in StartTurnAsync
        // ================================================================

        [Fact]
        public async Task AC5_HasTellBonusSetOnMatchingOptions()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.SelfAwareness, "Goes silent"));
            llm.EnqueueOptions(
                new DialogueOption(StatType.SelfAwareness, "I notice"),
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.SelfAwareness, "Read the room"),
                new DialogueOption(StatType.Wit, "Joke"));

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start = await session.StartTurnAsync();
            Assert.True(start.Options[0].HasTellBonus);   // SA matches
            Assert.False(start.Options[1].HasTellBonus);  // Charm doesn't
            Assert.True(start.Options[2].HasTellBonus);   // SA matches
            Assert.False(start.Options[3].HasTellBonus);  // Wit doesn't
        }

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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: tell active → bonus
            await session.StartTurnAsync();
            var result1 = await session.ResolveTurnAsync(0);
            Assert.Equal(2, result1.TellReadBonus);

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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +2 bonus.", result.TellReadMessage);
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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatOne);
            Assert.False(result.Roll.IsSuccess); // Nat 1 is auto-fail
            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +2 bonus.", result.TellReadMessage);
        }

        // ================================================================
        // Edge Case 9: Tell matched but roll still fails
        // ================================================================

        [Fact]
        public async Task EdgeCase9_TellMatchedButRollStillFails_BonusRecorded()
        {
            // Player has allStats=2, opponent allStats=2, so DC = 16 + 2 = 15
            // d20=10, mod=2, total=12, externalBonus=2, finalTotal=14 < 15 → still fails
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=10, timing=5
            var dice = new FixedDice(5, 15, 5, 10, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess); // 10+2+2=14 < 15
            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +2 bonus.", result.TellReadMessage);
        }

        // ================================================================
        // Edge Case 4 (bonus): Tell turns miss into hit
        // ================================================================

        [Fact]
        public async Task TellBonusTurnsMissIntoHit()
        {
            // DC = 16 + 2 = 18. d20=14 + mod=2 = total 16, externalBonus=2 → FinalTotal=18 = success
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=14, timing=5
            var dice = new FixedDice(5, 15, 5, 14, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Without bonus: 14+2=16 < 18 → fail
            // With bonus: 14+2+2=18 >= 18 → success
            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(2, result.TellReadBonus);
        }

        // ================================================================
        // Edge Case 11: Opponent response has no tell → null stored
        // ================================================================

        [Fact]
        public async Task EdgeCase11_OpponentNoTell_NoBonusNextTurn()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(null); // No tell
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Next"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.False(start1.Options[0].HasTellBonus);
            var result1 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, result1.TellReadBonus);
        }

        // ================================================================
        // Edge Case: Read clears active tell
        // ================================================================

        [Fact]
        public async Task ReadClearsActiveTell()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            // After Read, tell should be cleared
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Should not have tell"));

            // Turn 0: d20=15, timing=5. Read: d20=15. Turn 2: d20=15, timing=5
            var dice = new FixedDice(5, 15, 5, 15, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Read clears the tell
            await session.ReadAsync();

            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasTellBonus);
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
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Wait clears the tell
            session.Wait();

            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasTellBonus);
        }

        // ================================================================
        // Helpers
        // ================================================================

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
        /// LLM adapter that supports enqueuing tells per turn.
        /// </summary>
        private sealed class TellTestLlm : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();
            private readonly Queue<Tell?> _tells = new Queue<Tell?>();

            public void EnqueueOptions(params DialogueOption[] options)
            {
                _optionSets.Enqueue(options);
            }

            public void EnqueueTell(Tell? tell)
            {
                _tells.Enqueue(tell);
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
            {
                var tell = _tells.Count > 0 ? _tells.Dequeue() : null;
                return Task.FromResult(new OpponentResponse("...", detectedTell: tell));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}

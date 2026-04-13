using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// Tests for Issue #49: Weakness Windows — §15 opponent crack detection.
    /// Covers AC1–AC7 and test scenarios T1–T7 from docs/specs/issue-49-spec.md.
    /// </summary>
    [Trait("Category", "Core")]
    public class WeaknessWindowTests
    {
        // ================================================================
        // T5 / AC1: DcReduction validation
        // ================================================================

        [Fact]
        public void T5_DcReduction_Zero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WeaknessWindow(StatType.Charm, 0));
        }

        [Fact]
        public void T5_DcReduction_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WeaknessWindow(StatType.Charm, -1));
        }

        [Fact]
        public void T5_DcReduction_Positive_Succeeds()
        {
            var ww = new WeaknessWindow(StatType.Charm, 1);
            Assert.Equal(StatType.Charm, ww.DefendingStat);
            Assert.Equal(1, ww.DcReduction);
        }

        [Fact]
        public void T5_DcReduction_LargeValue_Succeeds()
        {
            var ww = new WeaknessWindow(StatType.SelfAwareness, 3);
            Assert.Equal(StatType.SelfAwareness, ww.DefendingStat);
            Assert.Equal(3, ww.DcReduction);
        }

        // ================================================================
        // T1 / AC3: Window applied for one turn then cleared
        // ================================================================

        [Fact]
        public async Task T1_WindowAppliedOneTurnThenCleared()
        {
            // Turn 0: opponent returns a weakness window (Honesty, 2)
            // Turn 1: SA option should have HasWeaknessWindow=true, window consumed
            // Turn 2: no window active

            var llm = new WeaknessTestLlm();
            // Turn 0: return Charm option, opponent response has weakness window
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: return SA and Charm options, no window from opponent
            llm.EnqueueOptions(
                new DialogueOption(StatType.SelfAwareness, "I see"),
                new DialogueOption(StatType.Charm, "Cool"));
            llm.EnqueueWeaknessWindow(null);
            // Turn 2: return SA option
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "Hmm"));

            // Dice: each turn needs d20 roll + timing roll
            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            var start0 = await session.StartTurnAsync();
            Assert.False(start0.Options[0].HasWeaknessWindow); // No window yet
            var result0 = await session.ResolveTurnAsync(0);
            Assert.NotNull(result0.DetectedWindow);
            Assert.Equal(StatType.Honesty, result0.DetectedWindow!.DefendingStat);

            // Turn 1: SA option should have window (DefenceTable[SA] = Honesty = match)
            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasWeaknessWindow);  // SA → defended by Honesty → match
            Assert.False(start1.Options[1].HasWeaknessWindow); // Charm → defended by SA → no match
            var result1 = await session.ResolveTurnAsync(0);
            Assert.Null(result1.DetectedWindow); // No new window

            // Turn 2: no window active
            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasWeaknessWindow);
        }

        // ================================================================
        // T2 / AC5: Correct stat DC reduced
        // ================================================================

        [Fact]
        public async Task T2_CorrectStatDcReduced()
        {
            // Opponent has allStats=2, so Honesty mod = 2 → DC = 16+2 = 18
            // With weakness window DcReduction=2 → DC should be 16

            var llm = new WeaknessTestLlm();
            // Turn 0: Charm option, opponent returns window(Honesty, 2)
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: SA option (defended by Honesty → window applies)
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "I notice"));
            llm.EnqueueWeaknessWindow(null);

            // Turn 0: roll 15, timing roll
            // Turn 1: roll 14 (SA mod=2, level bonus=0, total=16). Normal DC=18 → fail. With dcAdj=2 → DC=16 → success.
            var dice = new FixedDice(5, 15, 5, 14, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: SA option with weakness window
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Roll=14, stat mod=2, DC should be 16 (18-2)
            Assert.Equal(16, result.Roll.DC);
            Assert.True(result.Roll.IsSuccess); // 14+2=16 >= 16
        }

        // ================================================================
        // T3 / AC3: No window → no reduction
        // ================================================================

        [Fact]
        public async Task T3_NoWindowNoReduction()
        {
            var llm = new WeaknessTestLlm();
            // Turn 0: no window from opponent
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var start = await session.StartTurnAsync();
            Assert.False(start.Options[0].HasWeaknessWindow);

            var result = await session.ResolveTurnAsync(0);
            // DC for Charm → defended by SA (mod=2) → DC=16+2=18, no reduction
            Assert.Equal(18, result.Roll.DC);
        }

        // ================================================================
        // T4: Window clears even if not exploited
        // ================================================================

        [Fact]
        public async Task T4_WindowClearsEvenIfNotExploited()
        {
            // Window on Honesty. Player picks Charm (defended by SA, not Honesty).
            // Window should still clear after turn.

            var llm = new WeaknessTestLlm();
            // Turn 0: opponent returns window(Honesty, 2)
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: Charm option (doesn't match window)
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Cool"));
            llm.EnqueueWeaknessWindow(null);
            // Turn 2: SA option - should NOT have window
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "Hmm"));

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: pick Charm (doesn't match window on Honesty)
            var start1 = await session.StartTurnAsync();
            Assert.False(start1.Options[0].HasWeaknessWindow); // Charm→SA, not Honesty
            var result1 = await session.ResolveTurnAsync(0);
            // DC should be normal (no adjustment for Charm)
            Assert.Equal(18, result1.Roll.DC); // 16+2=18

            // Turn 2: no window should be active
            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasWeaknessWindow);
        }

        // ================================================================
        // T6: DetectedWindow in TurnResult
        // ================================================================

        [Fact]
        public async Task T6_DetectedWindowInTurnResult()
        {
            var llm = new WeaknessTestLlm();
            // Turn 0: opponent returns window
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Wit, 2));

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.NotNull(result.DetectedWindow);
            Assert.Equal(StatType.Wit, result.DetectedWindow!.DefendingStat);
            Assert.Equal(2, result.DetectedWindow.DcReduction);
        }

        [Fact]
        public async Task T6_DetectedWindowNull_WhenNoWindow()
        {
            var llm = new WeaknessTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Null(result.DetectedWindow);
        }

        // ================================================================
        // T7: Window does not apply to Read/Recover/Wait
        // ================================================================

        [Fact]
        public async Task T7_WaitClearsWeaknessWindow()
        {
            var llm = new WeaknessTestLlm();
            // Turn 0: set up a window
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 2: SA option - should NOT have window (cleared by Wait)
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "After wait"));

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: Wait
            session.Wait();

            // Turn 2: no window
            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasWeaknessWindow);
        }

        // ================================================================
        // Edge case: SelfAwareness overshare → Charm gets window (DC −3)
        // ================================================================

        [Fact]
        public async Task SelfAwarenessOvershare_CharmGetsWindow()
        {
            // Window on SelfAwareness → DefenceTable[Charm] = SA → Charm benefits
            var llm = new WeaknessTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Setup"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.SelfAwareness, 3));
            llm.EnqueueOptions(
                new DialogueOption(StatType.Charm, "Smooth"),
                new DialogueOption(StatType.Honesty, "Truth"));

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasWeaknessWindow);  // Charm → SA = match
            Assert.False(start1.Options[1].HasWeaknessWindow); // Honesty → Chaos ≠ SA
        }

        // ================================================================
        // Edge case: Consecutive cracks replace, don't stack
        // ================================================================

        [Fact]
        public async Task ConsecutiveCracks_Replace_NoStacking()
        {
            var llm = new WeaknessTestLlm();
            // Turn 0: window on Honesty
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: use SA (matches Honesty), opponent returns window on Charm
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "Deep"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Charm, 2));
            // Turn 2: should have Charm window (not Honesty)
            llm.EnqueueOptions(
                new DialogueOption(StatType.Chaos, "Wild"),      // Chaos → Charm = match
                new DialogueOption(StatType.SelfAwareness, "SA")); // SA → Honesty ≠ Charm

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: Charm window should be active
            var start2 = await session.StartTurnAsync();
            Assert.True(start2.Options[0].HasWeaknessWindow);  // Chaos → Charm = match
            Assert.False(start2.Options[1].HasWeaknessWindow); // SA → Honesty ≠ Charm
        }

        // ================================================================
        // Edge case: Window on first turn (no window at game start)
        // ================================================================

        [Fact]
        public async Task NoWindowAtGameStart()
        {
            var llm = new WeaknessTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "First"));

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var start = await session.StartTurnAsync();
            Assert.False(start.Options[0].HasWeaknessWindow);
        }

        // ================================================================
        // All 6 defence table mappings verified
        // ================================================================

        [Theory]
        [InlineData(StatType.Charm, StatType.SelfAwareness)]
        [InlineData(StatType.Rizz, StatType.Wit)]
        [InlineData(StatType.Honesty, StatType.Chaos)]
        [InlineData(StatType.Chaos, StatType.Charm)]
        [InlineData(StatType.Wit, StatType.Rizz)]
        [InlineData(StatType.SelfAwareness, StatType.Honesty)]
        public async Task DefenceTableMapping_WindowMatchesCorrectAttackStat(
            StatType attackStat, StatType defenceStat)
        {
            var llm = new WeaknessTestLlm();
            // Turn 0: set up window on defenceStat
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(defenceStat, 2));
            // Turn 1: offer both matching and non-matching stat
            var otherStat = attackStat == StatType.Charm ? StatType.Wit : StatType.Charm;
            llm.EnqueueOptions(
                new DialogueOption(attackStat, "Match"),
                new DialogueOption(otherStat, "NoMatch"));

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasWeaknessWindow);
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
        /// Activates a trap on the session via reflection.
        /// </summary>
        private static void ActivateTrapOnSession(GameSession session)
        {
            var trapDef = new TrapDefinition(
                "TestTrap", StatType.Honesty,
                TrapEffect.Disadvantage, 0, 3, "trap", "clear", "nat1");
            var trapsField = typeof(GameSession).GetField("_traps",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;
            trapState.Activate(trapDef);
        }

        /// <summary>
        /// LLM adapter that supports enqueuing weakness windows per turn.
        /// </summary>
        private sealed class WeaknessTestLlm : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();
            private readonly Queue<WeaknessWindow?> _weaknessWindows = new Queue<WeaknessWindow?>();

            public void EnqueueOptions(params DialogueOption[] options)
            {
                _optionSets.Enqueue(options);
            }

            public void EnqueueWeaknessWindow(WeaknessWindow? window)
            {
                _weaknessWindows.Enqueue(window);
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
                var window = _weaknessWindows.Count > 0 ? _weaknessWindows.Dequeue() : null;
                return Task.FromResult(new OpponentResponse("...", weaknessWindow: window));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}

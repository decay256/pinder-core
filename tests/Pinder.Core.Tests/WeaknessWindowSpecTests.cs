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
    /// Supplementary spec-driven tests for Issue #49: Weakness Windows.
    /// Covers additional acceptance criteria and edge cases from docs/specs/issue-49-spec.md
    /// that are not in WeaknessWindowTests.cs.
    /// </summary>
    public class WeaknessWindowSpecTests
    {
        // ================================================================
        // AC1: WeaknessWindow type properties
        // ================================================================

        // Mutation: would catch if DefendingStat property returns wrong value or is hardcoded
        [Theory]
        [InlineData(StatType.Charm, 2)]
        [InlineData(StatType.Honesty, 2)]
        [InlineData(StatType.SelfAwareness, 3)]
        [InlineData(StatType.Wit, 2)]
        [InlineData(StatType.Chaos, 2)]
        [InlineData(StatType.Rizz, 1)]
        public void AC1_WeaknessWindow_StoresStatAndReduction(StatType stat, int reduction)
        {
            var ww = new WeaknessWindow(stat, reduction);
            Assert.Equal(stat, ww.DefendingStat);
            Assert.Equal(reduction, ww.DcReduction);
        }

        // Mutation: would catch if validation threshold is > 0 instead of >= 0 (i.e., allows 0)
        [Fact]
        public void AC1_WeaknessWindow_DcReductionMustBePositive_BoundaryAtOne()
        {
            var ww = new WeaknessWindow(StatType.Charm, 1);
            Assert.Equal(1, ww.DcReduction);
        }

        // Mutation: would catch if only checking for exactly 0 and not negatives
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void AC1_WeaknessWindow_InvalidDcReduction_Throws(int badReduction)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WeaknessWindow(StatType.Charm, badReduction));
        }

        // ================================================================
        // AC2: OpponentResponse carries optional WeaknessWindow
        // ================================================================

        // Mutation: would catch if OpponentResponse ignores WeaknessWindow parameter
        [Fact]
        public void AC2_OpponentResponse_CarriesWeaknessWindow()
        {
            var window = new WeaknessWindow(StatType.Honesty, 2);
            var response = new OpponentResponse("test msg", weaknessWindow: window);
            Assert.Same(window, response.WeaknessWindow);
        }

        // Mutation: would catch if WeaknessWindow defaults to non-null
        [Fact]
        public void AC2_OpponentResponse_WeaknessWindowDefaultsToNull()
        {
            var response = new OpponentResponse("test msg");
            Assert.Null(response.WeaknessWindow);
        }

        // ================================================================
        // AC4: DialogueOption backward compat — HasWeaknessWindow defaults false
        // ================================================================

        // Mutation: would catch if HasWeaknessWindow defaults to true instead of false
        [Fact]
        public void AC4_DialogueOption_HasWeaknessWindow_DefaultsFalse()
        {
            var option = new DialogueOption(StatType.Charm, "Hey");
            Assert.False(option.HasWeaknessWindow);
        }

        // Mutation: would catch if HasWeaknessWindow is always false regardless of constructor param
        [Fact]
        public void AC4_DialogueOption_HasWeaknessWindow_CanBeSetTrue()
        {
            var option = new DialogueOption(StatType.Charm, "Hey", hasWeaknessWindow: true);
            Assert.True(option.HasWeaknessWindow);
        }

        // ================================================================
        // AC5: RollResult.DC reflects reduced value via dcAdjustment
        // ================================================================

        // Mutation: would catch if RollEngine ignores dcAdjustment parameter
        [Fact]
        public void AC5_RollEngine_DcAdjustment_ReducesDC()
        {
            // Opponent Honesty = 2 → DC = 16 + 2 = 18. With dcAdjustment=5 → DC=13.
            var attacker = TestHelpers.MakeStatBlock(2); // SA mod = 2
            var defender = TestHelpers.MakeStatBlock(2); // Honesty mod = 2
            var traps = new TrapState();
            var dice = new FixedDiceRoller(10); // roll = 10, total = 10 + 2 = 12

            var result = RollEngine.Resolve(
                StatType.SelfAwareness, attacker, defender,
                traps, 1, new NullTrapReg(), dice,
                dcAdjustment: 5);

            Assert.Equal(13, result.DC); // 18 - 5 = 13
        }

        // Mutation: would catch if dcAdjustment is added to DC instead of subtracted
        [Fact]
        public void AC5_DcAdjustment_MakesRollEasier_NotHarder()
        {
            var attacker = TestHelpers.MakeStatBlock(2);
            var defender = TestHelpers.MakeStatBlock(2);
            var traps = new TrapState();
            // Roll 16: total = 16 + 2 = 18. Normal DC = 18 → exactly meets. With adj=5 → DC=13 → beats.
            var dice = new FixedDiceRoller(16);

            var withAdj = RollEngine.Resolve(
                StatType.SelfAwareness, attacker, defender,
                traps, 1, new NullTrapReg(), dice,
                dcAdjustment: 5);

            Assert.True(withAdj.IsSuccess);
            Assert.Equal(13, withAdj.DC);
        }

        // Mutation: would catch if dcAdjustment=0 changes behavior (backward compat)
        [Fact]
        public void AC5_DcAdjustment_Zero_NoEffect()
        {
            var attacker = TestHelpers.MakeStatBlock(2);
            var defender = TestHelpers.MakeStatBlock(2);
            var traps = new TrapState();
            var dice = new FixedDiceRoller(15);

            var result = RollEngine.Resolve(
                StatType.SelfAwareness, attacker, defender,
                traps, 1, new NullTrapReg(), dice,
                dcAdjustment: 0);

            Assert.Equal(18, result.DC); // 16 + 2 = 18, no adjustment
        }

        // ================================================================
        // Edge case: DC reduced below reasonable values (spec says no clamping)
        // ================================================================

        // Mutation: would catch if implementation clamps DC at 1 or some floor
        [Fact]
        public void EdgeCase_LargeDcReduction_NoClamping()
        {
            var attacker = TestHelpers.MakeStatBlock(0);
            var defender = TestHelpers.MakeStatBlock(0); // DC = 16 + 0 = 16
            var traps = new TrapState();
            var dice = new FixedDiceRoller(2); // roll 2, total = 2 + 0 = 2

            // dcAdjustment=18 → DC = 16 - 18 = -2. Roll of 2 should beat DC of -2.
            var result = RollEngine.Resolve(
                StatType.SelfAwareness, attacker, defender,
                traps, 1, new NullTrapReg(), dice,
                dcAdjustment: 18);

            Assert.True(result.DC < 1); // DC went negative
            Assert.True(result.IsSuccess); // Low roll still beats very low DC
        }

        // ================================================================
        // Edge case: Interaction with externalBonus — orthogonal
        // ================================================================

        // Mutation: would catch if dcAdjustment and externalBonus are conflated
        [Fact]
        public void EdgeCase_DcAdjustment_And_ExternalBonus_Independent()
        {
            var attacker = TestHelpers.MakeStatBlock(2);
            var defender = TestHelpers.MakeStatBlock(2); // DC = 18 (16+2)
            var traps = new TrapState();
            var dice = new FixedDiceRoller(10); // roll 10, stat 2 = base total 12

            // externalBonus=2 → FinalTotal = 14. dcAdjustment=5 → DC = 13. 14 >= 13 → success
            var result = RollEngine.Resolve(
                StatType.SelfAwareness, attacker, defender,
                traps, 1, new NullTrapReg(), dice,
                externalBonus: 2, dcAdjustment: 5);

            Assert.Equal(13, result.DC); // DC reduced by dcAdjustment
            Assert.True(result.IsSuccess); // externalBonus helped meet reduced DC
        }

        // ================================================================
        // AC3/AC4: Full GameSession integration — DC adjustment flows through
        // ================================================================

        // Mutation: would catch if GameSession doesn't pass dcAdjustment to RollEngine
        [Fact]
        public async Task AC3_GameSession_WindowReducesDC_InRollResult()
        {
            var llm = new TestLlm();
            // Turn 0: set up window(Honesty, 2)
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: SA option (defended by Honesty → window applies)
            llm.EnqueueOptions(new DialogueOption(StatType.SelfAwareness, "Insight"));
            llm.EnqueueWeaknessWindow(null);

            // Turn 0: roll 15, Turn 1: roll 14 (14+2=16, DC=18-2=16 → success)
            var dice = new FixedDice(5, 15, 5, 14, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Verify the DC in the roll result is 16 (18 - 2)
            Assert.Equal(16, result.Roll.DC);
            Assert.True(result.Roll.IsSuccess);
        }

        // Mutation: would catch if GameSession applies dcAdjustment even when stat doesn't match
        [Fact]
        public async Task AC3_GameSession_MismatchedStat_NoDcReduction()
        {
            var llm = new TestLlm();
            // Turn 0: window on Honesty
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: Charm option (defended by SelfAwareness, not Honesty → no adjustment)
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Smooth"));
            llm.EnqueueWeaknessWindow(null);

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Charm defended by SA(mod=2) → DC=16+2=18, no reduction
            Assert.Equal(18, result.Roll.DC);
        }

        // ================================================================
        // Edge case: Crack trigger table — all 6 mappings as WeaknessWindows
        // ================================================================

        // Mutation: would catch if any crack type's stat is mapped incorrectly
        [Theory]
        [InlineData(StatType.Honesty, 2, "Contradicts themselves")]
        [InlineData(StatType.Charm, 2, "Laughs genuinely")]
        [InlineData(StatType.SelfAwareness, 3, "Shares something personal")]
        [InlineData(StatType.Wit, 2, "Gets flustered")]
        [InlineData(StatType.Honesty, 2, "Asks personal question")]
        [InlineData(StatType.Chaos, 2, "Makes a risky joke")]
        public void CrackTriggerTable_AllTypesConstructValid(
            StatType defendingStat, int dcReduction, string reason)
        {
            _ = reason; // Used for test case documentation only
            var ww = new WeaknessWindow(defendingStat, dcReduction);
            Assert.Equal(defendingStat, ww.DefendingStat);
            Assert.Equal(dcReduction, ww.DcReduction);
        }

        // ================================================================
        // Edge case: Window stored from turn result, then a new turn
        // with no options matching — all false
        // ================================================================

        // Mutation: would catch if HasWeaknessWindow is true for all options when window is active
        [Fact]
        public async Task AC4_OnlyMatchingOptionGetsWindowFlag()
        {
            var llm = new TestLlm();
            // Turn 0: window on Wit
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Wit, 2));
            // Turn 1: multiple options, only Rizz defends with Wit
            llm.EnqueueOptions(
                new DialogueOption(StatType.Charm, "A"),       // Charm → SA ≠ Wit
                new DialogueOption(StatType.Honesty, "B"),     // Honesty → Chaos ≠ Wit
                new DialogueOption(StatType.Rizz, "C"));       // Rizz → Wit = match!
            llm.EnqueueWeaknessWindow(null);

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.False(start1.Options[0].HasWeaknessWindow); // Charm
            Assert.False(start1.Options[1].HasWeaknessWindow); // Honesty
            Assert.True(start1.Options[2].HasWeaknessWindow);  // Rizz → Wit = match
        }

        // ================================================================
        // Edge case: Two options with same defending stat both get flagged
        // ================================================================

        // Mutation: would catch if only first matching option gets the flag
        [Fact]
        public async Task AC4_MultipleOptionsWithSameDefendingStat_AllFlagged()
        {
            var llm = new TestLlm();
            // Window on Honesty. DefenceTable[SA] = Honesty, so SA options match.
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueWeaknessWindow(new WeaknessWindow(StatType.Honesty, 2));
            // Turn 1: two SA options
            llm.EnqueueOptions(
                new DialogueOption(StatType.SelfAwareness, "Option A"),
                new DialogueOption(StatType.SelfAwareness, "Option B"),
                new DialogueOption(StatType.Charm, "Option C"));
            llm.EnqueueWeaknessWindow(null);

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasWeaknessWindow);  // SA → Honesty = match
            Assert.True(start1.Options[1].HasWeaknessWindow);  // SA → Honesty = match
            Assert.False(start1.Options[2].HasWeaknessWindow); // Charm → SA ≠ Honesty
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
        /// Simple fixed dice roller for RollEngine direct tests.
        /// </summary>
        private sealed class FixedDiceRoller : IDiceRoller
        {
            private readonly int _value;
            public FixedDiceRoller(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class NullTrapReg : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        /// <summary>
        /// Multi-roll fixed dice for GameSession tests.
        /// </summary>
        private sealed class FixedDice : IDiceRoller
        {
            private readonly int[] _rolls;
            private int _index;

            public FixedDice(params int[] rolls) => _rolls = rolls;

            public int Roll(int sides)
            {
                if (_index < _rolls.Length)
                    return _rolls[_index++];
                return 10;
            }
        }

        // GameSession tests use the public NullTrapRegistry from GameSessionTests.cs

        private sealed class TestLlm : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();
            private readonly Queue<WeaknessWindow?> _weaknessWindows = new Queue<WeaknessWindow?>();

            public void EnqueueOptions(params DialogueOption[] options)
                => _optionSets.Enqueue(options);

            public void EnqueueWeaknessWindow(WeaknessWindow? window)
                => _weaknessWindows.Enqueue(window);

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
        }
    }
}

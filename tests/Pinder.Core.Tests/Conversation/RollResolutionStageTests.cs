using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    [Trait("Category", "Core")]
    public class RollResolutionStageTests
    {
        private class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private static RollResolutionStage BuildRollResolutionStage(
            IDiceRoller dice,
            double activeTrapInterestPenalty = 0.0,
            int globalDcBias = 0)
        {
            return new RollResolutionStage(
                dice,
                new EmptyTrapRegistry(),
                null,
                null,
                new SessionXpRecorder(new XpLedger(), null),
                globalDcBias,
                activeTrapInterestPenalty);
        }

        private static CharacterProfile BuildProfile(string name, int statVal = 2)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: TestHelpers.MakeStatBlock(statVal),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public void Execute_WithSelfAwarenessSelected_ActiveTrapCleared()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] { new DialogueOption(StatType.SelfAwareness, "Option 1") };
            
            var trapDef = new TrapDefinition(
                id: "test_trap",
                stat: StatType.Charm,
                effect: TrapEffect.Disadvantage,
                effectValue: 2,
                durationTurns: 3,
                llmInstruction: "test",
                clearMethod: "none",
                nat1Bonus: "none",
                displayName: "Test Trap",
                summary: "Test trap");
            
            state.Traps.Activate(trapDef);
            Assert.True(state.Traps.HasActive);

            var dice = new FixedDice(10, 10, 50); // d20, d20 (disadvantage/advantage), d100
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.False(state.Traps.HasActive);
            Assert.Equal("Test Trap", result.TrapClearedDisplayName);
        }

        [Fact]
        public void Execute_WithOtherStatSelected_ActiveTrapNotCleared()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Option 1") };
            
            var trapDef = new TrapDefinition(
                id: "test_trap",
                stat: StatType.Charm,
                effect: TrapEffect.Disadvantage,
                effectValue: 2,
                durationTurns: 3,
                llmInstruction: "test",
                clearMethod: "none",
                nat1Bonus: "none",
                displayName: "Test Trap",
                summary: "Test trap");
            
            state.Traps.Activate(trapDef);
            Assert.True(state.Traps.HasActive);

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.True(state.Traps.HasActive);
            Assert.Null(result.TrapClearedDisplayName);
        }

        [Fact]
        public void Execute_HonestySkipped_AppliesDenialGrowth()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] {
                new DialogueOption(StatType.Charm, "Charm Option"),
                new DialogueOption(StatType.Honesty, "Honesty Option")
            };
            state.PlayerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(allStats: 2, allShadow: 0));

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(1, state.PlayerShadows.GetEffectiveShadow(ShadowStatType.Denial));
        }

        [Fact]
        public void Execute_HonestyChosen_NoDenialGrowth()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] {
                new DialogueOption(StatType.Charm, "Charm Option"),
                new DialogueOption(StatType.Honesty, "Honesty Option")
            };
            state.PlayerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(allStats: 2, allShadow: 0));

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            stage.Execute(state, 1, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(0, state.PlayerShadows.GetEffectiveShadow(ShadowStatType.Denial));
        }

        [Fact]
        public void Execute_HonestySkipped_ManuallyNullStageState_NoCrash()
        {
            // This exercises the stage directly, bypassing GameSession's #1322 default tracker.
            var state = new GameSessionState();
            state.CurrentOptions = new[] {
                new DialogueOption(StatType.Charm, "Charm Option"),
                new DialogueOption(StatType.Honesty, "Honesty Option")
            };
            state.PlayerShadows = null;

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act & Assert (Should not throw)
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));
            Assert.NotNull(result.RollResult);
        }

        [Fact]
        public void Execute_WithCallbackBonus_CalculatesCallbackBonus()
        {
            // Arrange
            var state = new GameSessionState { TurnNumber = 4 };
            state.CurrentOptions = new[] {
                new DialogueOption(StatType.Charm, "Charm Option", callbackTurnNumber: 2)
            };

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.True(result.CallbackBonus > 0);
        }

        [Fact]
        public void Execute_WithActiveTell_AppliesTellBonus()
        {
            // Arrange
            var state = new GameSessionState { ActiveTell = new Tell(StatType.Charm, "Revealed charm vulnerability") };
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(4, result.TellBonus);
            Assert.Null(state.ActiveTell); // ActiveTell should be consumed/cleared after execution
        }

        [Fact]
        public void Execute_WithActiveTellMismatch_NoTellBonus()
        {
            // Arrange
            var state = new GameSessionState { ActiveTell = new Tell(StatType.Honesty, "Revealed honesty vulnerability") };
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(0, result.TellBonus);
            Assert.Null(state.ActiveTell); // ActiveTell is consumed regardless of match
        }

        [Fact]
        public void Execute_WithTripleBonus_AppliesAndConsumesTripleBonus()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };
            state.ComboTracker.RestoreFromSnapshot(Array.Empty<(string, bool)>(), pendingTripleBonus: true);
            Assert.True(state.ComboTracker.HasTripleBonus);

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(2, result.TripleBonusApplied);
            Assert.False(state.ComboTracker.HasTripleBonus); // TripleBonus should be consumed
        }

        [Fact]
        public void Execute_WithWeakness_AppliesDcReductionAndClearsWeakness()
        {
            // Arrange
            var defendingStatOfCharm = StatBlock.DefenceTable[StatType.Charm];
            var state = new GameSessionState { ActiveWeakness = new WeaknessWindow(defendingStatOfCharm, 3) };
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Null(state.ActiveWeakness); // Weakness should be consumed/cleared after execution
        }

        [Fact]
        public void Execute_WithGlobalDcBias_AdjustsDc()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };

            var dice = new FixedDice(10, 10, 50);
            var stage = BuildRollResolutionStage(dice, globalDcBias: 4);

            var player = BuildProfile("Player");
            var datee = BuildProfile("Datee");
            int baseDc = datee.Stats.GetDefenceDC(StatType.Charm);

            // Act
            var result = stage.Execute(state, 0, player, datee);

            // Assert
            // Effective DC should be: baseDc - globalDcBias => baseDc - 4
            Assert.Equal(baseDc - 4, result.RollResult.DC);
        }

        [Theory]
        [InlineData(1, true, false, FailureTier.Legendary, false)]
        [InlineData(20, false, true, FailureTier.Success, true)]
        public void Execute_WithGlobalDcBias_PreservesNatRollSemantics(
            int dieRoll,
            bool expectedNatOne,
            bool expectedNatTwenty,
            FailureTier expectedTier,
            bool expectedSuccess)
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };

            var dice = new FixedDice(dieRoll, 50);
            var stage = BuildRollResolutionStage(dice, globalDcBias: 99);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(dieRoll, result.RollResult.UsedDieRoll);
            Assert.Equal(expectedNatOne, result.RollResult.IsNatOne);
            Assert.Equal(expectedNatTwenty, result.RollResult.IsNatTwenty);
            Assert.Equal(expectedTier, result.RollResult.Tier);
            Assert.Equal(expectedSuccess, result.RollResult.IsSuccess);
        }

        [Fact]
        public void Execute_WithShadowDisadvantage_ForcesDisadvantage()
        {
            // Arrange
            var state = new GameSessionState { ShadowDisadvantagedStats = new HashSet<StatType> { StatType.Charm } };
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };
            state.CurrentHasDisadvantage = false;

            // In disadvantage roll twice, take lower.
            // Dice rolls: first = 15, second = 8.
            // Under disadvantage, we should resolve using 8.
            var dice = new FixedDice(15, 8, 50);
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.Equal(8, result.RollResult.UsedDieRoll);
        }

        [Fact]
        public void Execute_SucceedsDespiteOverthinkingDisadvantage_ReducesOverthinking()
        {
            // Arrange
            var state = new GameSessionState { ShadowDisadvantagedStats = new HashSet<StatType> { StatType.SelfAwareness } };
            state.CurrentOptions = new[] { new DialogueOption(StatType.SelfAwareness, "SA Option") };
            
            // SA maps to Overthinking shadow
            state.PlayerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(allStats: 2, allShadow: 6));
            Assert.Equal(6, state.PlayerShadows.GetEffectiveShadow(ShadowStatType.Overthinking));

            // Force high success roll so that success is guaranteed.
            var dice = new FixedDice(20, 20, 50); // Nat 20 success
            var stage = BuildRollResolutionStage(dice);

            // Act
            stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            // Overthinking is reduced by 1
            Assert.Equal(5, state.PlayerShadows.GetEffectiveShadow(ShadowStatType.Overthinking));
        }

        [Fact]
        public void Execute_SucceedsWithHighInterest_ReducesOverthinking()
        {
            // Arrange
            var state = new GameSessionState();
            state.Interest = new InterestMeter(18); // Will be >= 20 after success delta
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };
            state.PlayerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(allStats: 2, allShadow: 6));

            var dice = new FixedDice(20, 20, 50); // Nat 20 success gives huge delta
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.True(result.InterestAfter >= 20);
            Assert.Equal(5, state.PlayerShadows.GetEffectiveShadow(ShadowStatType.Overthinking));
        }

        [Fact]
        public void Execute_InterestReachesZero_GameOverAndDreadGrowth()
        {
            // Arrange
            var state = new GameSessionState();
            state.Interest = new InterestMeter(1); // will go to 0 on failure
            state.CurrentOptions = new[] { new DialogueOption(StatType.Charm, "Charm Option") };
            state.PlayerShadows = new SessionShadowTracker(TestHelpers.MakeStatBlock(allStats: 2, allShadow: 0));

            var dice = new FixedDice(1, 1, 50); // Nat 1 failure gives negative delta
            var stage = BuildRollResolutionStage(dice);

            // Act
            var result = stage.Execute(state, 0, BuildProfile("Player"), BuildProfile("Datee"));

            // Assert
            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.Unmatched, result.Outcome);
            Assert.Equal(1, state.PlayerShadows.GetEffectiveShadow(ShadowStatType.Dread));
        }
    }
}

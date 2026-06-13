using System;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    [Collection("GameSession")]
    [Trait("Category", "Core")]
    public class TurnDiceEvaluatorTests
    {
        private sealed class DummyDiceRoller : IDiceRoller
        {
            private readonly int _value;
            public DummyDiceRoller(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class DummyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        [Fact]
        public void EvaluateRolls_StandardRoll_ReturnsResultAndDice()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentDicePools = new PerOptionDicePool[1];
            
            var player = new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(10), // All stats = 10
                assembledSystemPrompt: "Player",
                displayName: "Player",
                timing: new TimingProfile(5, 0f, 0f, "neutral"),
                level: 1
            );
            
            var datee = new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(10),
                assembledSystemPrompt: "Datee",
                displayName: "Datee",
                timing: new TimingProfile(5, 0f, 0f, "neutral"),
                level: 1
            );

            var chosenOption = new DialogueOption(StatType.Charm, "Test Charm option");
            var dice = new DummyDiceRoller(12); // always roll 12
            var trapRegistry = new DummyTrapRegistry();

            // Act
            var (rollResult, resolveDice) = TurnDiceEvaluator.EvaluateRolls(
                state: state,
                optionIndex: 0,
                chosenOption: chosenOption,
                player: player,
                datee: datee,
                dice: dice,
                trapRegistry: trapRegistry,
                consequenceCatalog: null,
                externalBonus: 0,
                dcAdjustment: 0,
                resolveHasDisadvantage: false
            );

            // Assert
            Assert.NotNull(rollResult);
            Assert.NotNull(resolveDice);
            Assert.Equal(StatType.Charm, rollResult.Stat);
            
            // Check that the pool was stored in the state
            Assert.NotNull(state.CurrentDicePools[0]);
            Assert.Equal(12, state.CurrentDicePools[0].ToArray()[0]); // First d20 roll should be 12
        }

        [Fact]
        public void EvaluateRolls_InjectedPool_UsesInjectedPool()
        {
            // Arrange
            var state = new GameSessionState();
            state.CurrentDicePools = new PerOptionDicePool[1];
            
            // Inject a specific pool (e.g. roll a natural 20, d100 = 50)
            var injectedPool = new PerOptionDicePool(0, new[] { 20, 50 });
            state.InjectedNextPool = injectedPool;

            var player = new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(10),
                assembledSystemPrompt: "Player",
                displayName: "Player",
                timing: new TimingProfile(5, 0f, 0f, "neutral"),
                level: 1
            );
            
            var datee = new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(10),
                assembledSystemPrompt: "Datee",
                displayName: "Datee",
                timing: new TimingProfile(5, 0f, 0f, "neutral"),
                level: 1
            );

            var chosenOption = new DialogueOption(StatType.Rizz, "Test Rizz option");
            var dice = new DummyDiceRoller(1); // dummy, shouldn't be called if pool is injected
            var trapRegistry = new DummyTrapRegistry();

            // Act
            var (rollResult, resolveDice) = TurnDiceEvaluator.EvaluateRolls(
                state: state,
                optionIndex: 0,
                chosenOption: chosenOption,
                player: player,
                datee: datee,
                dice: dice,
                trapRegistry: trapRegistry,
                consequenceCatalog: null,
                externalBonus: 0,
                dcAdjustment: 0,
                resolveHasDisadvantage: false
            );

            // Assert
            Assert.NotNull(rollResult);
            Assert.True(rollResult.IsNatTwenty);
            Assert.Null(state.InjectedNextPool); // Single-use injected pool should be consumed (set to null)
            Assert.NotNull(state.CurrentDicePools[0]);
            Assert.Equal(20, state.CurrentDicePools[0].ToArray()[0]);
        }
    }
}

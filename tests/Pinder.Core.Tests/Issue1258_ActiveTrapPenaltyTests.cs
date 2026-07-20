using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1258_ActiveTrapPenaltyTests
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

        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, allStats },
                    { StatType.Rizz, allStats },
                    { StatType.Honesty, allStats },
                    { StatType.Chaos, allStats },
                    { StatType.Wit, allStats },
                    { StatType.SelfAwareness, allStats }
                },
                new Dictionary<ShadowStatType, int>());
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        // ===== GameDefinition Parsing Tests =====

        [Fact]
        public void LoadFrom_WithActiveTrapPenalty_ParsesPercentage()
        {
            const string yaml = @"
name: TestGame
game_master_prompt: gm
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 0
  afternoon: 0
  evening: 0
  overnight: 0
active_trap_interest_penalty: -25%
" + GameDefinitionYamlTestFixtures.RequiredParserBlocksWithoutActiveTrapPenalty;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(-0.25, gd.ActiveTrapInterestPenalty);
        }

        [Fact]
        public void LoadFrom_WithActiveTrapPenalty_ParsesDecimal()
        {
            const string yaml = @"
name: TestGame
game_master_prompt: gm
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 0
  afternoon: 0
  evening: 0
  overnight: 0
active_trap_interest_penalty: -0.25
" + GameDefinitionYamlTestFixtures.RequiredParserBlocksWithoutActiveTrapPenalty;
            var gd = GameDefinition.LoadFrom(yaml);
            Assert.Equal(-0.25, gd.ActiveTrapInterestPenalty);
        }

        [Fact]
        public void LoadFrom_WithoutActiveTrapPenalty_ThrowsInvalidOperationException()
        {
            const string yaml = @"
name: TestGame
game_master_prompt: gm
player_avatar_role_description: p
datee_role_description: o
global_dc_bias: 0
horniness_time_modifiers:
  morning: 0
  afternoon: 0
  evening: 0
  overnight: 0
";
            var ex = Assert.Throws<InvalidOperationException>(() =>
                GameDefinition.LoadFrom(yaml + GameDefinitionYamlTestFixtures.RequiredParserBlocksWithoutActiveTrapPenalty));
            Assert.Contains("active_trap_interest_penalty", ex.Message);
        }

        // ===== Engine Integration Tests (RollResolutionStage) =====
        
        [Fact]
        public void Execute_WithActiveTrap_AppliesPenaltyToPositiveInterestDelta()
        {
            // Arrange
            var dice = new FixedDice(20); // Nat 20 for positive delta
            var trapRegistry = new EmptyTrapRegistry();
            var xpRecorder = new SessionXpRecorder(new XpLedger(), null);

            var stage = new RollResolutionStage(
                dice,
                trapRegistry,
                null,
                null,
                xpRecorder,
                globalDcBias: 0,
                activeTrapInterestPenalty: -0.25,
                onRuleResolution: null);

            var player = MakeProfile("Player", 10);
            var datee = MakeProfile("Datee", 10);

            var state = new GameSessionState();
            state.CurrentOptions = new DialogueOption[]
            {
                new DialogueOption(StatType.Charm, "Hello")
            };
            
            var trapDef = new TrapDefinition(
                id: "test_trap",
                stat: StatType.Honesty,
                effect: TrapEffect.Disadvantage,
                effectValue: 2,
                durationTurns: 3,
                llmInstruction: "test",
                clearMethod: "none",
                nat1Bonus: "none",
                displayName: "Test Trap",
                summary: "Test trap");
                
            state.Traps.Activate(trapDef);
            
            // Baseline
            var standardStage = new RollResolutionStage(
                new FixedDice(20),
                trapRegistry,
                null,
                null,
                new SessionXpRecorder(new XpLedger(), null),
                globalDcBias: 0,
                activeTrapInterestPenalty: 0.0,
                onRuleResolution: null);
                
            var standardState = new GameSessionState();
            standardState.CurrentOptions = new DialogueOption[]
            {
                new DialogueOption(StatType.Charm, "Hello")
            };
            standardState.Traps.Activate(trapDef); 
            
            var standardResult = standardStage.Execute(standardState, 0, player, datee);
            
            // Act
            var penalizedResult = stage.Execute(state, 0, player, datee);

            // Assert
            Assert.True(standardResult.InterestDelta > 0, "Expected a positive base interest delta from a Nat 20.");
            int expectedPenalizedDelta = (int)Math.Round(standardResult.InterestDelta * 0.75, MidpointRounding.AwayFromZero);
            Assert.Equal(expectedPenalizedDelta, penalizedResult.InterestDelta);
        }

        [Fact]
        public void Execute_WithoutActiveTrap_DoesNotApplyPenalty()
        {
            // Arrange
            var dice = new FixedDice(20); 
            var trapRegistry = new EmptyTrapRegistry();
            var xpRecorder = new SessionXpRecorder(new XpLedger(), null);

            var stage = new RollResolutionStage(
                dice,
                trapRegistry,
                null,
                null,
                xpRecorder,
                globalDcBias: 0,
                activeTrapInterestPenalty: -0.25,
                onRuleResolution: null);

            var player = MakeProfile("Player", 10);
            var datee = MakeProfile("Datee", 10);

            var state = new GameSessionState();
            state.CurrentOptions = new DialogueOption[]
            {
                new DialogueOption(StatType.Charm, "Hello")
            };
            
            var standardStage = new RollResolutionStage(
                new FixedDice(20),
                trapRegistry,
                null,
                null,
                new SessionXpRecorder(new XpLedger(), null),
                globalDcBias: 0,
                activeTrapInterestPenalty: 0.0,
                onRuleResolution: null);
            var standardState = new GameSessionState();
            standardState.CurrentOptions = new DialogueOption[]
            {
                new DialogueOption(StatType.Charm, "Hello")
            };
            var standardResult = standardStage.Execute(standardState, 0, player, datee);

            // Act
            var result = stage.Execute(state, 0, player, datee);

            // Assert
            Assert.False(state.Traps.HasActive);
            Assert.Equal(standardResult.InterestDelta, result.InterestDelta);
        }
    }
}

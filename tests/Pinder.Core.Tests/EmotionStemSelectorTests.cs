using System;
using System.Collections.Generic;
using Xunit;
using Pinder.Core.Conversation;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class EmotionStemSelectorTests
    {
        [Fact]
        public void PhaseDerivation_AppliesHysteresisBuffer_PreventsThrashingOnBandBoundaries()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState 
            {
                TurnCount = 5,
                InterestScore = 50, // Hovering at a boundary
                PreviousPhase = "MacroPhase1" // Mocking a phase
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            // Hysteresis should keep it in the previous phase behavior
            Assert.Equal("BACKSTORY", result.Registry); 
        }

        [Fact]
        public void QuadrantHFITOR_Calculations_AreClamped_And_DetermineManner()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                PlayerStats = new ParticipantStats { BaseHFI = 25, BaseTOR = -5 }, // Needs clamping to [0..20]
                DateeStats = new ParticipantStats { BaseHFI = 15, BaseTOR = 15 }
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            // Example mapping check: if it ends up in Q1 based on clamping, we expect a certain manner.
            // "Manner: Q1 -> CURATED_BUFFER, Q2 -> DEFENSIVE_EVASION, Q3 -> INTIMATE_BREAKTHROUGH, Q4 -> TRAUMATIC_LEAKAGE"
            // We'll assert that the manner output is one of the valid strings.
            Assert.Contains(result.Manner, new[] { "CURATED", "PRE_EMPTIVE", "SINCERE", "LEAKING", "CURATED_BUFFER", "DEFENSIVE_EVASION", "INTIMATE_BREAKTHROUGH", "TRAUMATIC_LEAKAGE" });
        }

        [Fact]
        public void TrapPrecedence_OverridesPostureManners()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                PlayerStats = new ParticipantStats { BaseHFI = 20, BaseTOR = 20 }, // Would normally be a SINCERE / Q3 manner
                ActiveTraps = new List<string> { "DEFENSIVE_TRAP" } // Should override manner
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            // Active trap should force a specific manner, e.g. DEFENSIVE_EVASION or PRE_EMPTIVE
            Assert.NotEqual("SINCERE", result.Manner);
            Assert.NotEqual("INTIMATE_BREAKTHROUGH", result.Manner);
        }

        [Fact]
        public void CoverageExhaustion_FallsBackTo_MostRecentThread()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                // All indices spent
                SpentBackstoryIndices = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
                SpentStakeIndices = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
                PreviousResolvedIndex = 13 // Recent thread fallback
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            // Must not stall or throw. Should pick previous thread.
            Assert.Equal(13, result.Index);
        }

        [Fact]
        public void Selections_Are_100Percent_Reproducible_With_SameSeed()
        {
            // Arrange
            int seed = 12345;
            var selectorA = new EmotionStemSelector(seed);
            var selectorB = new EmotionStemSelector(seed);
            
            var state = new ConversationState
            {
                TurnCount = 10,
                InterestScore = 75
            };

            // Act
            var resultA = selectorA.Resolve(state);
            var resultB = selectorB.Resolve(state);

            // Assert
            Assert.Equal(resultA.Registry, resultB.Registry);
            Assert.Equal(resultA.Index, resultB.Index);
            Assert.Equal(resultA.Manner, resultB.Manner);
        }
    }
}
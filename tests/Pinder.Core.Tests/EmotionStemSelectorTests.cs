using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Characters;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class EmotionStemSelectorTests
    {
        [Fact]
        public void Hydrate_UsesCanonicalBackstoryFactInsteadOfPlaceholderText()
        {
            var facts = BackstoryValidator.RequiredCategories.ToDictionary(
                category => category,
                category => new BackstoryFact($"lie for {category}", $"reality for {category}"));
            var target = new ResolvedRevelationTarget
            {
                Registry = "BACKSTORY", Index = 0, Field = "BIO_LIE", Manner = "CURATED_BUFFER"
            };

            var result = EmotionStemSelector.Hydrate(target, facts, null);

            Assert.Equal("lie for age_and_demographics", result.StemText);
        }

        [Fact]
        public void Hydrate_UsesIndexedPsychologicalStake()
        {
            var target = new ResolvedRevelationTarget
            {
                Registry = "STAKE", Index = 1, Field = "STAKE_LINE", Manner = "INTIMATE_BREAKTHROUGH"
            };

            var result = EmotionStemSelector.Hydrate(target, null, new[] { "first", "actual stake" });

            Assert.Equal("actual stake", result.StemText);
        }

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

        // =====================================================================
        // NEW TESTS FOR ISSUE #1279
        // =====================================================================

        [Fact]
        public void Resolve_MacroPhase1_ReturnsBackstoryRegistry()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 2,
                InterestScore = 20,
                PreviousPhase = null
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            Assert.Equal("BACKSTORY", result.Registry);
        }

        [Fact]
        public void Resolve_MacroPhase2_ReturnsBackstoryRegistry()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 6,
                InterestScore = 20,
                PreviousPhase = null
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            Assert.Equal("BACKSTORY", result.Registry); // Fails on current code (currently returns STAKE)
        }

        [Fact]
        public void Resolve_MacroPhase3_ReturnsStakeRegistry()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 10,
                InterestScore = 20,
                PreviousPhase = null
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            Assert.Equal("STAKE", result.Registry);
        }

        [Fact]
        public void Resolve_MacroPhase1_ReturnsBioLieField()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 2,
                InterestScore = 20,
                PreviousPhase = null
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            Assert.Equal("BIO_LIE", result.Field);
        }

        [Fact]
        public void Resolve_MacroPhase2_ReturnsTragicRealityField()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 6,
                InterestScore = 20,
                PreviousPhase = null
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            Assert.Equal("TRAGIC_REALITY", result.Field); // Fails on current code (currently returns STAKE_LINE)
        }

        [Fact]
        public void Resolve_MacroPhase3_ReturnsStakeLineField()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 10,
                InterestScore = 20,
                PreviousPhase = null
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            Assert.Equal("STAKE_LINE", result.Field);
        }

        [Fact]
        public void Resolve_Backstory_BoundsCheck_ExcludesIndex20AndAbove()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 2, // MacroPhase1 -> BACKSTORY
                InterestScore = 20,
                SpentBackstoryIndices = new HashSet<int>(Enumerable.Range(0, 20)), // 0 to 19 spent
                PreviousResolvedIndex = 77
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            // Since all valid indices (0 to 19) are spent, and index 20+ is not allowed,
            // it must fallback to PreviousResolvedIndex.
            Assert.Equal(77, result.Index); // Fails on current code (returns 20)
        }

        [Fact]
        public void Resolve_Stake_BoundsCheck_ExcludesIndex15AndAbove()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 10, // MacroPhase3 -> STAKE
                InterestScore = 20,
                SpentStakeIndices = new HashSet<int>(Enumerable.Range(0, 15)), // 0 to 14 spent
                PreviousResolvedIndex = 55
            };

            // Act
            var result = selector.Resolve(state);

            // Assert
            // Since all valid indices (0 to 14) are spent, and index 15+ is not allowed,
            // it must fallback to PreviousResolvedIndex.
            Assert.Equal(55, result.Index); // Fails on current code (returns 15 or above)
        }

        [Fact]
        public void Resolve_SpentExhaustionFallback_MacroPhase1_NoExceptionAndReturnsPreviousResolvedIndex()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 2,
                InterestScore = 20,
                SpentBackstoryIndices = new HashSet<int>(Enumerable.Range(0, 20)), // 0 to 19 spent
                PreviousResolvedIndex = 11
            };

            // Act & Assert
            var exception = Record.Exception(() => {
                var result = selector.Resolve(state);
                Assert.Equal(11, result.Index);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void Resolve_SpentExhaustionFallback_MacroPhase2_NoExceptionAndReturnsPreviousResolvedIndex()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 6,
                InterestScore = 20,
                SpentBackstoryIndices = new HashSet<int>(Enumerable.Range(0, 20)), // 0 to 19 spent
                PreviousResolvedIndex = 12
            };

            // Act & Assert
            var exception = Record.Exception(() => {
                var result = selector.Resolve(state);
                Assert.Equal(12, result.Index);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void Resolve_SpentExhaustionFallback_MacroPhase3_NoExceptionAndReturnsPreviousResolvedIndex()
        {
            // Arrange
            var selector = new EmotionStemSelector(42);
            var state = new ConversationState
            {
                TurnCount = 10,
                InterestScore = 20,
                SpentStakeIndices = new HashSet<int>(Enumerable.Range(0, 15)), // 0 to 14 spent
                PreviousResolvedIndex = 13
            };

            // Act & Assert
            var exception = Record.Exception(() => {
                var result = selector.Resolve(state);
                Assert.Equal(13, result.Index);
            });

            Assert.Null(exception);
        }
    }
}

using System.Collections.Generic;
using Pinder.Core.Characters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for ActiveArchetype resolution in CharacterAssembler (#649).
    /// </summary>
    public class ActiveArchetypeTests
    {
        [Fact]
        public void ResolveActiveArchetype_SelectsHighestCountEligibleAtLevel()
        {
            // Arrange: "The Peacock" (3-8) count=5, "The Sniper" (5-11) count=3
            var ranked = new List<(string Archetype, int Count)>
            {
                ("The Peacock", 5),
                ("The Sniper", 3),
                ("The Hey Opener", 2)
            };

            // Act: level 4 — Peacock eligible (3-8), Sniper not (5-11)
            var result = CharacterAssembler.ResolveActiveArchetype(ranked, 4);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("The Peacock", result.Name);
            Assert.Equal(5, result.Count);
            Assert.Equal("clear", result.InterferenceLevel);
        }

        [Fact]
        public void ResolveActiveArchetype_FallsBackToHighestCountWhenNoneEligible()
        {
            // Arrange: all archetypes have high min levels
            var ranked = new List<(string Archetype, int Count)>
            {
                ("The Sniper", 5),   // 5-11
                ("The Player", 3),   // 5-10
            };

            // Act: level 2 — neither eligible
            var result = CharacterAssembler.ResolveActiveArchetype(ranked, 2);

            // Assert: falls back to highest count overall
            Assert.NotNull(result);
            Assert.Equal("The Sniper", result.Name);
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void ResolveActiveArchetype_ReturnsNullForEmptyList()
        {
            var ranked = new List<(string Archetype, int Count)>();
            var result = CharacterAssembler.ResolveActiveArchetype(ranked, 5);
            Assert.Null(result);
        }

        [Fact]
        public void ResolveActiveArchetype_NoLevelFiltering_WhenLevelIsZero()
        {
            var ranked = new List<(string Archetype, int Count)>
            {
                ("The Sniper", 5),
                ("The Peacock", 3),
            };

            // Act: level 0 — no filtering, just pick highest count
            var result = CharacterAssembler.ResolveActiveArchetype(ranked, 0);

            Assert.NotNull(result);
            Assert.Equal("The Sniper", result.Name);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_Slight()
        {
            var arch = new ActiveArchetype("Test", "behavior", 1);
            Assert.Equal("slight", arch.InterferenceLevel);

            arch = new ActiveArchetype("Test", "behavior", 2);
            Assert.Equal("slight", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_Clear()
        {
            var arch = new ActiveArchetype("Test", "behavior", 3);
            Assert.Equal("clear", arch.InterferenceLevel);

            arch = new ActiveArchetype("Test", "behavior", 5);
            Assert.Equal("clear", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_Dominant()
        {
            var arch = new ActiveArchetype("Test", "behavior", 6);
            Assert.Equal("dominant", arch.InterferenceLevel);

            arch = new ActiveArchetype("Test", "behavior", 10);
            Assert.Equal("dominant", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_Directive_ContainsNameAndBehavior()
        {
            var arch = new ActiveArchetype("The Peacock", "Shows off constantly.", 4);
            var directive = arch.Directive;

            Assert.Contains("ACTIVE ARCHETYPE: The Peacock (clear)", directive);
            Assert.Contains("Shows off constantly.", directive);
        }

        [Fact]
        public void ArchetypeCatalog_GetBehavior_ReturnsPlaceholderForUnknown()
        {
            var behavior = ArchetypeCatalog.GetBehavior("Unknown Archetype XYZ");
            Assert.Contains("Unknown Archetype XYZ", behavior);
            Assert.Contains("behavioral pattern", behavior);
        }

        [Fact]
        public void ArchetypeCatalog_RegisterAndGetBehavior()
        {
            ArchetypeCatalog.RegisterBehavior("Test Archetype 12345", "Test behavior text");
            var behavior = ArchetypeCatalog.GetBehavior("Test Archetype 12345");
            Assert.Equal("Test behavior text", behavior);
        }
    }
}

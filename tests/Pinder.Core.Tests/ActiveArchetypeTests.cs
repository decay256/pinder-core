using System.Collections.Generic;
using Pinder.Core.Characters;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for ActiveArchetype resolution in CharacterAssembler (#649).
    /// </summary>
    [Trait("Category", "Core")]
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

            // Assert: 5 / (5+3+2) = 0.50 → "clear" (#375 ratio rule).
            Assert.NotNull(result);
            Assert.Equal("The Peacock", result.Name);
            Assert.Equal(5, result.Count);
            Assert.Equal(10, result.TotalCount);
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

        // ── #375 ratio-based intensity tests ─────────────────────────────────────
        // Intensity is now share-of-archetype-votes, not raw count, so a
        // single-vote lead no longer gets "clear" or "dominant". The legacy
        // single-arg ctor defaults TotalCount to Count, which gives ratio=1.0
        // and therefore "dominant" — preserved for backward compatibility for
        // callers that have not yet been migrated to pass totalCount.

        [Fact]
        public void ActiveArchetype_InterferenceLevel_LegacyCtor_DefaultsToDominant()
        {
            // No total supplied → ratio = Count/Count = 1.0 → dominant.
            // This preserves "always dominant" for callers that haven't
            // migrated to pass totalCount, and keeps the public API
            // source-compatible.
            var arch = new ActiveArchetype("Test", "behavior", 1);
            Assert.Equal("dominant", arch.InterferenceLevel);
            Assert.Equal(1, arch.TotalCount);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_PunTroll2_Player1_IsClear()
        {
            // The #375 acceptance test: a 2-over-1 lead must NOT be "dominant".
            // 2/3 = 0.67 → < 0.7 → "clear".
            var arch = new ActiveArchetype("The Pun Troll", "behavior", count: 2, totalCount: 3);
            Assert.Equal("clear", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_PunTroll4_Player1_IsDominant()
        {
            // The #375 acceptance test: a 4-over-1 lead IS "dominant".
            // 4/5 = 0.80 → ≥ 0.7 → "dominant".
            var arch = new ActiveArchetype("The Pun Troll", "behavior", count: 4, totalCount: 5);
            Assert.Equal("dominant", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_TiedBuild_IsClear()
        {
            // [Pun Troll: 2, Player: 2] → 2/4 = 0.50 → "clear" (tied lead).
            var arch = new ActiveArchetype("The Pun Troll", "behavior", count: 2, totalCount: 4);
            Assert.Equal("clear", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_OneVoiceAmongMany_IsSlight()
        {
            // 1/4 = 0.25 → < 0.4 → "slight".
            var arch = new ActiveArchetype("The Pun Troll", "behavior", count: 1, totalCount: 4);
            Assert.Equal("slight", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_InterferenceLevel_PureBuild_IsDominant()
        {
            // 4/4 = 1.0 → "dominant" (a build that votes one archetype only).
            var arch = new ActiveArchetype("The Peacock", "behavior", count: 4, totalCount: 4);
            Assert.Equal("dominant", arch.InterferenceLevel);
        }

        [Fact]
        public void ActiveArchetype_Directive_ContainsNameAndBehavior()
        {
            // Build of 4 votes total, this archetype gets 4 → dominant.
            var arch = new ActiveArchetype("The Peacock", "Shows off constantly.", count: 4, totalCount: 4);
            var directive = arch.Directive;

            Assert.Contains("ACTIVE ARCHETYPE: The Peacock (dominant)", directive);
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

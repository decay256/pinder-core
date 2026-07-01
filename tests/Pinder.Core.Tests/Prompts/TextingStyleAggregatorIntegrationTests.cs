using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Prompts;
using Pinder.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests.Prompts
{
    /// <summary>
    /// Regression tests for pinder-core#907: conflict-matrix wiring into
    /// production aggregation paths.
    ///
    /// Covers the specific bug the ticket was filed to fix: the Zyx session
    /// where "trousers" equipped with a wall-of-text item and "frame"
    /// equipped with a never-sends-more-than-5-words item co-existed in the
    /// aggregated style profile despite being explicitly listed as mutually
    /// exclusive in the conflict matrix.
    ///
    /// Before the fix: both callsites (<see cref="CharacterDefinitionLoader"/>
    /// and <see cref="PromptBuilder"/>) passed <see cref="TextingStyleConflicts.Empty"/>
    /// to the 2-arg overloads, so the matrix was never consulted at runtime.
    /// After the fix: both 2-arg overloads use
    /// <see cref="TextingStyleAggregator.ConflictCatalog"/> (loaded at startup
    /// by <see cref="PromptWiring.Wire"/>), and
    /// <see cref="CharacterDefinitionLoader"/> switches to
    /// <see cref="TextingStyleAggregator.AggregateWithAudit"/> and logs drops.
    /// </summary>
    [Trait("Category", "Prompts")]
    [Collection("StaticWiring")]
    public class TextingStyleAggregatorIntegrationTests
    {
        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        private static TextingStyleConflicts LoadRealConflictCatalog()
        {
            var yaml = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "persona", "texting-style-conflicts.yaml"));
            return TextingStyleConflicts.LoadFrom(yaml);
        }

        private static IItemRepository LoadItemRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "items", "starter-items.json"));
            return new JsonItemRepository(json);
        }

        private static IAnatomyRepository LoadAnatomyRepo()
        {
            string json = File.ReadAllText(
                Path.Combine(RepoRoot, "data", "anatomy", "anatomy-parameters.json"));
            return new JsonAnatomyRepository(json);
        }

        // Minimal sources that reproduce the Zyx conflict:
        //   trousers → structure: wall-of-text  (picked first)
        //   frame    → length: never sends more than 5 words  (conflicts → should drop)
        private static IReadOnlyList<TextingStyleFragmentSource> ZyxConflictSources()
            => new[]
            {
                new TextingStyleFragmentSource(
                    kind:            "item",
                    source:          "harem-pants",
                    fragment:
                        "SYNTAX:\n" +
                        "- emoji: replaces \".\" with a soft emoji consistently\n" +
                        "- shorthand: \"iykyk\" appended to anything\n" +
                        "- grammar: consistent recurring misspellings\n" +
                        "- structure: wall-of-text (one paragraph, no breaks, comma splices throughout)\n" +
                        "- length: minimum 80 words per message, no exceptions\n" +
                        "- tics: types \"?\" alone as a full message\n" +
                        "TONE:\n" +
                        "- stance (pivot-heavy): never stays on one topic\n" +
                        "- pacing (measured): deliberate, paced, never rushed",
                    slotOrParameter: "trousers"),
                new TextingStyleFragmentSource(
                    kind:            "item",
                    source:          "third-eye-piercing",
                    fragment:
                        "SYNTAX:\n" +
                        "- emoji: pairs two emojis in a fixed combo\n" +
                        "- shorthand: \"ngl\" at the start of sentences\n" +
                        "- grammar: writes numbers as words\n" +
                        "- structure: caption-voice (third person about themselves)\n" +
                        "- length: never sends more than 5 words\n" +
                        "- tics: always ends with a question\n" +
                        "TONE:\n" +
                        "- stance (deflective): turns every question into a question\n" +
                        "- pacing (double-text): sends two messages back-to-back",
                    slotOrParameter: "frame"),
            };

        // ------------------------------------------------------------------
        // Test 1: Demonstrate the OLD bug (Empty catalog = no resolution).
        // This test is intentionally named to document the pre-fix behaviour;
        // it does NOT assert the conflict fires — it asserts both values appear,
        // confirming the bug was real when Empty was used.
        // ------------------------------------------------------------------

        [Fact]
        public void WithEmptyCatalog_BothConflictingValues_AppearInResult()
        {
            // Arrange: use Empty exactly as the old 2-arg callsite did.
            var sources = ZyxConflictSources();

            // Act
            var result = TextingStyleAggregator.AggregateWithAudit(
                sources, "zyx-test", TextingStyleConflicts.Empty);

            var joined = string.Join("|", result.Lines);

            // Assert: with Empty, no conflict fires → both values present.
            Assert.Contains("wall-of-text", joined,           StringComparison.Ordinal);
            Assert.Contains("never sends more than 5 words",  joined, StringComparison.Ordinal);
            Assert.Empty(result.Drops); // Empty = no drops
        }

        // ------------------------------------------------------------------
        // Test 2: With the loaded catalog, the conflict fires.
        // This is the positive-path test: loaded catalog → wall-of-text
        // kept, length "never sends more than 5 words" dropped.
        // ------------------------------------------------------------------

        [Fact]
        public void WithLoadedCatalog_ConflictingLengthValue_IsDropped()
        {
            // Arrange
            var conflicts = LoadRealConflictCatalog();
            var sources   = ZyxConflictSources();

            // Act
            var result = TextingStyleAggregator.AggregateWithAudit(
                sources, "zyx-test", conflicts);

            var joined = string.Join("|", result.Lines);

            // Assert: conflict fired, wall-of-text kept, 5-word cap dropped.
            Assert.Contains("wall-of-text", joined,              StringComparison.Ordinal);
            Assert.DoesNotContain("never sends more than 5 words", joined, StringComparison.Ordinal);
            Assert.Single(result.Drops);
            Assert.Equal("length",       result.Drops[0].Axis);
            Assert.Contains("never sends more than 5 words",
                result.Drops[0].DroppedValue, StringComparison.Ordinal);
        }

        // Test 3: Production path — CharacterDefinitionLoader.Load for Zyx.
        //
        // Restored #907 end-to-end production guard: conflicting structure axis
        // kept, lower-priority 5-word length cap dropped on a real Zyx load with
        // real Unity items hair1/arms0.
        // ------------------------------------------------------------------

        [Fact]
        public void ProductionPath_ZyxLoad_ConflictResolved_NeverFiveWordsDropped()
        {
            // Arrange: ConflictCatalog is loaded by CoreTestWiring.Initialize()
            Assert.NotNull(TextingStyleAggregator.ConflictCatalog);
            Assert.True(
                TextingStyleAggregator.ConflictCatalog!.Entries.Count > 0,
                "ConflictCatalog should have been loaded by CoreTestWiring");

            var itemRepo    = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string zyxPath  = Path.Combine(RepoRoot, "data", "characters", "zyx.json");

            // Act — must not throw
            var profile = CharacterDefinitionLoader.Load(zyxPath, itemRepo, anatomyRepo);

            // Assert: profile assembles correctly; Zyx's real Unity items are known
            Assert.NotNull(profile);
            Assert.False(string.IsNullOrWhiteSpace(profile.AssembledSystemPrompt));
            Assert.NotNull(profile.TextingStyleFragment);

            Assert.Contains("wall-of-text", profile.TextingStyleFragment, StringComparison.Ordinal);
            Assert.DoesNotContain("never sends more than 5 words", profile.TextingStyleFragment, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------
        // Test 4: ConflictCatalog static property is honoured by 2-arg overloads.
        // Demonstrates that the production callsite (2-arg) picks up whatever
        // is stored in ConflictCatalog, so PromptWiring.Wire() controls runtime
        // behaviour without requiring callsite changes.
        // ------------------------------------------------------------------

        [Fact]
        public void TwoArgOverload_UsesConflictCatalog_WhenSet()
        {
            // Save and restore the static to avoid test cross-contamination.
            var saved = TextingStyleAggregator.ConflictCatalog;
            try
            {
                // With an empty catalog, both values appear.
                TextingStyleAggregator.ConflictCatalog = TextingStyleConflicts.Empty;
                var sources = ZyxConflictSources();
                string emptyResult = TextingStyleAggregator.Aggregate(sources, "seed");
                Assert.Contains("never sends more than 5 words",
                    emptyResult, StringComparison.Ordinal);

                // With the real catalog, the conflict fires.
                TextingStyleAggregator.ConflictCatalog = LoadRealConflictCatalog();
                string catalogResult = TextingStyleAggregator.Aggregate(sources, "seed");
                Assert.DoesNotContain("never sends more than 5 words",
                    catalogResult, StringComparison.Ordinal);
                Assert.Contains("wall-of-text", catalogResult, StringComparison.Ordinal);
            }
            finally
            {
                TextingStyleAggregator.ConflictCatalog = saved;
            }
        }

        #region AC_Deterministic_TextingStyle_Tests

        [Fact]
        public void RepeatedAggregation_IsByteForByteIdenticalAndStable()
        {
            var conflicts = LoadRealConflictCatalog();
            var sources = ZyxConflictSources();

            var firstResult = TextingStyleAggregator.AggregateWithAudit(sources, "zyx-test", conflicts);
            Assert.NotNull(firstResult);

            for (int i = 0; i < 4; i++)
            {
                var currentResult = TextingStyleAggregator.AggregateWithAudit(sources, "zyx-test", conflicts);
                Assert.NotNull(currentResult);

                // Assert result.Lines sequence is byte-for-byte identical
                Assert.Equal(firstResult.Lines.Count, currentResult.Lines.Count);
                Assert.True(firstResult.Lines.SequenceEqual(currentResult.Lines, StringComparer.Ordinal));

                // Assert result.Drops count/content identical across runs
                Assert.Equal(firstResult.Drops.Count, currentResult.Drops.Count);
                for (int j = 0; j < firstResult.Drops.Count; j++)
                {
                    var expectedDrop = firstResult.Drops[j];
                    var actualDrop = currentResult.Drops[j];
                    Assert.Equal(expectedDrop.Axis, actualDrop.Axis);
                    Assert.Equal(expectedDrop.DroppedValue, actualDrop.DroppedValue);
                }
            }
        }

        [Fact]
        public void SeedIndependence_DoesNotAffectAggregationResult()
        {
            var conflicts = LoadRealConflictCatalog();
            var sources = ZyxConflictSources();

            var seeds = new List<string?>
            {
                null,
                "",
                "some-fixed-seed",
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString()
            };

            var firstResult = TextingStyleAggregator.AggregateWithAudit(sources, null, conflicts);
            var firstAggregate = TextingStyleAggregator.Aggregate(sources, null, conflicts);

            foreach (var seed in seeds)
            {
                var result = TextingStyleAggregator.AggregateWithAudit(sources, seed, conflicts);
                var aggregate = TextingStyleAggregator.Aggregate(sources, seed, conflicts);

                // Assert result.Lines sequence is identical
                Assert.True(firstResult.Lines.SequenceEqual(result.Lines, StringComparer.Ordinal));

                // Assert aggregated string is identical
                Assert.Equal(firstAggregate, aggregate);
            }
        }

        [Fact]
        public void ProductionPath_RepeatedLoad_IsIdenticalAndStable()
        {
            var itemRepo = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string zyxPath = Path.Combine(RepoRoot, "data", "characters", "zyx.json");

            string? firstFragment = null;
            IReadOnlyList<string>? firstLines = null;

            for (int i = 0; i < 3; i++)
            {
                var profile = CharacterDefinitionLoader.Load(zyxPath, itemRepo, anatomyRepo);
                Assert.NotNull(profile);

                if (firstFragment == null)
                {
                    firstFragment = profile.TextingStyleFragment;
                    firstLines = profile.TextingStyleLines;
                    Assert.NotNull(firstFragment);
                    Assert.NotNull(firstLines);
                }
                else
                {
                    Assert.Equal(firstFragment, profile.TextingStyleFragment);
                    Assert.True(firstLines.SequenceEqual(profile.TextingStyleLines, StringComparer.Ordinal));
                }
            }
        }

        [Fact]
        public void ListVsStringConsistency_AcrossRepeats_IsIdentical()
        {
            var conflicts = LoadRealConflictCatalog();
            var sources = ZyxConflictSources();
            string seedKey = "zyx-test";

            IReadOnlyList<string>? firstList = null;

            for (int i = 0; i < 5; i++)
            {
                var currentList = TextingStyleAggregator.AggregateAsList(sources, seedKey, conflicts);
                Assert.NotNull(currentList);

                if (firstList == null)
                {
                    firstList = currentList;
                }
                else
                {
                    Assert.True(firstList.SequenceEqual(currentList, StringComparer.Ordinal));
                }
            }
        }

        #endregion
    }
}

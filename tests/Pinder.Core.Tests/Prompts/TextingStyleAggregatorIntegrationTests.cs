using System;
using System.Collections.Generic;
using System.IO;
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

        // ------------------------------------------------------------------
        // Test 3: Production path — CharacterDefinitionLoader.Load for Zyx.
        //
        // This is the key regression test the reviewer requested:
        //   "exercises the production path (CharacterDefinitionLoader.LoadCharacter
        //    → aggregation produces a profile that should have conflicts dropped)
        //    and asserts the conflict matrix actually fired."
        //
        // BEFORE the fix: the callsite passed TextingStyleConflicts.Empty via
        //   the 2-arg Aggregate overload → both conflicting values appeared in
        //   the profile → this assertion would FAIL.
        //
        // AFTER the fix: CharacterDefinitionLoader.Load uses AggregateWithAudit
        //   with ConflictCatalog (loaded by CoreTestWiring / PromptWiring.Wire).
        //   Conflict fires → "never sends more than 5 words" is dropped →
        //   this assertion PASSES.
        //
        // Zyx items that trigger the conflict:
        //   harem-pants     (slot=trousers → structure axis):
        //       structure: wall-of-text (one paragraph, no breaks, comma splices throughout)
        //   third-eye-piercing (slot=frame → length axis):
        //       length: never sends more than 5 words
        // These are listed as mutually exclusive in texting-style-conflicts.yaml.
        // ------------------------------------------------------------------

        [Fact]
        public void ProductionPath_ZyxLoad_ConflictResolved_NeverFiveWordsDropped()
        {
            // Arrange: ConflictCatalog is loaded by CoreTestWiring.Initialize()
            // (module initializer, runs before any test). Verify it is set.
            Assert.NotNull(TextingStyleAggregator.ConflictCatalog);
            Assert.True(
                TextingStyleAggregator.ConflictCatalog!.Entries.Count > 0,
                "ConflictCatalog should have been loaded by CoreTestWiring");

            var itemRepo    = LoadItemRepo();
            var anatomyRepo = LoadAnatomyRepo();
            string zyxPath  = Path.Combine(RepoRoot, "data", "characters", "zyx.json");

            // Act
            var profile = CharacterDefinitionLoader.Load(zyxPath, itemRepo, anatomyRepo);

            // Assert: the wall-of-text fragment (from harem-pants/trousers)
            // should appear in the resolved style profile.
            Assert.Contains("wall-of-text", profile.TextingStyleFragment, StringComparison.Ordinal);

            // Assert: "never sends more than 5 words" (from third-eye-piercing/frame)
            // conflicts with wall-of-text and must have been dropped.
            // On the pre-fix code this assertion FAILS because both appeared.
            Assert.DoesNotContain(
                "never sends more than 5 words",
                profile.TextingStyleFragment,
                StringComparison.Ordinal);
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
    }
}

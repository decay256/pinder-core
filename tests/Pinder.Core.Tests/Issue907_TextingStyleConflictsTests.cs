using System;
using System.Collections.Generic;
using System.IO;
using Pinder.Core.Prompts;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="TextingStyleConflicts"/> covering YAML load,
    /// bidirectional lookup, and edge cases.
    ///
    /// See #907.
    /// </summary>
    [Trait("Category", "Prompts")]
    public class Issue907_TextingStyleConflictsTests
    {
        // Shared YAML fixture written once per test class run.
        private static readonly string FixtureYaml = Path.GetTempPath() + "texting-style-conflicts-test-" + Guid.NewGuid().ToString("N") + ".yaml";

        static Issue907_TextingStyleConflictsTests()
        {
            File.WriteAllText(FixtureYaml, @"
conflicts:
  - axis_a: { axis: length, value: ""never sends more than 5 words"" }
    axis_b: { axis: structure, value: ""wall-of-text (one paragraph, no breaks, comma splices throughout)"" }
    reason: ""Wall-of-text is incompatible with hard 5-word cap""
  - axis_a: { axis: pacing, value: ""fast, breathless, low-edit, thoughts overlapping"" }
    axis_b: { axis: structure, value: ""measured whitespace (3-5 short lines, blank between)"" }
    reason: ""Hectic pacing incompatible with measured whitespace structure""
  - axis_a: { axis: tics, value: ""never asks questions, only states"" }
    axis_b: { axis: tics, value: ""always ends with a question, even when not asking"" }
    reason: ""Direct contradiction on question-asking behavior""
");
        }

        private static TextingStyleConflicts Load() => new TextingStyleConflicts(FixtureYaml);

        // ----- basic load -------------------------------------------------

        [Fact]
        public void Load_ReadsAllEntries()
        {
            var c = Load();
            Assert.Equal(3, c.Count);
        }

        [Fact]
        public void Load_ThrowsOnMissingFile()
        {
            Assert.Throws<FileNotFoundException>(() =>
                new TextingStyleConflicts(Path.Combine(Path.GetTempPath(), "__pinder_nonexistent_" + Guid.NewGuid().ToString("N") + ".yaml")));
        }

        [Fact]
        public void Load_ThrowsOnEmptyReason()
        {
            var tmp = Path.GetTempPath() + "bad-" + Guid.NewGuid().ToString("N") + ".yaml";
            File.WriteAllText(tmp, @"
conflicts:
  - axis_a: { axis: a, value: v1 }
    axis_b: { axis: b, value: v2 }
    reason: ""  ""
");
            try
            {
                Assert.Throws<InvalidDataException>(() => new TextingStyleConflicts(tmp));
            }
            finally { File.Delete(tmp); }
        }

        // ----- conflict detection ------------------------------------------

        [Fact]
        public void AreConflicting_FindsKnownPair()
        {
            var c = Load();
            Assert.True(c.AreConflicting(
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                ("length", "never sends more than 5 words")));
        }

        [Fact]
        public void AreConflicting_Bidirectional()
        {
            var c = Load();
            Assert.True(c.AreConflicting(
                ("length", "never sends more than 5 words"),
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)")));
        }

        [Fact]
        public void AreConflicting_NonConflictingPair_ReturnsFalse()
        {
            var c = Load();
            Assert.False(c.AreConflicting(
                ("emoji", "some value"),
                ("shorthand", "another value")));
        }

        [Fact]
        public void GetReason_ReturnsReasonForConflict()
        {
            var c = Load();
            var reason = c.GetReason(
                ("length", "never sends more than 5 words"),
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)"));
            Assert.Contains("hard 5-word cap", reason);
        }

        [Fact]
        public void GetReason_ReturnsNullForNoConflict()
        {
            var c = Load();
            Assert.Null(c.GetReason(
                ("emoji", "anything"),
                ("shorthand", "anything else")));
        }

        // ----- exact matching (pinned: #851 precedent — use exact strings) ---

        [Fact]
        public void AreConflicting_ExactValueMatchRequired()
        {
            var c = Load();
            Assert.False(c.AreConflicting(
                ("length", "never sends more than 5 words (approx)"),
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)")));
        }

        [Fact]
        public void AreConflicting_ExactAxisNameMatchRequired()
        {
            var c = Load();
            Assert.False(c.AreConflicting(
                ("LENGTH", "never sends more than 5 words"),
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)")));
        }

        [Fact]
        public void SameAxis_Conflict_BetweenValues()
        {
            var c = Load();
            Assert.True(c.AreConflicting(
                ("tics", "never asks questions, only states"),
                ("tics", "always ends with a question, even when not asking")));
        }
    }
}

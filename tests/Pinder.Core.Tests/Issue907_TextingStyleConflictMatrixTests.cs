using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// #907 - TextingStyleConflicts loader + TextingStyleAggregator conflict resolution.
    /// </summary>
    [Trait("Category", "Characters")]
    public class Issue907_TextingStyleConflictMatrixTests
    {
        private static string FindDataFile(string relativePath)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                var candidate = Path.Combine(dir, "data", relativePath);
                if (File.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new FileNotFoundException(
                $"Could not locate data/{relativePath} in any ancestor of the test binary.");
        }

        private static TextingStyleConflicts LoadRealConflicts()
        {
            string path = FindDataFile("persona/texting-style-conflicts.yaml");
            return TextingStyleConflicts.LoadFrom(File.ReadAllText(path));
        }

        private const string MinimalConflictsYaml = @"
conflicts:
  - axis_a: { axis: length, value: ""never sends more than 5 words"" }
    axis_b: { axis: length, value: ""minimum 80 words per message, no exceptions"" }
    reason: ""Mutually exclusive length rules""
  - axis_a: { axis: structure, value: ""wall-of-text (one paragraph, no breaks, comma splices throughout)"" }
    axis_b: { axis: length, value: ""never sends more than 5 words"" }
    reason: ""Wall-of-text incompatible with hard 5-word cap""
  - axis_a: { axis: structure, value: ""wall-of-text (one paragraph, no breaks, comma splices throughout)"" }
    axis_b: { axis: pacing, value: ""minimal, sparse, long pauses implied"" }
    reason: ""Wall-of-text incompatible with sparse pacing""
  - axis_a: { axis: pacing, value: ""fast, breathless, low-edit, thoughts overlapping"" }
    axis_b: { axis: structure, value: ""measured whitespace (3-5 short lines, blank between)"" }
    reason: ""Hectic incompatible with measured whitespace""
  - axis_a: { axis: tics, value: ""never asks questions, only states"" }
    axis_b: { axis: tics, value: ""always ends with a question, even when not asking"" }
    reason: ""Direct contradiction on question-asking""
  - axis_a: { axis: stance, value: ""agreer"" }
    axis_b: { axis: stance, value: ""contrarian"" }
    reason: ""Mutually exclusive stances""
";

        // ------------------------------------------------------------------
        // TextingStyleConflicts.LoadFrom tests
        // ------------------------------------------------------------------

        [Fact]
        public void LoadFrom_MinimalYaml_LoadsSixEntries()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            Assert.Equal(6, c.Entries.Count);
        }

        [Fact]
        public void LoadFrom_AllEntriesHaveNonEmptyReason()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            foreach (var entry in c.Entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason),
                    $"Entry ({entry.AxisA}:{entry.ValueA} vs {entry.AxisB}:{entry.ValueB}) has empty reason.");
            }
        }

        [Fact]
        public void AreConflicting_KnownPair_ReturnsTrue()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            Assert.True(c.AreConflicting(
                ("length", "never sends more than 5 words"),
                ("length", "minimum 80 words per message, no exceptions")));
        }

        [Fact]
        public void AreConflicting_IsSymmetric()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);

            var a = ("tics", "never asks questions, only states");
            var b = ("tics", "always ends with a question, even when not asking");

            Assert.True(c.AreConflicting(a, b), "Forward direction should conflict.");
            Assert.True(c.AreConflicting(b, a), "Reverse direction should conflict.");
        }

        [Fact]
        public void AreConflicting_UnknownPair_ReturnsFalse()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            Assert.False(c.AreConflicting(
                ("emoji", "some emoji rule"),
                ("shorthand", "some shorthand rule")));
        }

        [Fact]
        public void AreConflicting_CaseInsensitiveAxisAndValue()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            Assert.True(c.AreConflicting(
                ("LENGTH", "never sends more than 5 words"),
                ("LENGTH", "minimum 80 words per message, no exceptions")));
            Assert.True(c.AreConflicting(
                ("stance", "AGREER"),
                ("stance", "CONTRARIAN")));
        }

        [Fact]
        public void GetReason_KnownPair_ReturnsNonEmptyReason()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            var reason = c.GetReason(
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                ("length", "never sends more than 5 words"));
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.Contains("5-word", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetReason_UnknownPair_ReturnsNull()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            Assert.Null(c.GetReason(
                ("emoji", "foo"),
                ("shorthand", "bar")));
        }

        [Fact]
        public void LoadFrom_EmptyYaml_ReturnsEmpty()
        {
            var c = TextingStyleConflicts.LoadFrom(string.Empty);
            Assert.Equal(0, c.Entries.Count);
        }

        // ------------------------------------------------------------------
        // Real YAML file tests
        // ------------------------------------------------------------------

        [Fact]
        public void RealConflictsYaml_LoadsWithoutException()
        {
            var c = LoadRealConflicts();
            Assert.True(c.Entries.Count >= 6,
                $"Expected at least 6 conflict entries in the real YAML; got {c.Entries.Count}.");
        }

        [Fact]
        public void RealConflictsYaml_AllEntriesHaveNonEmptyReason()
        {
            var c = LoadRealConflicts();
            foreach (var entry in c.Entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason),
                    $"Real conflict entry ({entry.AxisA}:{entry.ValueA} vs {entry.AxisB}:{entry.ValueB}) has empty reason.");
            }
        }

        [Fact]
        public void RealConflictsYaml_ContainsCanonicalConflicts()
        {
            var c = LoadRealConflicts();

            Assert.True(c.AreConflicting(
                ("length", "never sends more than 5 words"),
                ("length", "minimum 80 words per message, no exceptions")),
                "Missing: length:5words vs length:80words");

            Assert.True(c.AreConflicting(
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                ("length", "never sends more than 5 words")),
                "Missing: structure:wall-of-text vs length:5words");

            Assert.True(c.AreConflicting(
                ("tics", "never asks questions, only states"),
                ("tics", "always ends with a question, even when not asking")),
                "Missing: tics:never-questions vs tics:always-question");

            // Stance: use actual parsed values (not parenthetical keys)
            Assert.True(c.AreConflicting(
                ("stance", "\"omg same\" energy, can't disagree, vaguely unsettling"),
                ("stance", "mild disagreement with everything, even compliments")),
                "Missing: stance:agreer vs stance:contrarian");
        }

        // ------------------------------------------------------------------
        // TextingStyleAggregator conflict-resolution tests
        // ------------------------------------------------------------------

        private static TextingStyleFragmentSource MakeItemSource(
            string slot, string axisName, string axisValue)
        {
            string fragment =
                "SYNTAX:\n" +
                "- emoji: default emoji rule\n" +
                "- shorthand: default shorthand\n" +
                "- grammar: default grammar\n" +
                "- structure: default structure\n" +
                "- length: default length\n" +
                "- tics: default tics\n" +
                "TONE:\n" +
                "- stance (neutral): neutral-stance\n" +
                "- register (neutral): neutral-register\n" +
                "- pacing (neutral): neutral-pacing";

            fragment = fragment.Replace(
                $"- {axisName}: default {axisName}",
                $"- {axisName}: {axisValue}");

            return new TextingStyleFragmentSource(
                kind: "item",
                source: slot,
                fragment: fragment,
                slotOrParameter: slot);
        }

        [Fact]
        public void Aggregator_ConflictingLengthAxes_OnlyOneAxisKept_DeterministicWinner()
        {
            // trousers -> structure: wall-of-text
            // frame -> length: never sends more than 5 words
            // The matrix says these conflict. Structure (trousers) is iterated before
            // frame (length) in SlotToSyntaxAxis, so structure is picked first and kept;
            // length is picked later and dropped.

            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);

            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                MakeItemSource("frame",    "length",
                    "never sends more than 5 words"),
            };

            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-001", conflicts);

            var hasStructure = result.Lines.Any(l =>
                l.StartsWith("structure:", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("wall-of-text"));
            var hasLength = result.Lines.Any(l =>
                l.StartsWith("length:", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("5 words"));

            Assert.True(hasStructure,  "structure:wall-of-text should be kept (picked first).");
            Assert.False(hasLength,    "length:5words should be dropped (picked later, conflicts with structure).");

            Assert.Single(result.Drops);
            var drop = result.Drops[0];
            Assert.Equal("char-001", drop.CharacterId);
            Assert.Equal("length",   drop.Axis);
            Assert.Contains("5 words", drop.DroppedValue, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("structure", drop.ConflictAxis);
            Assert.False(string.IsNullOrWhiteSpace(drop.Reason));
        }

        [Fact]
        public void Aggregator_ConflictingTicsPair_DropsLaterPicked()
        {
            // Tests cross-axis conflict: structure:wall-of-text vs pacing:sparse.
            // The anatomy source contributes pacing = "minimal, sparse, long pauses implied".
            // Item syntax axes are processed first, so structure is kept; pacing is dropped.

            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);

            const string sparseFragment =
                "SYNTAX:\n" +
                "TONE:\n" +
                "- stance (neutral): neutral-stance\n" +
                "- register (neutral): neutral-register\n" +
                "- pacing (sparse): minimal, sparse, long pauses implied";

            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                new TextingStyleFragmentSource(
                    kind: "anatomy",
                    source: "ball_size_tier",
                    fragment: sparseFragment,
                    slotOrParameter: "ball_size"),
            };

            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-002", conflicts);

            var hasStructure = result.Lines.Any(l =>
                l.StartsWith("structure:", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("wall-of-text"));
            var hasPacing = result.Lines.Any(l =>
                l.StartsWith("pacing:", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("minimal, sparse"));

            Assert.True(hasStructure, "structure:wall-of-text should be kept (picked first).");
            Assert.False(hasPacing,   "pacing:sparse should be dropped (conflicts with structure:wall-of-text).");

            Assert.Single(result.Drops);
            var drop = result.Drops[0];
            Assert.Equal("char-002", drop.CharacterId);
            Assert.Equal("pacing",   drop.Axis);
            Assert.Contains("minimal, sparse", drop.DroppedValue, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Aggregator_NoConflicts_DropListIsEmpty()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);

            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("shoes",     "emoji",     "a safe emoji rule"),
                MakeItemSource("hat",       "shorthand", "a safe shorthand rule"),
                MakeItemSource("frame",     "length",    "a safe length rule"),
            };

            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-003", conflicts);

            Assert.Empty(result.Drops);
            Assert.Equal(3, result.Lines.Count);
        }

        [Fact]
        public void Aggregator_WithEmptyConflicts_BehaviorUnchangedFromV1()
        {
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                MakeItemSource("frame",    "length",
                    "never sends more than 5 words"),
            };

            var withEmpty    = TextingStyleAggregator.AggregateWithAudit(
                sources, "char-004", TextingStyleConflicts.Empty);
            var legacyResult = TextingStyleAggregator.AggregateAsList(sources, "char-004");

            Assert.Empty(withEmpty.Drops);
            Assert.Equal(legacyResult.Count, withEmpty.Lines.Count);
        }

        [Fact]
        public void Aggregator_AuditEntry_ContainsAllRequiredFields()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);

            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                MakeItemSource("frame",    "length",
                    "never sends more than 5 words"),
            };

            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-audit", conflicts);
            Assert.Single(result.Drops);

            var drop = result.Drops[0];
            Assert.Equal("char-audit",          drop.CharacterId);
            Assert.False(string.IsNullOrWhiteSpace(drop.Axis),          "Axis must be set.");
            Assert.False(string.IsNullOrWhiteSpace(drop.DroppedValue),  "DroppedValue must be set.");
            Assert.False(string.IsNullOrWhiteSpace(drop.ConflictAxis),  "ConflictAxis must be set.");
            Assert.False(string.IsNullOrWhiteSpace(drop.KeptValue),     "KeptValue must be set.");
            Assert.False(string.IsNullOrWhiteSpace(drop.Reason),        "Reason must be set.");

            var str = drop.ToString();
            Assert.Contains("char-audit", str, StringComparison.Ordinal);
            Assert.Contains("ConflictDrop", str, StringComparison.Ordinal);
        }
    }
}

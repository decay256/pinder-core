using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Xunit;

namespace Pinder.Core.Tests
{
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
            string yaml = File.ReadAllText(path);
            return TextingStyleConflicts.LoadFrom(yaml);
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
    axis_b: { axis: pacing, value: ""dry-pacing"" }
    reason: ""Wall-of-text incompatible with sparse pacing""
  - axis_a: { axis: pacing, value: ""hectic"" }
    axis_b: { axis: structure, value: ""measured whitespace (3-5 short lines, blank between)"" }
    reason: ""Hectic incompatible with measured whitespace""
  - axis_a: { axis: tics, value: ""never asks questions, only states"" }
    axis_b: { axis: tics, value: ""always ends with a question, even when not asking"" }
    reason: ""Direct contradiction on question-asking""
  - axis_a: { axis: stance, value: ""agreer"" }
    axis_b: { axis: stance, value: ""contrarian"" }
    reason: ""Mutually exclusive stances""
";

        [Fact]
        public void LoadFrom_MinimalYaml_LoadsSixEntries()
        {
            var c = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            Assert.Equal(6, c.Entries.Count);
        }

        [Fact]
        public void LoadFrom_StructuredYaml_AllowsFlowKeyReorderingQuotedColonsAndMultilineReasons()
        {
            const string yaml = @"
conflicts:
  - axis_a: { value: ""compact, but layered: two beats"", axis: length }
    axis_b:
      value: ""always ends with a question, even when not asking""
      axis: tics
    reason: >-
      Compact values with colons and commas still parse through
      the structured YAML loader.
";
            var c = TextingStyleConflicts.LoadFrom(yaml);

            var entry = Assert.Single(c.Entries);
            Assert.Equal("length", entry.AxisA);
            Assert.Equal("compact, but layered: two beats", entry.ValueA);
            Assert.Equal("tics", entry.AxisB);
            Assert.Contains("structured YAML loader", entry.Reason);
        }

        [Fact]
        public void LoadFrom_UnknownAxis_ThrowsFormatException()
        {
            const string yaml = @"
conflicts:
  - axis_a: { axis: unknown_axis, value: ""value"" }
    axis_b: { axis: length, value: ""other"" }
    reason: ""bad axis""
";
            var ex = Assert.Throws<FormatException>(() => TextingStyleConflicts.LoadFrom(yaml));
            Assert.Contains("unknown_axis", ex.Message);
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
            Assert.True(c.AreConflicting(a, b), "Forward should conflict.");
            Assert.True(c.AreConflicting(b, a), "Reverse should conflict.");
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
            Assert.Null(c.GetReason(("emoji", "foo"), ("shorthand", "bar")));
        }

        [Fact]
        public void LoadFrom_EmptyString_ReturnsEmptyCatalog()
        {
            var c = TextingStyleConflicts.LoadFrom("");
            Assert.Equal(0, c.Entries.Count);
        }

        [Fact]
        public void RealConflictsYaml_LoadsWithoutException()
        {
            var c = LoadRealConflicts();
            Assert.True(c.Entries.Count >= 6,
                $"Expected >=6 entries; got {c.Entries.Count}.");
        }

        [Fact]
        public void RealConflictsYaml_AllEntriesHaveNonEmptyReason()
        {
            var c = LoadRealConflicts();
            foreach (var entry in c.Entries)
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason),
                    $"Entry ({entry.AxisA}:{entry.ValueA} vs {entry.AxisB}:{entry.ValueB}) has empty reason.");
        }

        [Fact]
        public void RealConflictsYaml_ContainsCanonicalConflicts()
        {
            var c = LoadRealConflicts();
            // #1094 (commit 8d054c7) retired the "minimum 80 words per message" length
            // floor in favour of a brevity-compatible "compact but layered" axis and
            // updated the conflict matrix accordingly. The canonical length×length
            // conflict is now 5-word-cap vs. "compact but layered", not vs. the old
            // 80-word floor. (This assertion was left stale by #1094, which shipped
            // without CI — see #1095 PR notes.)
            Assert.True(c.AreConflicting(
                ("length", "never sends more than 5 words"),
                ("length", "compact but layered — 1-2 sentences, several ideas packed in, never padded")),
                "Missing: length:5words vs length:compact-but-layered");
            Assert.True(c.AreConflicting(
                ("structure", "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                ("length", "never sends more than 5 words")),
                "Missing: structure:wall-of-text vs length:5words");
            Assert.True(c.AreConflicting(
                ("tics", "never asks questions, only states"),
                ("tics", "always ends with a question, even when not asking")),
                "Missing: tics:never-questions vs tics:always-question");
            Assert.True(c.AreConflicting(
                ("stance", "\"omg same\" energy, can't disagree, vaguely unsettling"),
                ("stance", "mild disagreement with everything, even compliments")),
                "Missing: stance:agreer vs stance:contrarian");
        }

        private static TextingStyleFragmentSource MakeItemSource(
            string slot, string axisName, string axisValue)
        {
            string fragment =
                "SYNTAX:\n" +
                $"- emoji: default emoji rule\n" +
                $"- shorthand: default shorthand\n" +
                $"- grammar: default grammar\n" +
                $"- structure: default structure\n" +
                $"- length: default length\n" +
                $"- tics: default tics\n" +
                "TONE:\n" +
                "- stance (neutral): neutral\n" +
                "- register (neutral): neutral\n" +
                "- pacing (neutral): neutral";
            fragment = fragment.Replace(
                $"- {axisName}: default {axisName}",
                $"- {axisName}: {axisValue}");
            return new TextingStyleFragmentSource(
                kind: "item", source: slot, fragment: fragment, slotOrParameter: slot);
        }

        [Fact]
        public void AggregateWithAudit_ConflictingStructureVsLength_DropsLaterPicked()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                MakeItemSource("frame", "length",
                    "never sends more than 5 words"),
            };
            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-001", conflicts);

            var hasStructure = result.Lines.Any(l =>
                l.StartsWith("structure:", StringComparison.OrdinalIgnoreCase));
            var hasLength = result.Lines.Any(l =>
                l.StartsWith("length:", StringComparison.OrdinalIgnoreCase));

            Assert.True(hasStructure, "structure should be kept (canonically first).");
            Assert.False(hasLength, "length should be dropped (conflicts with structure).");
            Assert.Single(result.Drops);

            var drop = result.Drops[0];
            Assert.Equal("char-001", drop.CharacterId);
            Assert.Equal("length", drop.Axis);
            Assert.Contains("5 words", drop.DroppedValue, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(drop.Reason));
        }

        [Fact]
        public void AggregateWithAudit_ConflictingStructureVsPacing_DropsPacing()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            string MakeToneFragment(string pacingKey, string pacingValue) =>
                "SYNTAX:\n" +
                "TONE:\n" +
                $"- stance (neutral): neutral-stance\n" +
                $"- register (neutral): neutral-register\n" +
                $"- pacing ({pacingKey}): {pacingValue}\n";
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                new TextingStyleFragmentSource(
                    kind: "anatomy", source: "isCircumcised[0.00,0.50)",
                    fragment: MakeToneFragment("dry-low-edit", "dry-pacing"),
                    slotOrParameter: "isCircumcised"),
            };
            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-002", conflicts);

            var hasStructure = result.Lines.Any(l =>
                l.StartsWith("structure:", StringComparison.OrdinalIgnoreCase));
            var hasPacing = result.Lines.Any(l =>
                l.StartsWith("pacing:", StringComparison.OrdinalIgnoreCase));

            Assert.True(hasStructure, "structure should be kept.");
            Assert.False(hasPacing, "pacing:dry-pacing should be dropped.");
            Assert.Single(result.Drops);
        }

        [Fact]
        public void AggregateWithAudit_NoConflicts_DropListIsEmpty()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("shoes", "emoji", "a safe emoji rule"),
                MakeItemSource("hat",   "shorthand", "a safe shorthand rule"),
                MakeItemSource("frame", "length", "a safe length rule"),
            };
            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-003", conflicts);
            Assert.Empty(result.Drops);
            Assert.Equal(3, result.Lines.Count);
        }

        [Fact]
        public void AggregateWithAudit_WithEmptyConflicts_NoDrops()
        {
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                MakeItemSource("frame", "length",
                    "never sends more than 5 words"),
            };
            var result = TextingStyleAggregator.AggregateWithAudit(
                sources, "char-004", TextingStyleConflicts.Empty);
            Assert.Empty(result.Drops);
            Assert.Equal(2, result.Lines.Count);
        }

        [Fact]
        public void AggregateWithAudit_AuditEntry_ContainsAllRequiredFields()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("trousers", "structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)"),
                MakeItemSource("frame", "length",
                    "never sends more than 5 words"),
            };
            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-audit", conflicts);
            Assert.Single(result.Drops);
            var drop = result.Drops[0];
            Assert.Equal("char-audit", drop.CharacterId);
            Assert.False(string.IsNullOrWhiteSpace(drop.Axis));
            Assert.False(string.IsNullOrWhiteSpace(drop.DroppedValue));
            Assert.False(string.IsNullOrWhiteSpace(drop.ConflictAxis));
            Assert.False(string.IsNullOrWhiteSpace(drop.KeptValue));
            Assert.False(string.IsNullOrWhiteSpace(drop.Reason));
            var str = drop.ToString();
            Assert.Contains("char-audit", str, StringComparison.Ordinal);
            Assert.Contains("ConflictDrop", str, StringComparison.Ordinal);
        }

        [Fact]
        public void AggregateWithAudit_SameSeedKeyDifferentOutput_Unused()
        {
            var conflicts = TextingStyleConflicts.LoadFrom(MinimalConflictsYaml);
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeItemSource("shoes", "emoji", "a safe emoji rule"),
            };
            var r1 = TextingStyleAggregator.AggregateWithAudit(sources, "seed-a", conflicts);
            var r2 = TextingStyleAggregator.AggregateWithAudit(sources, "seed-b", conflicts);
            Assert.Equal(r1.Lines, r2.Lines);
            Assert.Equal(r1.Drops.Count, r2.Drops.Count);
        }

        [Fact]
        public void ConflictDropEntry_ToString_DoesNotThrow()
        {
            var entry = new TextingStyleAggregator.ConflictDropEntry(
                "test-id", "length", "dropped-val", "structure", "kept-val", "test reason");
            var s = entry.ToString();
            Assert.Contains("test-id", s, StringComparison.Ordinal);
            Assert.Contains("ConflictDrop", s, StringComparison.Ordinal);
        }

        [Fact]
        public void AggregationResult_Constructor_StoresArguments()
        {
            var lines = new List<string> { "emoji: test" };
            var drops = new List<TextingStyleAggregator.ConflictDropEntry>();
            var result = new TextingStyleAggregator.AggregationResult(lines, drops);
            Assert.Single(result.Lines);
            Assert.Empty(result.Drops);
        }
    }
}

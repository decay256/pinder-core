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
    /// Tests for conflict-aware texting-style aggregation (#907).
    ///
    /// Ensures that when <see cref="TextingStyleConflicts"/> is provided
    /// to <see cref="TextingStyleAggregator.AggregateAsList"/>,
    /// conflicting axis-value pairs are resolved deterministically and
    /// audit entries are emitted for dropped fragments.
    /// </summary>
    [Trait("Category", "Prompts")]
    public class Issue907_TextingStyleAggregatorConflictTests
    {
        // ----- YAML fixture ------------------------------------------------

        private static readonly string ConflictYaml;

        static Issue907_TextingStyleAggregatorConflictTests()
        {
            ConflictYaml = Path.GetTempPath() + "ts-conflict-agg-test-" + Guid.NewGuid().ToString("N") + ".yaml";
            File.WriteAllText(ConflictYaml, @"
conflicts:
  - axis_a: { axis: structure, value: ""wall-of-text (one paragraph, no breaks, comma splices throughout)"" }
    axis_b: { axis: length, value: ""never sends more than 5 words"" }
    reason: ""Wall-of-text incompatible with 5-word cap""
");
        }

        // ----- helpers -----------------------------------------------------

        private static TextingStyleConflicts Conflicts => new TextingStyleConflicts(ConflictYaml);

        private static TextingStyleFragmentSource ItemSource(string slot, string fragment)
            => new TextingStyleFragmentSource("item", "test-item", fragment, slot);

        // Minimal fragment with one syntax axis filled.
        private static string SyntaxFrag(string axis, string value)
            => $@"SYNTAX:
  - emoji: uses no emoji
  - shorthand: standard
  - grammar: standard
  - structure: standard
  - length: standard
  - tics: standard
TONE:
  - stance (a): neutral
  - register (a): standard
  - pacing (a): standard"
            .Replace($"- {axis}: standard", $"- {axis}: {value}");

        // ----- backward compatibility --------------------------------------

        [Fact]
        public void NullConflicts_PreservesV1Behavior()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)")),
                ItemSource("frame", SyntaxFrag("length",
                    "never sends more than 5 words")),
            };

            // Without conflicts, both axes survive as-is.
            var result = TextingStyleAggregator.AggregateAsList(sources, null, null, out var audit);
            Assert.Contains(result, s => s.StartsWith("structure: wall-of-text"));
            Assert.Contains(result, s => s.StartsWith("length: never sends more than 5 words"));
            Assert.Null(audit);
        }

        [Fact]
        public void BackwardCompatOverload_ProducesSameResult()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)")),
            };

            var result1 = TextingStyleAggregator.AggregateAsList(sources, null, null, out _);
            var result2 = TextingStyleAggregator.AggregateAsList(sources, null);
            Assert.Equal(result1, result2);
        }

        // ----- conflict resolution -----------------------------------------

        [Fact]
        public void ConflictingPair_DropsLaterAxis()
        {
            // structure (canonical index 3) wins over length (index 4)
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)")),
                ItemSource("frame", SyntaxFrag("length",
                    "never sends more than 5 words")),
            };

            var conflicts = Conflicts;
            var result = TextingStyleAggregator.AggregateAsList(sources, null, conflicts, out var audit);

            Assert.Contains(result, s => s.StartsWith("structure: wall-of-text"));
            Assert.DoesNotContain(result, s => s.StartsWith("length: never sends more than 5 words"));
        }

        [Fact]
        public void Conflict_EmitsAuditEntry()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)")),
                ItemSource("frame", SyntaxFrag("length",
                    "never sends more than 5 words")),
            };

            var conflicts = Conflicts;
            TextingStyleAggregator.AggregateAsList(sources, null, conflicts, out var audit);

            Assert.NotNull(audit);
            Assert.Single(audit);
            var entry = audit[0];
            Assert.Equal("length", entry.Axis);
            Assert.Contains("5 words", entry.DroppedValue);
            Assert.Equal("structure", entry.KeptAxis);
            Assert.Contains("wall-of-text", entry.KeptValue);
            Assert.Contains("5-word cap", entry.Reason);
        }

        [Fact]
        public void NoConflict_NoAudit()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure", "measured whitespace")),
                ItemSource("frame", SyntaxFrag("length", "medium-length replies")),
            };

            var conflicts = Conflicts;
            var result = TextingStyleAggregator.AggregateAsList(sources, null, conflicts, out var audit);

            Assert.Contains(result, s => s.StartsWith("structure: measured whitespace"));
            Assert.Contains(result, s => s.StartsWith("length: medium-length replies"));
            Assert.Null(audit);
        }

        [Fact]
        public void EmptySources_ReturnsEmpty()
        {
            var result = TextingStyleAggregator.AggregateAsList(
                Array.Empty<TextingStyleFragmentSource>(), null, Conflicts, out var audit);

            Assert.Empty(result);
            Assert.Null(audit);
        }

        // ----- deterministic by construction -------------------------------

        [Fact]
        public void RepeatedCalls_Deterministic()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)")),
                ItemSource("frame", SyntaxFrag("length",
                    "never sends more than 5 words")),
            };

            var conflicts = Conflicts;
            var first = TextingStyleAggregator.AggregateAsList(sources, null, conflicts, out _);

            for (int i = 0; i < 10; i++)
            {
                var next = TextingStyleAggregator.AggregateAsList(sources, null, conflicts, out _);
                Assert.Equal(first, next);
            }
        }

        // ----- aggregate string method -------------------------------------

        [Fact]
        public void Aggregate_WithConflicts_JoinsWithPipe()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure",
                    "wall-of-text (one paragraph, no breaks, comma splices throughout)")),
                ItemSource("frame", SyntaxFrag("length",
                    "never sends more than 5 words")),
            };

            var result = TextingStyleAggregator.Aggregate(sources, null, Conflicts, out var audit);
            Assert.NotNull(audit);
            Assert.Contains("structure: wall-of-text", result);
            Assert.DoesNotContain("5 words", result);
        }

        [Fact]
        public void Aggregate_BackwardCompat_ReturnsString()
        {
            var sources = new[]
            {
                ItemSource("trousers", SyntaxFrag("structure", "measured whitespace")),
            };
            var result = TextingStyleAggregator.Aggregate(sources, null);
            Assert.Contains("structure: measured whitespace", result);
        }
    }
}

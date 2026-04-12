using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests
{
    public class WordDiffTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static IReadOnlyList<DiffSpan> Diff(string before, string after)
            => WordDiff.Compute(before, after);

        private static string Render(IReadOnlyList<DiffSpan> spans) =>
            string.Concat(spans.Select(s => s.Text));

        // ── Single word replacement ───────────────────────────────────────────

        [Fact]
        public void SingleWordReplacement_HelloWorld_HelloEarth()
        {
            // "hello world" → "hello earth"
            var spans = Diff("hello world", "hello earth");

            // Should have: Keep("hello "), Remove("world"), Add("earth")
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Keep   && s.Text.TrimEnd() == "hello"), $"Expected Keep 'hello', got: {RenderSpans(spans)}");
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Remove && s.Text.TrimEnd() == "world"), $"Expected Remove 'world', got: {RenderSpans(spans)}");
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Add    && s.Text.TrimEnd() == "earth"), $"Expected Add 'earth', got: {RenderSpans(spans)}");
        }

        [Fact]
        public void SingleWordReplacement_ReconstructsAfterString()
        {
            var spans = Diff("hello world", "hello earth");
            // Kept + added tokens should reconstruct "hello earth"
            string reconstructed = string.Concat(
                spans.Where(s => s.Type != DiffSpanType.Remove).Select(s => s.Text));
            Assert.Equal("hello earth", reconstructed.TrimEnd());
        }

        // ── No change ─────────────────────────────────────────────────────────

        [Fact]
        public void NoChange_IdenticalStrings_AllKeep()
        {
            var spans = Diff("hello world this is unchanged", "hello world this is unchanged");
            Assert.All(spans, s => Assert.Equal(DiffSpanType.Keep, s.Type));
            Assert.Equal("hello world this is unchanged", Render(spans).TrimEnd());
        }

        // ── Add at end (steering case) ────────────────────────────────────────

        [Fact]
        public void AddAtEnd_SteeringCase()
        {
            // "text" → "text question?"
            var spans = Diff("text", "text question?");
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Keep && s.Text.TrimEnd() == "text"),
                $"Expected Keep 'text', got: {RenderSpans(spans)}");
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Add && s.Text.TrimEnd() == "question?"),
                $"Expected Add 'question?', got: {RenderSpans(spans)}");
            Assert.False(spans.Any(s => s.Type == DiffSpanType.Remove),
                "Should have no Remove spans");
        }

        // ── Whole section replacement ─────────────────────────────────────────

        [Fact]
        public void WholeSectionReplacement_LongText()
        {
            string before = "hey how are you doing today I was thinking we could grab coffee";
            string after  = "hey how are you doing today I was wondering if you want to hang out";

            var spans = Diff(before, after);

            // Common prefix should be kept
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Keep && s.Text.Contains("hey")),
                "Should keep common prefix");
            // Removed words should appear
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Remove),
                "Should have some removed spans");
            // Added words should appear
            Assert.True(spans.Any(s => s.Type == DiffSpanType.Add),
                "Should have some added spans");

            // Reconstruct "after" from non-removed spans
            string reconstructed = string.Concat(
                spans.Where(s => s.Type != DiffSpanType.Remove).Select(s => s.Text));
            Assert.Equal(after, reconstructed.TrimEnd());
        }

        // ── Empty edge cases ─────────────────────────────────────────────────

        [Fact]
        public void EmptyBefore_AllAdd()
        {
            var spans = Diff("", "hello world");
            Assert.True(spans.Count == 0 || spans.All(s => s.Type == DiffSpanType.Add),
                "All spans should be Add when before is empty");
        }

        [Fact]
        public void EmptyAfter_AllRemove()
        {
            var spans = Diff("hello world", "");
            Assert.True(spans.Count == 0 || spans.All(s => s.Type == DiffSpanType.Remove),
                "All spans should be Remove when after is empty");
        }

        [Fact]
        public void BothEmpty_NoSpans()
        {
            var spans = Diff("", "");
            Assert.Empty(spans);
        }

        // ── Merge: consecutive same-type spans are merged ─────────────────────

        [Fact]
        public void ConsecutiveSameType_AreMerged()
        {
            // All kept — should be one or few spans, not one per word
            var spans = Diff("a b c d e", "a b c d e");
            Assert.True(spans.Count <= 1, $"Expected at most 1 merged Keep span, got {spans.Count}");
        }

        // ── TextDiff wrapper ──────────────────────────────────────────────────

        [Fact]
        public void TextDiff_StoresAllProperties()
        {
            var spans = Diff("hello", "world");
            var diff = new TextDiff("Misfire", spans, "hello", "world");

            Assert.Equal("Misfire", diff.LayerName);
            Assert.Equal("hello", diff.Before);
            Assert.Equal("world", diff.After);
            Assert.Same(spans, diff.Spans);
        }

        // ── RenderDiff helper (mirrors Program.RenderDiff logic) ──────────────

        [Fact]
        public void RenderDiff_RendersMarkdown()
        {
            // "hello world" → "hello earth"
            // Expected rendered: "hello ~~world~~ ***earth***" (approximately)
            var spans = Diff("hello world", "hello earth");
            var sb = new System.Text.StringBuilder();
            foreach (var span in spans)
            {
                switch (span.Type)
                {
                    case DiffSpanType.Keep:   sb.Append(span.Text); break;
                    case DiffSpanType.Remove: sb.Append("~~").Append(span.Text.TrimEnd()).Append("~~ "); break;
                    case DiffSpanType.Add:    sb.Append("***").Append(span.Text.TrimEnd()).Append("*** "); break;
                }
            }
            string rendered = sb.ToString().TrimEnd();
            Assert.Contains("~~world~~", rendered);
            Assert.Contains("***earth***", rendered);
            Assert.Contains("hello", rendered);
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static string RenderSpans(IReadOnlyList<DiffSpan> spans) =>
            string.Join(", ", spans.Select(s => $"{s.Type}(\"{s.Text}\")"));
    }
}

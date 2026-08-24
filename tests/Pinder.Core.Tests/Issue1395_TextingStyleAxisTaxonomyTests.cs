using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Prompts;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Characters")]
    public class Issue1395_TextingStyleAxisTaxonomyTests
    {
        private static readonly string[] CanonicalNineAxisOrder =
        {
            "emoji", "shorthand", "grammar", "structure", "length", "tics",
            "directness", "affect", "rhythm",
        };

        private static readonly string[] LegacyToneAxes =
        {
            "stance", "register", "pacing",
        };

        [Fact]
        public void CanonicalAxisOrder_UsesNineAxisModelWithoutLegacyToneNames()
        {
            Assert.Equal(CanonicalNineAxisOrder, TextingStyleTaxonomy.CanonicalAxes);
            Assert.Equal(CanonicalNineAxisOrder, TextingStyleAggregator.CanonicalAxisOrder);
            Assert.Equal(CanonicalNineAxisOrder.Take(6), TextingStyleTaxonomy.SyntaxAxes);
            Assert.Equal(CanonicalNineAxisOrder.Skip(6), TextingStyleTaxonomy.ExpressionAxes);
            foreach (var legacyAxis in LegacyToneAxes)
                Assert.DoesNotContain(legacyAxis, TextingStyleTaxonomy.CanonicalAxes);
        }

        [Fact]
        public void CanonicalTaxonomy_IsCaseInsensitiveAndCannotBeMutatedByCallers()
        {
            Assert.True(TextingStyleTaxonomy.IsSyntaxAxis("EMOJI"));
            Assert.True(TextingStyleTaxonomy.IsExpressionAxis("Directness"));
            Assert.False(TextingStyleTaxonomy.IsSyntaxAxis("stance"));
            Assert.False(TextingStyleTaxonomy.IsExpressionAxis("register"));

            Assert.Throws<NotSupportedException>(
                () => ((IList<string>)TextingStyleTaxonomy.SyntaxAxes)[0] = "changed");
            Assert.Throws<NotSupportedException>(
                () => ((IList<string>)TextingStyleTaxonomy.ExpressionAxes).Add("changed"));
            Assert.Throws<NotSupportedException>(
                () => ((IList<string>)TextingStyleTaxonomy.CanonicalAxes).RemoveAt(0));

            Assert.Equal(CanonicalNineAxisOrder, TextingStyleTaxonomy.CanonicalAxes);
        }

        [Fact]
        public void ParseToneAxes_LegacyToneHeader_NormalizesExpressionAxes()
        {
            const string fragment =
                "TONE:\n" +
                "- stance (guarded): answer directly but without softening the edge\n" +
                "- register (bright): let warmth show through punctuation\n" +
                "- pacing (staccato): send clipped beats with visible pauses";

            var axes = TextingStyleAggregator.ParseToneAxes(fragment);

            Assert.Equal(
                new[] { "affect", "directness", "rhythm" },
                axes.Keys.OrderBy(axis => axis, StringComparer.Ordinal).ToArray());
            Assert.Equal("answer directly but without softening the edge", axes["directness"]);
            Assert.Equal("let warmth show through punctuation", axes["affect"]);
            Assert.Equal("send clipped beats with visible pauses", axes["rhythm"]);
            foreach (var legacyAxis in LegacyToneAxes)
                Assert.False(axes.ContainsKey(legacyAxis), $"Legacy axis {legacyAxis} must not appear in parsed output.");
        }

        [Fact]
        public void AggregateWithAudit_AnatomyToneFragments_EmitAttributedExpressionAxesOnly()
        {
            var sources = new List<TextingStyleFragmentSource>
            {
                MakeAnatomyToneFragment("trunkLengthBase", "direct", directness: "be very clear", affect: "ignored", rhythm: "ignored"),
                MakeAnatomyToneFragment("skinHue", "warm", directness: "ignored", affect: "show warmth", rhythm: "ignored"),
                MakeAnatomyToneFragment("glansScale", "paced", directness: "ignored", affect: "ignored", rhythm: "reply in two short beats"),
            };

            var result = TextingStyleAggregator.AggregateWithAudit(sources, "char-1395", TextingStyleConflicts.Empty);

            Assert.Equal(
                new[]
                {
                    "directness: be very clear",
                    "affect: show warmth",
                    "rhythm: reply in two short beats",
                },
                result.Lines);
            Assert.Equal(new[] { "directness", "affect", "rhythm" }, result.AttributedLines.Select(line => line.Axis).ToArray());
            foreach (var legacyAxis in LegacyToneAxes)
            {
                Assert.DoesNotContain(result.Lines, line => line.StartsWith(legacyAxis + ":", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(result.AttributedLines, line => string.Equals(line.Axis, legacyAxis, StringComparison.OrdinalIgnoreCase));
            }
        }


        [Fact]
        public void ConflictEntries_LegacyExpressionAxisAliases_NormalizeToCanonicalAxes()
        {
            var conflicts = TextingStyleConflicts.FromEntries(new[]
            {
                (AxisA: "stance", ValueA: "guarded", AxisB: "pacing", ValueB: "staccato", Reason: "legacy content before #1398"),
            });

            var entry = Assert.Single(conflicts.Entries);
            Assert.Equal("directness", entry.AxisA);
            Assert.Equal("rhythm", entry.AxisB);
            Assert.True(conflicts.AreConflicting(("directness", "guarded"), ("rhythm", "staccato")));
            Assert.True(conflicts.AreConflicting(("stance", "guarded"), ("pacing", "staccato")));
            foreach (var legacyAxis in LegacyToneAxes)
            {
                Assert.NotEqual(legacyAxis, entry.AxisA, StringComparer.OrdinalIgnoreCase);
                Assert.NotEqual(legacyAxis, entry.AxisB, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static TextingStyleFragmentSource MakeAnatomyToneFragment(
            string parameterId,
            string tierName,
            string directness,
            string affect,
            string rhythm)
        {
            string fragment =
                "SYNTAX:\n" +
                "TONE:\n" +
                $"- stance ({tierName}): {directness}\n" +
                $"- register ({tierName}): {affect}\n" +
                $"- pacing ({tierName}): {rhythm}";
            return new TextingStyleFragmentSource(
                kind: "anatomy",
                source: tierName,
                fragment: fragment,
                slotOrParameter: parameterId);
        }
    }
}

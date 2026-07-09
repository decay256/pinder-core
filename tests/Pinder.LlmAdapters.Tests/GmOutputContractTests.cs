using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #1124: the ONE canonical Game Master output-format contract. Verifies
    /// the emit/parse pair round-trips and that malformed input degrades
    /// gracefully (never throws; collapses to a message-only result).
    /// </summary>
    public class GmOutputContractTests
    {
        // ── Round-trip: parse(emit(x)) == x ──────────────────────────────────

        [Fact]
        public void RoundTrip_MessageOnly()
        {
            var x = new GmTurnOutput("hey, what's up?");
            var emitted = GmOutputContract.Emit(x);
            var parsed = GmOutputContract.Parse(emitted);
            Assert.Equal(x, parsed);
        }

        [Fact]
        public void RoundTrip_MessageWithTell()
        {
            var x = new GmTurnOutput(
                "Nice try, but you'll have to do better than that.",
                tell: new Tell(StatType.Charm, "she twirls her hair when nervous"));

            var emitted = GmOutputContract.Emit(x);
            var parsed = GmOutputContract.Parse(emitted);
            Assert.Equal(x, parsed);
        }

        [Fact]
        public void RoundTrip_MessageWithWeakness()
        {
            var x = new GmTurnOutput(
                "ok that actually got me",
                weakness: new WeaknessWindow(StatType.Wit, 2),
                weaknessDescription: "distracted by the joke");

            var emitted = GmOutputContract.Emit(x);
            var parsed = GmOutputContract.Parse(emitted);
            Assert.Equal(x, parsed);
        }

        [Fact]
        public void RoundTrip_MessageWithBothSignals()
        {
            var x = new GmTurnOutput(
                "you think you're clever?",
                tell: new Tell(StatType.Honesty, "avoiding eye contact"),
                weakness: new WeaknessWindow(StatType.Chaos, 3),
                weaknessDescription: "thrown off balance");

            var emitted = GmOutputContract.Emit(x);
            var parsed = GmOutputContract.Parse(emitted);
            Assert.Equal(x, parsed);
        }

        [Fact]
        public void RoundTrip_SelfAwareness_NormalizesAndSurvives()
        {
            var x = new GmTurnOutput(
                "I know exactly what I'm doing.",
                tell: new Tell(StatType.SelfAwareness, "knows her flaws"));

            var emitted = GmOutputContract.Emit(x);
            // Canonical wire spelling is the SELF_AWARENESS token.
            Assert.Contains("SELF_AWARENESS", emitted);
            var parsed = GmOutputContract.Parse(emitted);
            Assert.Equal(x, parsed);
        }

        [Fact]
        public void RoundTrip_MultiLineMessage()
        {
            var x = new GmTurnOutput("first line\nsecond line\nthird line");
            var parsed = GmOutputContract.Parse(GmOutputContract.Emit(x));
            Assert.Equal(x, parsed);
        }

        // ── Emit shape ───────────────────────────────────────────────────────

        [Fact]
        public void Emit_NoSignals_OmitsSignalsBlock()
        {
            var emitted = GmOutputContract.Emit(new GmTurnOutput("just a message"));
            Assert.DoesNotContain(GmOutputContract.SignalsMarker, emitted);
            Assert.Equal("just a message", emitted);
        }

        [Fact]
        public void Emit_WithSignals_IncludesSignalsMarker()
        {
            var emitted = GmOutputContract.Emit(new GmTurnOutput(
                "msg", tell: new Tell(StatType.Rizz, "leaning in")));
            Assert.Contains(GmOutputContract.SignalsMarker, emitted);
            Assert.Contains("TELL: Rizz (leaning in)", emitted);
        }

        // ── Parse from raw model text (canonical hand-authored format) ────────

        [Fact]
        public void Parse_CanonicalText_ExtractsMessageAndSignals()
        {
            var raw = "Nice try, but you'll have to do better than that.\n" +
                      "[SIGNALS]\n" +
                      "TELL: Charm (she twirls her hair when nervous)\n" +
                      "WEAKNESS: Wit -2 (distracted by the joke)";

            var parsed = GmOutputContract.Parse(raw);
            Assert.Equal("Nice try, but you'll have to do better than that.", parsed.Message);
            Assert.NotNull(parsed.Tell);
            Assert.Equal(StatType.Charm, parsed.Tell!.Stat);
            Assert.Equal("she twirls her hair when nervous", parsed.Tell.Description);
            Assert.NotNull(parsed.Weakness);
            Assert.Equal(StatType.Wit, parsed.Weakness!.DefendingStat);
            Assert.Equal(2, parsed.Weakness.DcReduction);
        }

        // ── Graceful degradation (malformed input never throws) ───────────────

        [Fact]
        public void Parse_Null_ReturnsEmptyMessage()
        {
            var parsed = GmOutputContract.Parse(null);
            Assert.Equal(string.Empty, parsed.Message);
            Assert.Null(parsed.Tell);
            Assert.Null(parsed.Weakness);
        }

        [Fact]
        public void Parse_Empty_ReturnsEmptyMessage()
        {
            var parsed = GmOutputContract.Parse("");
            Assert.Equal(string.Empty, parsed.Message);
            Assert.Null(parsed.Tell);
            Assert.Null(parsed.Weakness);
        }

        [Fact]
        public void Parse_SignalsMarkerButGarbageBody_DegradesToMessageOnly()
        {
            var raw = "here's my reply\n[SIGNALS]\nTELL: this is not valid at all";
            var parsed = GmOutputContract.Parse(raw);
            // Message is preserved; unparseable signals collapse to null.
            Assert.Equal("here's my reply", parsed.Message);
            Assert.Null(parsed.Tell);
            Assert.Null(parsed.Weakness);
        }

        [Fact]
        public void Parse_UnknownStat_DropsSignalKeepsMessage()
        {
            var raw = "reply text\n[SIGNALS]\nTELL: Telepathy (not a real stat)";
            var parsed = GmOutputContract.Parse(raw);
            Assert.Equal("reply text", parsed.Message);
            Assert.Null(parsed.Tell);
        }

        [Fact]
        public void Parse_ZeroWeaknessReduction_DropsWeakness()
        {
            var raw = "reply\n[SIGNALS]\nWEAKNESS: Wit -0 (no real opening)";
            var parsed = GmOutputContract.Parse(raw);
            Assert.Null(parsed.Weakness);
        }

        [Fact]
        public void Parse_PlainTextNoSignals_ReturnsWholeMessage()
        {
            var parsed = GmOutputContract.Parse("just a normal line with no signals");
            Assert.Equal("just a normal line with no signals", parsed.Message);
            Assert.Null(parsed.Tell);
            Assert.Null(parsed.Weakness);
        }

        [Theory]
        [InlineData("[SIGNALS]\nTELL: Charm (she twirls her hair when nervous)")]
        [InlineData("[RESPONSE]\n[SIGNALS]\nWEAKNESS: Wit -2 (distracted by the joke)")]
        public void ValidateSignalsStrict_SignalsWithoutResponseText_IsMalformed(string raw)
        {
            var result = GmOutputContract.ValidateSignalsStrict(raw, out var errorDetail);

            Assert.Equal(DateeSignalsValidationResult.MalformedSignals, result);
            Assert.Equal("missing_response_text", errorDetail);
        }

        // ── Emit guards ──────────────────────────────────────────────────────

        [Fact]
        public void Emit_NullOutput_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => GmOutputContract.Emit(null!));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.SessionRunner.Snapshot;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #474: per-turn <c>Events</c> array on <see cref="TurnSnapshot"/>,
    /// populated from <see cref="TurnResult"/> via
    /// <see cref="TurnEventDetector.DetectEventKinds"/> and rendered to
    /// human-readable interpretation strings via
    /// <see cref="Pinder.LlmAdapters.I18nCatalog.TVariant"/>.
    ///
    /// <para>
    /// This fixture covers:
    /// <list type="bullet">
    ///   <item>Per-event-kind detection from a fully-saturated TurnResult.</item>
    ///   <item>Mutual exclusion between roll-class events (nat_20 / nat_1
    ///   / miss_by_N).</item>
    ///   <item>Multiple shadow ticks on the same turn each emit their own
    ///   <c>shadow_tick_*</c> kind.</item>
    ///   <item>Ordering matches the engine emission order
    ///   (roll \u2192 combo/tell/callback \u2192 horniness \u2192 shadows \u2192 trap).</item>
    ///   <item>Forward compat: new event kinds with no yaml entry
    ///   surface in <c>Events</c> with empty interpretation rather than
    ///   throwing.</item>
    ///   <item>Schema-discipline: <c>BuildTurnSnapshot</c> populates
    ///   <c>Events</c> on every snapshot.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public class Issue474SnapshotEventsTests
    {
        // ── Detector unit tests ──────────────────────────────────────────

        [Fact]
        public void Detector_NeutralTurn_EmitsNoEvents()
        {
            // Plain success, no overlays, no growth, no traps. The
            // events.yaml framing reserves the kinds for moments
            // worth explaining to the player; a clean Charm 12 vs DC 10
            // is just "you nailed the line" with no annotation.
            var result = MakeResult(roll: MakeSuccessRoll());
            var kinds = TurnEventDetector.DetectEventKinds(result);
            Assert.Empty(kinds);
        }

        [Fact]
        public void Detector_Nat20_EmitsNat20()
        {
            var roll = new RollResult(20, null, 20, StatType.Charm, 2, 0, 13, FailureTier.Success);
            var result = MakeResult(roll: roll);
            var kinds = TurnEventDetector.DetectEventKinds(result);
            Assert.Equal(new[] { "nat_20" }, kinds);
        }

        [Fact]
        public void Detector_Nat1_EmitsNat1_NotMissByN()
        {
            // RollResult constructor sets is_nat_one and miss_margin
            // both \u2014 the detector must classify nat_1 first and skip
            // the miss_by_N branch (mutual exclusion).
            var roll = new RollResult(1, null, 1, StatType.Charm, 2, 0, 13, FailureTier.Catastrophe);
            var result = MakeResult(roll: roll);
            var kinds = TurnEventDetector.DetectEventKinds(result);
            Assert.Equal(new[] { "nat_1" }, kinds);
            Assert.DoesNotContain(kinds, k => k.StartsWith("miss_by_"));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(7)]
        public void Detector_MissByN_EmitsExactMargin(int margin)
        {
            // Miss_by_<exact margin>. Catalog ships entries for 1/3/5
            // but the detector emits the exact margin regardless \u2014
            // forward compat with future fine-grained variants.
            int dieRoll = 13 - margin;
            var roll = new RollResult(dieRoll, null, dieRoll, StatType.Charm, 0, 0, 13, FailureTier.Misfire);
            // RollResult computes IsSuccess + MissMargin internally.
            Assert.False(roll.IsSuccess);
            var result = MakeResult(roll: roll);
            var kinds = TurnEventDetector.DetectEventKinds(result);
            Assert.Equal(new[] { "miss_by_" + margin }, kinds);
        }

        [Fact]
        public void Detector_ComboTellCallback_AllThreeEmit()
        {
            // A turn that triggered a combo, read a tell, and applied
            // a callback bonus emits all three kinds in canonical order
            // alongside the roll's nat_20.
            var roll = new RollResult(20, null, 20, StatType.Charm, 2, 0, 13, FailureTier.Success);
            var result = MakeResult(
                roll: roll,
                comboTriggered: "ice-breaker",
                tellReadBonus: 2,
                callbackBonusApplied: 3);
            var kinds = TurnEventDetector.DetectEventKinds(result);
            Assert.Equal(new[]
            {
                "nat_20",
                "combo_hit",
                "tell_read",
                "callback_hit",
            }, kinds);
        }

        [Fact]
        public void Detector_HorninessFail_EmitsOnlyWhenOverlayApplied()
        {
            // A horniness check that missed but produced no overlay
            // (e.g. no shadows present) is not the player-visible
            // event the yaml framing covers \u2014 the framing is "desire
            // overran your read", which only makes sense when the
            // overlay actually corrupted the message.
            var roll = MakeSuccessRoll();
            var hornMissNoOverlay = new HorninessCheckResult(
                roll: 5, modifier: 1, total: 6, dc: 12,
                isMiss: true, tier: FailureTier.Misfire,
                overlayApplied: false);
            var hornMissOverlay = new HorninessCheckResult(
                roll: 5, modifier: 1, total: 6, dc: 12,
                isMiss: true, tier: FailureTier.Misfire,
                overlayApplied: true);

            Assert.Empty(TurnEventDetector.DetectEventKinds(
                MakeResult(roll: roll, horniness: hornMissNoOverlay)));
            Assert.Equal(new[] { "horniness_fail" },
                TurnEventDetector.DetectEventKinds(MakeResult(roll: roll, horniness: hornMissOverlay)));
        }

        [Fact]
        public void Detector_MultipleShadowTicks_EachEmitOwnKind()
        {
            // Two shadows can grow on the same turn (e.g. a Charm Nat 1
            // grows Madness AND triggers a Despair tick). Each emits
            // its own shadow_tick_<name> kind.
            var roll = MakeSuccessRoll();
            var result = MakeResult(
                roll: roll,
                shadowGrowth: new[]
                {
                    "Madness +1 (Charm Nat 1)",
                    "Despair +2 (RIZZ TropeTrap failure)",
                });
            var kinds = TurnEventDetector.DetectEventKinds(result);
            Assert.Equal(new[]
            {
                "shadow_tick_madness",
                "shadow_tick_despair",
            }, kinds);
        }

        [Fact]
        public void Detector_TrapActivation_EmitsTrapActivated()
        {
            var trap = new Pinder.Core.Traps.TrapDefinition(
                id: "cringe",
                stat: StatType.Honesty,
                effect: Pinder.Core.Traps.TrapEffect.Disadvantage,
                effectValue: 0,
                durationTurns: 3,
                llmInstruction: "",
                clearMethod: "Pick an Honesty option.",
                nat1Bonus: "",
                displayName: "Cringe",
                summary: "You're aware of how you're coming across.");
            var roll = new RollResult(8, null, 8, StatType.Charm, 0, 0, 13, FailureTier.Misfire,
                activatedTrap: trap);
            var result = MakeResult(roll: roll);
            var kinds = TurnEventDetector.DetectEventKinds(result);
            // miss_by_5 (DC 13, total 8) AND trap_activated; ordering:
            // roll-class first, trap last.
            Assert.Equal(new[] { "miss_by_5", "trap_activated" }, kinds);
        }

        [Theory]
        [InlineData("Madness +1 (Charm Nat 1)", "Madness")]
        [InlineData("Despair +2 (RIZZ TropeTrap failure)", "Despair")]
        [InlineData("Overthinking +1 (3 SA in a row)", "Overthinking")]
        public void ParseShadowName_HappyPaths(string growth, string expected)
        {
            Assert.Equal(expected, TurnEventDetector.ParseShadowName(growth));
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("madness +1 (lowercase)")]   // lower-case still letters; OK
        [InlineData("Madness")]                  // no space
        [InlineData("Madness 1")]                // no '+'
        [InlineData("3 short reads")]            // leading digits
        public void ParseShadowName_DefensiveFormatDrift(string growth)
        {
            // The lowercase entry IS valid (all letters), so it
            // round-trips. We assert via a separate path:
            if (growth == "madness +1 (lowercase)")
            {
                Assert.Equal("madness", TurnEventDetector.ParseShadowName(growth));
            }
            else
            {
                Assert.Null(TurnEventDetector.ParseShadowName(growth));
            }
        }

        // ── BuildTurnSnapshot integration ────────────────────────────────

        [Fact]
        public void BuildTurnSnapshot_PopulatesEventsArray_OnEveryTurn()
        {
            // Schema discipline: every TurnSnapshot built going forward
            // carries the Events array (possibly empty) so replay
            // tooling can rely on it.
            var roll = new RollResult(20, null, 20, StatType.Charm, 2, 0, 13, FailureTier.Success);
            var result = new TurnResult(
                roll: roll, deliveredMessage: "msg", opponentMessage: "...",
                narrativeBeat: null, interestDelta: 1,
                stateAfter: new GameStateSnapshot(
                    10, InterestState.Interested, 0, Array.Empty<string>(), 1),
                isGameOver: false, outcome: null,
                shadowGrowthEvents: Array.Empty<string>(),
                comboTriggered: "ice-breaker");

            var conversationHistory = new List<(string Sender, string Text)>
            {
                ("P1", "p1 turn 1"), ("P2", "p2 turn 1"),
            };

            var snap = global::Program.BuildTurnSnapshot(
                turnNumber: 1,
                result: result,
                shadows: new SessionShadowTracker(MakeStats()),
                statsUsedHistory: new List<StatType>(),
                highestPctHistory: new List<bool>(),
                charmUsageCount: 0,
                charmMadnessTriggered: false,
                saUsageCount: 0,
                saOverthinkingTriggered: false,
                rizzCumulativeFailureCount: 0,
                conversationHistory: conversationHistory,
                comboHistory: new List<(StatType Stat, bool Succeeded)>(),
                activeTell: null,
                perTurnTextDiffs: null,
                opponentHistory: null,
                playerSender: "P1",
                i18nCatalog: null);

            // Events list is non-null and contains exactly the kinds
            // the detector reports. The interpretation is empty because
            // we passed null catalog \u2014 the writer degraded gracefully
            // (same path the simulator takes when data/i18n is
            // unavailable).
            Assert.NotNull(snap.Events);
            Assert.Equal(2, snap.Events.Count);
            Assert.Equal("nat_20", snap.Events[0].Kind);
            Assert.Equal(1, snap.Events[0].TurnNumber);
            Assert.Equal(string.Empty, snap.Events[0].EventInterpretation);
            Assert.Equal("combo_hit", snap.Events[1].Kind);
        }

        [Fact]
        public void BuildTurnSnapshot_WithI18nCatalog_PopulatesInterpretationStrings()
        {
            // End-to-end: load the real seeded events.yaml, pass it to
            // BuildTurnSnapshot, verify each event has a non-empty
            // interpretation drawn deterministically from the variant
            // catalog.
            var catalog = LoadCatalog();
            var roll = new RollResult(20, null, 20, StatType.Charm, 2, 0, 13, FailureTier.Success);
            var result = new TurnResult(
                roll: roll, deliveredMessage: "msg", opponentMessage: "...",
                narrativeBeat: null, interestDelta: 1,
                stateAfter: new GameStateSnapshot(
                    10, InterestState.Interested, 0, Array.Empty<string>(), 7),
                isGameOver: false, outcome: null,
                shadowGrowthEvents: Array.Empty<string>(),
                comboTriggered: "ice-breaker");

            var snap = global::Program.BuildTurnSnapshot(
                turnNumber: 7,
                result: result,
                shadows: new SessionShadowTracker(MakeStats()),
                statsUsedHistory: new List<StatType>(),
                highestPctHistory: new List<bool>(),
                charmUsageCount: 0,
                charmMadnessTriggered: false,
                saUsageCount: 0,
                saOverthinkingTriggered: false,
                rizzCumulativeFailureCount: 0,
                conversationHistory: new List<(string Sender, string Text)> { ("P1","x"), ("P2","y") },
                comboHistory: new List<(StatType Stat, bool Succeeded)>(),
                activeTell: null,
                perTurnTextDiffs: null,
                opponentHistory: null,
                playerSender: "P1",
                i18nCatalog: catalog);

            Assert.Equal(2, snap.Events.Count);
            Assert.False(string.IsNullOrEmpty(snap.Events[0].EventInterpretation),
                "nat_20 must have a non-empty interpretation from events.yaml");
            Assert.False(string.IsNullOrEmpty(snap.Events[1].EventInterpretation),
                "combo_hit must have a non-empty interpretation from events.yaml");

            // Determinism: re-pick at the same turn yields the same
            // string \u2014 critical for cross-language parity with
            // frontend variantIndex.
            var snap2 = global::Program.BuildTurnSnapshot(
                turnNumber: 7,
                result: result,
                shadows: new SessionShadowTracker(MakeStats()),
                statsUsedHistory: new List<StatType>(),
                highestPctHistory: new List<bool>(),
                charmUsageCount: 0,
                charmMadnessTriggered: false,
                saUsageCount: 0,
                saOverthinkingTriggered: false,
                rizzCumulativeFailureCount: 0,
                conversationHistory: new List<(string Sender, string Text)> { ("P1","x"), ("P2","y") },
                comboHistory: new List<(StatType Stat, bool Succeeded)>(),
                activeTell: null,
                perTurnTextDiffs: null,
                opponentHistory: null,
                playerSender: "P1",
                i18nCatalog: catalog);
            Assert.Equal(snap.Events[0].EventInterpretation, snap2.Events[0].EventInterpretation);
            Assert.Equal(snap.Events[1].EventInterpretation, snap2.Events[1].EventInterpretation);
        }

        [Fact]
        public void OnDiskSnapshot_SerializesEventsArrayWithCanonicalShape()
        {
            // Issue #474 acceptance criterion: "run a sim, inspect a
            // .turn-NN.snap.json file, confirm the Events array is
            // present and each entry has a non-empty
            // EventInterpretation". This test simulates the exact
            // serialization path session-runner takes (System.Text.Json
            // with WriteIndented = true) and asserts the disk shape is
            // what replay tooling will see.
            var catalog = LoadCatalog();
            var roll = new RollResult(20, null, 20, StatType.Charm, 2, 0, 13, FailureTier.Success);
            var result = new TurnResult(
                roll: roll, deliveredMessage: "msg", opponentMessage: "...",
                narrativeBeat: null, interestDelta: 1,
                stateAfter: new GameStateSnapshot(
                    10, InterestState.Interested, 0, Array.Empty<string>(), 4),
                isGameOver: false, outcome: null,
                shadowGrowthEvents: new[] { "Madness +1 (Charm Nat 1)" },
                comboTriggered: "ice-breaker",
                tellReadBonus: 2);

            var snap = global::Program.BuildTurnSnapshot(
                turnNumber: 4,
                result: result,
                shadows: new SessionShadowTracker(MakeStats()),
                statsUsedHistory: new List<StatType>(),
                highestPctHistory: new List<bool>(),
                charmUsageCount: 0,
                charmMadnessTriggered: false,
                saUsageCount: 0,
                saOverthinkingTriggered: false,
                rizzCumulativeFailureCount: 0,
                conversationHistory: new List<(string Sender, string Text)> { ("P1","x"), ("P2","y") },
                comboHistory: new List<(StatType Stat, bool Succeeded)>(),
                activeTell: null,
                perTurnTextDiffs: null,
                opponentHistory: null,
                playerSender: "P1",
                i18nCatalog: catalog);

            // Match the simulator's serializer settings.
            string json = System.Text.Json.JsonSerializer.Serialize(snap,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            // The on-disk JSON contains the Events array with the
            // canonical key names (PascalCase by default — the
            // simulator doesn't pin a JsonNamingPolicy on this path,
            // so the shape matches what replay tools today already
            // parse off the disk for the rest of the snapshot).
            Assert.Contains("\"Events\":", json);
            Assert.Contains("\"Kind\":", json);
            Assert.Contains("\"TurnNumber\":", json);
            Assert.Contains("\"EventInterpretation\":", json);

            // Each of the three detected kinds (nat_20, combo_hit,
            // tell_read, shadow_tick_madness) appears in the
            // serialized output with a non-empty interpretation.
            Assert.Contains("\"nat_20\"", json);
            Assert.Contains("\"combo_hit\"", json);
            Assert.Contains("\"tell_read\"", json);
            Assert.Contains("\"shadow_tick_madness\"", json);

            // No empty interpretation strings on the live catalog
            // path — a missing variant set is a yaml gap that should
            // surface (every kind we detect has yaml entries today).
            foreach (var ev in snap.Events)
            {
                Assert.False(string.IsNullOrEmpty(ev.EventInterpretation),
                    $"event {ev.Kind} has empty interpretation — events.yaml is missing the kind");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────

        private static StatBlock MakeStats() => new StatBlock(
            new Dictionary<StatType, int>
            {
                { StatType.Charm, 3 }, { StatType.Rizz, 2 },
                { StatType.Honesty, 1 }, { StatType.Chaos, 0 },
                { StatType.Wit, 4 }, { StatType.SelfAwareness, 2 },
            },
            new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 },
            });

        private static RollResult MakeSuccessRoll() =>
            new RollResult(15, null, 15, StatType.Charm, 2, 0, 13, FailureTier.Success);

        private static TurnResult MakeResult(
            RollResult? roll = null,
            string? comboTriggered = null,
            int tellReadBonus = 0,
            int callbackBonusApplied = 0,
            HorninessCheckResult? horniness = null,
            IReadOnlyList<string>? shadowGrowth = null)
        {
            return new TurnResult(
                roll: roll ?? MakeSuccessRoll(),
                deliveredMessage: "msg",
                opponentMessage: "...",
                narrativeBeat: null,
                interestDelta: 1,
                stateAfter: new GameStateSnapshot(
                    10, InterestState.Interested, 0, Array.Empty<string>(), 1),
                isGameOver: false,
                outcome: null,
                shadowGrowthEvents: shadowGrowth,
                comboTriggered: comboTriggered,
                callbackBonusApplied: callbackBonusApplied,
                tellReadBonus: tellReadBonus,
                horninessCheck: horniness);
        }

        private static Pinder.LlmAdapters.I18nCatalog LoadCatalog()
        {
            // Walk up from the current directory looking for data/i18n.
            // Mirrors the I18nCatalogTests pattern (works in normal
            // checkouts AND inside git worktrees \u2014 no .git probe).
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                string candidate = System.IO.Path.Combine(dir, "data", "i18n");
                if (System.IO.Directory.Exists(candidate))
                {
                    return Pinder.LlmAdapters.I18nCatalog.LoadFromDirectory(candidate, "en");
                }
                var parent = System.IO.Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new System.IO.DirectoryNotFoundException(
                "could not find data/i18n above test base dir");
        }
    }
}

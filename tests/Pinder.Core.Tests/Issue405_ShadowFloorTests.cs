using System.Collections.Generic;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for issue #405 — shadow stats must be floored at 0.
    ///
    /// Pre-fix bug: <see cref="SessionShadowTracker.ApplyOffset"/> stacked arbitrary signed
    /// deltas with no clamp, and <see cref="SessionShadowTracker.GetEffectiveShadow"/> read
    /// the raw sum. Stack enough −1 reductions on a low-base shadow and the effective value
    /// dropped below 0. <see cref="SessionShadowTracker.GetEffectiveStat"/> uses
    /// <c>floor(shadow / 3)</c>; for negative shadows that floor produces negative penalties,
    /// which then *increased* the paired positive stat — silently buffing characters.
    ///
    /// Fix: floor at 0 at three layers (read, write, restore) for defense in depth, and mark
    /// floored ApplyOffset events with a "(floored)" suffix so the audit log is honest.
    /// </summary>
    [Trait("Category", "Core")]
    [Trait("Issue", "405")]
    public class Issue405_ShadowFloorTests
    {
        // =====================================================================
        // Test 1 — Reductions cannot drive shadow below 0
        // =====================================================================

        [Fact]
        public void T1_ReductionsAtZeroFloor_EffectiveStaysAtZero()
        {
            // Setup: character with madness: 0. Apply -1 three times.
            // Expectation: GetEffectiveShadow(Madness) == 0 after each call.
            var tracker = new SessionShadowTracker(MakeStats(madness: 0));

            tracker.ApplyOffset(ShadowStatType.Madness, -1, "combo: The Read");
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));

            tracker.ApplyOffset(ShadowStatType.Madness, -1, "Tell option selected");
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));

            tracker.ApplyOffset(ShadowStatType.Madness, -1, "Nat 20 reward");
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));

            // Pre-fix: stored delta would be -3 here; effective would read as -3.
            Assert.True(tracker.GetDelta(ShadowStatType.Madness) >= 0,
                "stored delta must not drive base+delta below 0");
        }

        // =====================================================================
        // Test 2 — Net reduction below 0 floors at 0 with audit log honesty
        // =====================================================================

        [Fact]
        public void T2_NetReductionBelowZero_FloorsAtZero_WithFlooredAuditEvents()
        {
            // Setup: character with madness: 1. Apply -1 four times.
            // Expectation: effective madness = 0 (not -3). Audit shows one real reduction
            // plus three floored events using the "(floored)" marker convention.
            var tracker = new SessionShadowTracker(MakeStats(madness: 1));

            tracker.ApplyOffset(ShadowStatType.Madness, -1, "first");   // 1 → 0  (real)
            tracker.ApplyOffset(ShadowStatType.Madness, -1, "second");  // 0 → 0  (floored)
            tracker.ApplyOffset(ShadowStatType.Madness, -1, "third");   // 0 → 0  (floored)
            tracker.ApplyOffset(ShadowStatType.Madness, -1, "fourth");  // 0 → 0  (floored)

            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));

            var events = tracker.DrainGrowthEvents();
            Assert.Equal(4, events.Count);

            // First event is a real -1 reduction (no floored marker).
            Assert.Contains("first", events[0]);
            Assert.DoesNotContain("(floored)", events[0]);
            Assert.Contains("Madness -1", events[0]);

            // The remaining three are floored — convention: "(floored)" suffix on the event.
            int flooredCount = 0;
            for (int i = 1; i < events.Count; i++)
            {
                Assert.Contains("(floored)", events[i]);
                flooredCount++;
            }
            Assert.Equal(3, flooredCount);
        }

        // =====================================================================
        // Test 3 — Negative shadow does NOT buff paired positive stat
        // =====================================================================

        [Fact]
        public void T3_FlooredShadow_DoesNotBuffPairedStat()
        {
            // Setup: madness: 0 (paired with Charm), charm: 5 base. Apply -2 Madness.
            // Expectation: GetEffectiveStat(Charm) == 5 (not 6).
            // Pre-fix: floor(-2 / 3) == -1 (C# integer division) → 5 - (-1) == 6. Bug.
            var tracker = new SessionShadowTracker(MakeStats(charm: 5, madness: 0));

            tracker.ApplyOffset(ShadowStatType.Madness, -2, "two reductions stacked");

            // Effective Madness floored at 0.
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));
            // Paired Charm stat is NOT buffed — stays at 5.
            Assert.Equal(5, tracker.GetEffectiveStat(StatType.Charm));
        }

        // Adjacent: even if a future code path stores a negative delta directly, the read-time
        // clamp must ensure GetEffectiveStat does not buff the paired stat. This guards against
        // regression if the write-time clamp is ever bypassed.
        [Fact]
        public void T3b_ReadTimeClamp_DefendsAgainstHypotheticalNegativeDelta_OnPairedStat()
        {
            // Even with a base shadow of 1 reduced by a request of -2 (delta would be -2 if
            // unclamped), the paired stat read goes through the floored path — never buffed.
            var tracker = new SessionShadowTracker(MakeStats(charm: 5, madness: 1));

            tracker.ApplyOffset(ShadowStatType.Madness, -5, "huge reduction request");

            // Effective Madness floored at 0; paired Charm still 5 (not 5 + something).
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));
            Assert.Equal(5, tracker.GetEffectiveStat(StatType.Charm));
        }

        // =====================================================================
        // Test 4 — Restore from snapshot clamps too
        // =====================================================================

        [Fact]
        public void T4_RestoreFromSnapshot_NegativeTargetValue_ClampedToZero()
        {
            // Synthesize a snapshot with a deliberately negative effective value (simulating
            // an old / corrupt snapshot recorded before the floor was enforced).
            var tracker = new SessionShadowTracker(MakeStats(madness: 2));

            var corruptSnapshot = new Dictionary<string, int>
            {
                { ShadowStatType.Madness.ToString(), -3 },     // corrupt: was negative
                { ShadowStatType.Despair.ToString(), 4 },      // legitimate
                { ShadowStatType.Denial.ToString(), -1 },      // corrupt: was negative
            };

            tracker.RestoreFromSnapshot(corruptSnapshot);

            // Negative targets clamp to 0, not below.
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Madness));
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Denial));
            // Legitimate positive values pass through unchanged.
            Assert.Equal(4, tracker.GetEffectiveShadow(ShadowStatType.Despair));
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static StatBlock MakeStats(
            int charm = 0, int rizz = 0, int honesty = 0,
            int chaos = 0, int wit = 0, int sa = 0,
            int madness = 0, int despair = 0, int denial = 0,
            int fixation = 0, int dread = 0, int overthinking = 0)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, madness }, { ShadowStatType.Despair, despair },
                    { ShadowStatType.Denial, denial }, { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread }, { ShadowStatType.Overthinking, overthinking }
                });
        }
    }
}

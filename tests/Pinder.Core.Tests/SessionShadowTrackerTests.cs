using System;
using System.Collections.Generic;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class SessionShadowTrackerTests
    {
        private static StatBlock MakeStats(
            int charm = 3, int rizz = 2, int honesty = 1, int chaos = 0, int wit = 4, int sa = 2,
            int madness = 2, int horniness = 0, int denial = 0, int fixation = 0, int dread = 5, int overthinking = 1)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm },
                    { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos },
                    { StatType.Wit, wit },
                    { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, madness },
                    { ShadowStatType.Despair, horniness },
                    { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation },
                    { ShadowStatType.Dread, dread },
                    { ShadowStatType.Overthinking, overthinking }
                });
        }

        [Fact]
        public void Constructor_NullBaseStats_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionShadowTracker(null!));
        }

        [Fact]
        public void GetEffectiveShadow_NoGrowth_ReturnsBaseValue()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            Assert.Equal(2, tracker.GetEffectiveShadow(ShadowStatType.Madness));
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Despair));
            Assert.Equal(5, tracker.GetEffectiveShadow(ShadowStatType.Dread));
        }

        [Fact]
        public void GetDelta_NoGrowth_ReturnsZero()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Madness));
        }

        [Fact]
        public void GetEffectiveStat_NoGrowth_MatchesSpecExamples()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            // Charm(3) - floor(Madness(2)/3) = 3 - 0 = 3
            Assert.Equal(3, tracker.GetEffectiveStat(StatType.Charm));
            // Wit(4) - floor(Dread(5)/3) = 4 - 1 = 3
            Assert.Equal(3, tracker.GetEffectiveStat(StatType.Wit));
        }

        [Fact]
        public void ApplyGrowth_ReturnsFormattedDescription()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            string result = tracker.ApplyGrowth(ShadowStatType.Madness, 1, "Charm fail");
            Assert.Equal("Madness +1 (Charm fail)", result);
        }

        [Fact]
        public void ApplyGrowth_ZeroAmount_Throws()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tracker.ApplyGrowth(ShadowStatType.Madness, 0, "nope"));
        }

        [Fact]
        public void ApplyGrowth_NegativeAmount_Throws()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tracker.ApplyGrowth(ShadowStatType.Madness, -1, "nope"));
        }

        [Fact]
        public void ApplyGrowth_UpdatesShadowAndStat()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "Charm fail");

            Assert.Equal(3, tracker.GetEffectiveShadow(ShadowStatType.Madness)); // 2 + 1
            Assert.Equal(1, tracker.GetDelta(ShadowStatType.Madness));
            // Charm(3) - floor(3/3) = 3 - 1 = 2
            Assert.Equal(2, tracker.GetEffectiveStat(StatType.Charm));
        }

        [Fact]
        public void ApplyGrowth_Accumulates()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "a");
            tracker.ApplyGrowth(ShadowStatType.Madness, 2, "b");
            Assert.Equal(3, tracker.GetDelta(ShadowStatType.Madness));
            Assert.Equal(5, tracker.GetEffectiveShadow(ShadowStatType.Madness)); // 2 + 3
        }

        [Fact]
        public void ApplyGrowth_DreadGrowth_AffectsWit()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            tracker.ApplyGrowth(ShadowStatType.Dread, 1, "combo trigger");

            Assert.Equal(6, tracker.GetEffectiveShadow(ShadowStatType.Dread)); // 5 + 1
            // Wit(4) - floor(6/3) = 4 - 2 = 2
            Assert.Equal(2, tracker.GetEffectiveStat(StatType.Wit));
        }

        [Fact]
        public void DrainGrowthEvents_ReturnsPendingEvents()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "Charm fail");
            tracker.ApplyGrowth(ShadowStatType.Dread, 1, "combo trigger");

            var events = tracker.DrainGrowthEvents();
            Assert.Equal(2, events.Count);
            Assert.Equal("Madness +1 (Charm fail)", events[0]);
            Assert.Equal("Dread +1 (combo trigger)", events[1]);
        }

        [Fact]
        public void DrainGrowthEvents_ClearsAfterDrain()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "test");
            tracker.DrainGrowthEvents();

            var second = tracker.DrainGrowthEvents();
            Assert.Empty(second);
        }

        [Fact]
        public void DrainGrowthEvents_EmptyWhenNoGrowth()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            Assert.Empty(tracker.DrainGrowthEvents());
        }

        [Fact]
        public void ShadowTypes_AreIndependent()
        {
            var tracker = new SessionShadowTracker(MakeStats());
            tracker.ApplyGrowth(ShadowStatType.Madness, 5, "test");
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Despair));
            Assert.Equal(0, tracker.GetDelta(ShadowStatType.Dread));
        }

        [Fact]
        public void GetEffectiveStat_NegativeResult_IsValid()
        {
            // Chaos=0, Fixation base=0, grow Fixation by 3 => effective = 0 - floor(3/3) = -1
            var tracker = new SessionShadowTracker(MakeStats(chaos: 0, fixation: 0));
            tracker.ApplyGrowth(ShadowStatType.Fixation, 3, "test");
            Assert.Equal(-1, tracker.GetEffectiveStat(StatType.Chaos));
        }

        [Fact]
        public void GetEffectiveStat_ZeroShadow_NoPenalty()
        {
            var tracker = new SessionShadowTracker(MakeStats(horniness: 0));
            // Rizz(2) - floor(0/3) = 2
            Assert.Equal(2, tracker.GetEffectiveStat(StatType.Rizz));
        }

        [Fact]
        public void GetEffectiveStat_BoundaryPenalty()
        {
            // Madness base=2, delta=1 => effective shadow=3 => floor(3/3)=1
            var tracker = new SessionShadowTracker(MakeStats(madness: 2));
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "boundary");
            Assert.Equal(2, tracker.GetEffectiveStat(StatType.Charm)); // 3 - 1 = 2
        }

        [Fact]
        public void LargeAccumulatedDelta()
        {
            var tracker = new SessionShadowTracker(MakeStats(madness: 0));
            for (int i = 0; i < 10; i++)
                tracker.ApplyGrowth(ShadowStatType.Madness, 1, $"growth {i}");

            Assert.Equal(10, tracker.GetDelta(ShadowStatType.Madness));
            Assert.Equal(10, tracker.GetEffectiveShadow(ShadowStatType.Madness));

            var events = tracker.DrainGrowthEvents();
            Assert.Equal(10, events.Count);
        }
    }
}

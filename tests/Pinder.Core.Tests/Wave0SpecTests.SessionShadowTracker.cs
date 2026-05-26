using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Wave0SpecTests
    {
        // ==================================================================
        // AC1: SessionShadowTracker — all shadow/stat pairs
        // ==================================================================

        // Mutation: Fails if Rizz uses wrong paired shadow (not Despair)
        [Fact]
        public void SessionShadowTracker_RizzPairedWithDespair()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock(rizz: 4, horniness: 6));
            // Rizz(4) - floor(Despair(6) / 3) = 4 - 2 = 2
            Assert.Equal(2, tracker.GetEffectiveStat(StatType.Rizz));
        }

        // Mutation: Fails if Honesty uses wrong paired shadow (not Denial)
        [Fact]
        public void SessionShadowTracker_HonestyPairedWithDenial()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock(honesty: 3, denial: 9));
            // Honesty(3) - floor(Denial(9) / 3) = 3 - 3 = 0
            Assert.Equal(0, tracker.GetEffectiveStat(StatType.Honesty));
        }

        // Mutation: Fails if Chaos uses wrong paired shadow (not Fixation)
        [Fact]
        public void SessionShadowTracker_ChaosPairedWithFixation()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock(chaos: 2, fixation: 3));
            // Chaos(2) - floor(Fixation(3) / 3) = 2 - 1 = 1
            Assert.Equal(1, tracker.GetEffectiveStat(StatType.Chaos));
        }

        // Mutation: Fails if SelfAwareness uses wrong paired shadow (not Overthinking)
        [Fact]
        public void SessionShadowTracker_SelfAwarenessPairedWithOverthinking()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock(sa: 3, overthinking: 6));
            // SA(3) - floor(Overthinking(6) / 3) = 3 - 2 = 1
            Assert.Equal(1, tracker.GetEffectiveStat(StatType.SelfAwareness));
        }

        // Mutation: Fails if GetEffectiveStat ignores session delta (only uses base shadow)
        [Fact]
        public void SessionShadowTracker_GetEffectiveStat_IncludesSessionDelta()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock(charm: 5, madness: 0));
            // Before growth: 5 - floor(0/3) = 5
            Assert.Equal(5, tracker.GetEffectiveStat(StatType.Charm));

            tracker.ApplyGrowth(ShadowStatType.Madness, 3, "test");
            // After growth: 5 - floor(3/3) = 5 - 1 = 4
            Assert.Equal(4, tracker.GetEffectiveStat(StatType.Charm));
        }

        // Mutation: Fails if DrainGrowthEvents doesn't preserve insertion order
        [Fact]
        public void SessionShadowTracker_DrainGrowthEvents_PreservesOrder()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock());
            tracker.ApplyGrowth(ShadowStatType.Dread, 2, "first");
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "second");
            tracker.ApplyGrowth(ShadowStatType.Despair, 3, "third");

            var events = tracker.DrainGrowthEvents();
            Assert.Equal(3, events.Count);
            Assert.Equal("Dread +2 (first)", events[0]);
            Assert.Equal("Madness +1 (second)", events[1]);
            Assert.Equal("Despair +3 (third)", events[2]);
        }

        // Mutation: Fails if multiple growths to same shadow lose individual descriptions
        [Fact]
        public void SessionShadowTracker_MultipleGrowthsSameShadow_AllCapturedInDrain()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock(madness: 0));
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "first fail");
            tracker.ApplyGrowth(ShadowStatType.Madness, 2, "second fail");

            var events = tracker.DrainGrowthEvents();
            Assert.Equal(2, events.Count);
            Assert.Equal("Madness +1 (first fail)", events[0]);
            Assert.Equal("Madness +2 (second fail)", events[1]);
        }

        // Mutation: Fails if drain doesn't actually clear — new events after drain appear in next drain
        [Fact]
        public void SessionShadowTracker_EventsAfterDrain_AppearInNextDrain()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock());
            tracker.ApplyGrowth(ShadowStatType.Madness, 1, "before drain");
            tracker.DrainGrowthEvents(); // clear

            tracker.ApplyGrowth(ShadowStatType.Dread, 1, "after drain");
            var events = tracker.DrainGrowthEvents();
            Assert.Single(events);
            Assert.Equal("Dread +1 (after drain)", events[0]);
        }

        // Mutation: Fails if ApplyGrowth description uses wrong format (e.g., missing parens)
        [Fact]
        public void SessionShadowTracker_ApplyGrowth_DescriptionFormat()
        {
            var tracker = new SessionShadowTracker(MakeStatBlock());
            var desc = tracker.ApplyGrowth(ShadowStatType.Despair, 5, "rizz crit");
            Assert.Equal("Despair +5 (rizz crit)", desc);
        }
    }
}

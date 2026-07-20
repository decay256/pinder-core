using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ShadowGrowthEventTests
    {
        // ======================== Omitted tracker → default tracker events ========================

        [Fact]
        public async Task OmittedShadowTracker_RecordsGrowthEvents()
        {
            var dice = new QueueDice(new[] { 1, 50 }); // Nat 1, d100=50
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 0),
                shadows: null);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Madness"));
            Assert.NotNull(session.State.PlayerShadows);
            Assert.Equal(1, session.State.PlayerShadows!.GetDelta(ShadowStatType.Madness));
        }

        // ======================== Multiple triggers in one turn ========================

        [Fact]
        public async Task Nat1OnWit_WithCatastrophe_BothDreadTriggers()
        {
            // Nat 1 on Wit gives Legendary tier (not Catastrophe), so only Nat 1 trigger fires
            var shadows = MakeShadowTracker();
            var dice = new QueueDice(new[] { 1, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(wit: 0),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(2); // Wit

            // Nat 1 = Legendary tier, NOT Catastrophe. So only Nat 1 trigger fires.
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Nat 1 on Wit"));
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Catastrophic Wit failure"));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Dread));
        }

        // ======================== SessionShadowTracker.ApplyOffset ========================

        [Fact]
        public void ApplyOffset_NegativeDelta_Works()
        {
            var tracker = MakeShadowTracker();
            tracker.ApplyGrowth(ShadowStatType.Fixation, 2, "test growth");
            tracker.ApplyOffset(ShadowStatType.Fixation, -1, "offset");

            Assert.Equal(1, tracker.GetDelta(ShadowStatType.Fixation));
        }

        [Fact]
        public void ApplyOffset_PositiveDelta_Works()
        {
            var tracker = MakeShadowTracker();
            tracker.ApplyOffset(ShadowStatType.Fixation, 3, "test");

            Assert.Equal(3, tracker.GetDelta(ShadowStatType.Fixation));
        }

        [Fact]
        public void ApplyOffset_AddsEvent()
        {
            // (#405 update) Pre-fix this asserted the description contained "-1" even when
            // the base+delta would be negative — silent state corruption. With the floor at 0,
            // a -1 reduction on Fixation=0 is fully floored: applied delta = 0,
            // description = "Fixation +0 (test offset) (floored)".
            var tracker = MakeShadowTracker();
            string desc = tracker.ApplyOffset(ShadowStatType.Fixation, -1, "test offset");

            // The event is still recorded for audit honesty.
            var events = tracker.DrainGrowthEvents();
            Assert.Single(events);
            // Effective shadow stays at 0 (floor enforced).
            Assert.Equal(0, tracker.GetEffectiveShadow(ShadowStatType.Fixation));
            // Description carries the (floored) marker.
            Assert.Contains("(floored)", desc);
            Assert.Contains("test offset", desc);
        }

        [Fact]
        public void ApplyOffset_NegativeWithinFloor_AddsRealReductionEvent()
        {
            // Counterpart to ApplyOffset_AddsEvent: when there IS positive shadow to reduce,
            // the event is a real reduction (no floored marker) and the description
            // contains the actual signed delta string.
            var tracker = MakeShadowTracker();
            tracker.ApplyGrowth(ShadowStatType.Fixation, 2, "setup");
            tracker.DrainGrowthEvents();

            string desc = tracker.ApplyOffset(ShadowStatType.Fixation, -1, "real reduction");

            Assert.Contains("-1", desc);
            Assert.DoesNotContain("(floored)", desc);
            Assert.Equal(1, tracker.GetEffectiveShadow(ShadowStatType.Fixation));
        }

        // ======================== GameEndedException.ShadowGrowthEvents ========================

        [Fact]
        public void GameEndedException_DefaultConstructor_EmptyShadowEvents()
        {
            var ex = new GameEndedException(GameOutcome.Ghosted);
            Assert.Empty(ex.ShadowGrowthEvents);
        }

        [Fact]
        public void GameEndedException_WithEvents_HasShadowEvents()
        {
            var events = new List<string> { "Dread +1 (Ghosted)" };
            var ex = new GameEndedException(GameOutcome.Ghosted, events);
            Assert.Single(ex.ShadowGrowthEvents);
            Assert.Contains("Dread +1 (Ghosted)", ex.ShadowGrowthEvents);
        }
    }
}

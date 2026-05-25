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
    public partial class ShadowReductionSpecTests
    {
        // =====================================================================
        // AC-4: Success with Overthinking Disadvantage → Overthinking −1
        // =====================================================================

        // What: AC-4 (updated for #755) — T2 disadvantage removed, Overthinking reduction via disadvantage no longer fires.
        // The shadow check mechanic (not roll disadvantage) is now the T2 effect.
        [Fact]
        public async Task AC4_SuccessWithOverthinkingAtT2_NoLongerReducesOverthinking_ViaDis()
        {
            // #755: T2 no longer causes roll disadvantage, so the "succeeded despite disadvantage" reduction is gone.
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50), // single roll (no disadvantage)
                playerStats: MakeStats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "insightful") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // #755: T2 disadvantage removed — Overthinking delta unchanged (reduction doesn't fire via old T2 path)
            // (may still reduce via high-interest mechanic if interest ≥20)
            Assert.True(shadows.GetDelta(ShadowStatType.Overthinking) >= 12); // no reduction from T2 disadvantage path
        }

        // What: AC-4 (updated for #755) — Growth event for overthinking-via-disadvantage no longer fires.
        [Fact]
        public async Task AC4_SuccessWithOverthinkingAtT2_NoDisadvantageEvent()
        {
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // #755: T2 disadvantage path removed — "Succeeded despite" event should NOT appear
            Assert.DoesNotContain(result.ShadowGrowthEvents,
                e => e.Contains("Overthinking") && e.Contains("Succeeded despite"));
        }

        // What: AC-4 negative — Failure with Overthinking disadvantage does NOT reduce
        // Mutation: Would catch if IsSuccess check is missing
        [Fact]
        public async Task AC4_FailureWithOverthinkingDisadvantage_NoReduction()
        {
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(2, 3, 50), // low rolls → failure
                playerStats: MakeStats(sa: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Mutation: Fails if reduction fires regardless of success
            Assert.Equal(12, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // What: AC-4 negative — Success WITHOUT Overthinking disadvantage does NOT reduce
        // Mutation: Would catch if disadvantage check is missing
        [Fact]
        public async Task AC4_SuccessWithSA_NoOverthinkingDisadvantage_NoReduction()
        {
            // Overthinking at 5 (below T2 threshold of 12) → no disadvantage
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 5, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(sa: 5),
                shadows: shadows,
                options: new[] { new DialogueOption(StatType.SelfAwareness, "aware") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if reduction fires without disadvantage being active
            Assert.Equal(5, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // What: AC-4 negative — Success with Charm (not SA) while Overthinking is high
        // Mutation: Would catch if stat check is missing (reduces on any successful roll)
        [Fact]
        public async Task AC4_SuccessWithCharm_OverthinkingHigh_NoReduction()
        {
            var shadows = new SessionShadowTracker(MakeStats());
            shadows.ApplyGrowth(ShadowStatType.Overthinking, 12, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm, not SA

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if any success triggers Overthinking reduction
            Assert.Equal(12, shadows.GetDelta(ShadowStatType.Overthinking));
        }
    }
}

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
        // AC-2: Honesty Success at Interest ≥ 15 → Denial −1
        // =====================================================================

        // What: AC-2 — Denial reduction on Honesty success at interest exactly 15 (boundary)
        // Mutation: Would catch if condition uses > 15 instead of >= 15
        [Fact]
        public async Task AC2_HonestySuccessAtExactly15_DenialReduced()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 4, "setup");
            shadows.DrainGrowthEvents();

            // Interest starts at 15, Honesty success keeps it ≥15
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Honesty, "truth bomb") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if >= 15 replaced with > 15
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // What: AC-2 — Growth event string for Denial reduction
        // Mutation: Would catch if reason string is wrong
        [Fact]
        public async Task AC2_HonestySuccessAtHighInterest_GrowthEventRecorded()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 2, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: shadows,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if event not recorded or reason text is wrong
            Assert.Contains(result.ShadowGrowthEvents,
                e => e.Contains("Denial") && e.Contains("Honesty success at high interest"));
        }

        // What: AC-2 negative — Honesty failure does NOT reduce Denial
        // Mutation: Would catch if reduction fires regardless of IsSuccess
        [Fact]
        public async Task AC2_HonestyFailureAtHighInterest_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 3, "setup");
            shadows.DrainGrowthEvents();

            // Interest at 15 (Interested, no advantage), low roll → failure
            var session = BuildSession(
                dice: Dice(2, 50), // low roll → failure
                playerStats: MakeStats(honesty: 0),
                shadows: shadows,
                startingInterest: 15,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsSuccess);
            // Mutation: Fails if IsSuccess check is missing
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // What: AC-2 negative — Non-Honesty stat at high interest does NOT reduce Denial
        // Mutation: Would catch if stat type check is missing
        [Fact]
        public async Task AC2_CharmSuccessAtHighInterest_NoDenialReduction()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 3, "setup");
            shadows.DrainGrowthEvents();

            // Use options without Honesty to isolate from #272 Denial skip-Honesty growth
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Charm, "Hey, you come here often?") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            // Mutation: Fails if stat type check is omitted (fires for any stat)
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Denial));
        }

        // What: Edge case — Denial reduction stacks across turns
        // Mutation: Would catch if reduction is capped to once per session
        [Fact]
        public async Task AC2_DenialReductionStacksAcrossTurns()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Denial, 5, "setup");
            shadows.DrainGrowthEvents();

            // Two turns of Honesty success at high interest
            var session = BuildSession(
                dice: Dice(18, 50, 18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: shadows,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            // Denial should be 4 after first turn

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            // Denial should be 3 after second turn (two reductions)

            // Mutation: Fails if reduction only fires once per session
            Assert.True(shadows.GetDelta(ShadowStatType.Denial) < 4,
                "Denial should stack reductions across turns");
        }
    }
}

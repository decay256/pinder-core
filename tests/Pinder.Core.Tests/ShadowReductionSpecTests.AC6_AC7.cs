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
        // AC-6: Omitted tracker is synthesized and reductions remain safe
        // =====================================================================

        [Fact]
        public async Task OmittedShadowTracker_DateSecured_UsesDefaultTracker()
        {
            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: null,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            Assert.NotNull(session.State.PlayerShadows);
        }

        [Fact]
        public async Task OmittedShadowTracker_HonestySuccessUsesDefaultTracker()
        {
            var session = BuildSession(
                dice: Dice(18, 50),
                playerStats: MakeStats(honesty: 5),
                shadows: null,
                startingInterest: 16,
                options: new[] { new DialogueOption(StatType.Honesty, "truth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);
            Assert.True(result.Roll.IsSuccess);
            Assert.NotNull(session.State.PlayerShadows);
        }

        // =====================================================================
        // AC-7: Nat 20 (any stat) → Dread −1 (#720)
        // =====================================================================

        // What: Nat 20 on Charm → Dread -1
        // Mutation: Would catch if Dread reduction on Nat 20 is missing
        [Fact]
        public async Task Nat20OnCharm_ReducesDreadByOne()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 5, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 10,
                options: new[] { new DialogueOption(StatType.Charm, "smooth line") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            // Nat 20 → Dread -1: 5 - 1 = 4
            Assert.Equal(4, shadows.GetDelta(ShadowStatType.Dread));
            Assert.Contains(result.ShadowGrowthEvents,
                e => e.Contains("Dread") && e.Contains("-1") && e.Contains("existential confidence"));
        }

        // What: Nat 20 on Wit → Dread -1 (stat-agnostic verification)
        // Mutation: Would catch if Dread reduction is stat-gated
        [Fact]
        public async Task Nat20OnWit_ReducesDreadByOne()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 3, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(20, 50),
                playerStats: MakeStats(wit: 5),
                shadows: shadows,
                startingInterest: 10,
                options: new[] { new DialogueOption(StatType.Wit, "clever quip") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsNatTwenty);
            // Nat 20 → Dread -1: 3 - 1 = 2
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Dread));
        }

        // What: Non-Nat-20 roll does NOT reduce Dread via Nat 20 path
        // Mutation: Would catch if Dread reduction fires on any roll
        [Fact]
        public async Task NonNat20_NoDreadReductionFromNat20Rule()
        {
            var shadows = MakeTracker();
            shadows.ApplyGrowth(ShadowStatType.Dread, 3, "setup");
            shadows.DrainGrowthEvents();

            var session = BuildSession(
                dice: Dice(15, 50),
                playerStats: MakeStats(charm: 5),
                shadows: shadows,
                startingInterest: 10,
                options: new[] { new DialogueOption(StatType.Charm, "hey") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.False(result.Roll.IsNatTwenty);
            // Dread should remain 3 — no Nat 20 reduction
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Dread));
        }
    }
}

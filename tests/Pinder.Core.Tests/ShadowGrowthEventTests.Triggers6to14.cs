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
        // ======================== Trigger 6+11: Honesty success tracking + end-of-game Denial ========================

        [Fact]
        public async Task DateSecuredWithoutHonestySuccess_GrowsDenial()
        {
            var shadows = MakeShadowTracker();
            // Start at interest 24, one successful Charm roll (+1 from SuccessScale) → 25 → DateSecured
            // d20=15, d100=50 for ComputeDelay
            var dice = new QueueDice(new[] { 15, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // Charm success

            Assert.True(result.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, result.Outcome);
            // Should have: "Never picked Chaos" +1 Fixation, "Date secured without Honesty" +1 Denial
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Denial") && e.Contains("Date secured without any Honesty successes"));
        }

        // ======================== Trigger 7: Interest hits 0 → +2 Dread ========================

        [Fact]
        public async Task InterestHits0_GrowsDread2()
        {
            var shadows = MakeShadowTracker();
            // Start at interest 1. Need a failure that drops interest by >= 1.
            // Roll: d20=2, Charm mod=0, level 1 → total 2, DC ~13 → fail Catastrophe (miss 11) → -3
            var dice = new QueueDice(new[] { 2, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 0),
                dateeStats: MakeStatBlock(sa: 0),
                shadows: shadows,
                startingInterest: 1);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Dread") && e.Contains("Interest hit 0 (unmatch)"));
            Assert.True(shadows.GetDelta(ShadowStatType.Dread) >= 2);
        }

        // ======================== Trigger 8: Ghost → +1 Dread ========================

        // #942 transactional fix: tracker is NOT mutated when StartTurnAsync throws.
        // The exception carries the event description; tracker unchanged.
        [Fact]
        public async Task Ghost_GrowsDread()
        {
            var shadows = MakeShadowTracker();
            // Interest 1 = Bored. dice.Roll(4)==1 → ghost
            var dice = new QueueDice(new[] { 1 }); // ghost roll = 1
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(),
                shadows: shadows,
                startingInterest: 1);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
            Assert.Contains(ex.ShadowGrowthEvents, e => e.Contains("Dread") && e.Contains("Ghosted"));
            // Tracker unchanged (#942 transactional contract): caller uses MarkEnded after catch.
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Dread));
        }

        // ======================== Trigger 9: SA used 3+ times → +1 Overthinking ========================

        [Fact]
        public async Task SA3Times_GrowsOverthinking()
        {
            var shadows = MakeShadowTracker();
            // 3 turns × (d20 + d100) = 6 dice values
            var dice = new QueueDice(new[] { 15, 50, 15, 50, 15, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(sa: 5),
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.SelfAwareness, "Hmm...")
                },
                startingInterest: 5);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // SA 3x triggers both Overthinking (+1) and same-stat-3 Fixation (+1)
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Overthinking));
        }

        // ======================== Trigger 12: Never picked Chaos → +1 Fixation (end-of-game) ========================

        [Fact]
        public async Task NeverPickedChaos_EndOfGame_GrowsFixation()
        {
            var shadows = MakeShadowTracker();
            // Quick game: start at 24, succeed with Charm → DateSecured
            var dice = new QueueDice(new[] { 15, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 5),
                shadows: shadows,
                startingInterest: 24);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.IsGameOver);
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Fixation") && e.Contains("Never picked Chaos"));
        }

        // ======================== Trigger 13: 4+ distinct stats → −1 Fixation (end-of-game) ========================

        [Fact]
        public async Task FourDistinctStats_EndOfGame_ReducesFixation()
        {
            var shadows = MakeShadowTracker();
            // Play 4 turns with different stats, then end the game
            // Use high interest so we can play multiple turns without ending
            // We need Charm, Honesty, Wit, Chaos → 4 distinct stats
            // Each turn: d20 + d100. 5 turns max.
            var diceValues = new List<int>();
            for (int i = 0; i < 6; i++) { diceValues.Add(20); diceValues.Add(50); }
            var dice = new QueueDice(diceValues.ToArray());
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 5, honesty: 5, wit: 5, chaos: 5),
                shadows: shadows,
                startingInterest: 5);

            // Turn 1: Charm (index 0)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: Honesty (index 1)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1);

            // Turn 3: Wit (index 2)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(2);

            // Turn 4: Chaos (index 3) — should push to DateSecured
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(3);

            if (!result.IsGameOver)
            {
                await session.StartTurnAsync();
                result = await session.ResolveTurnAsync(0);
            }

            Assert.True(result.IsGameOver);
            // 4+ distinct stats → -1 Fixation offset
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Fixation") && e.Contains("4+ different stats used"));
            // "Never picked Chaos" should NOT fire since we picked Chaos
            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Never picked Chaos"));
        }

        // ======================== Trigger 14: Same opener removed (#716) ========================

        [Fact]
        public async Task SameOpenerTwice_NoLongerGrowsMadness()
        {
            var shadows = MakeShadowTracker();
            var dice = new QueueDice(new[] { 15, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 5),
                shadows: shadows,
                previousOpener: "Hey, you come here often?");

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Same opener"));
        }

        [Fact]
        public async Task NullPreviousOpener_NoMadness()
        {
            var shadows = MakeShadowTracker();
            var dice = new QueueDice(new[] { 15, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 5),
                shadows: shadows,
                previousOpener: null);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.DoesNotContain(result.ShadowGrowthEvents, e => e.Contains("Same opener twice in a row"));
        }
    }
}

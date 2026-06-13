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
        // ======================== Trigger 1: Nat 1 → +1 paired shadow ========================

        [Fact]
        public async Task Nat1OnCharm_GrowsMadness()
        {
            // Nat 1 on Charm → +1 Madness
            var shadows = MakeShadowTracker();
            var session = MakeSession(
                diceValues: new[] { 1, 50 }, // d20 = 1 (Nat 1), d100 for ComputeDelay
                playerStats: MakeStatBlock(charm: 3),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // option 0 = Charm

            Assert.Single(result.ShadowGrowthEvents.Where(e => e.Contains("Madness") && e.Contains("Nat 1 on Charm")));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Madness));
        }

        [Fact]
        public async Task Nat1OnWit_GrowsDread()
        {
            var shadows = MakeShadowTracker();
            var session = MakeSession(
                diceValues: new[] { 1, 50 },
                playerStats: MakeStatBlock(wit: 3),
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(2); // option 2 = Wit

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Dread") && e.Contains("Nat 1 on Wit"));
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Dread));
        }

        [Fact]
        public async Task Nat1OnSA_GrowsOverthinking()
        {
            var shadows = MakeShadowTracker();
            var session = MakeSession(
                diceValues: new[] { 1, 50 },
                playerStats: MakeStatBlock(sa: 3),
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.SelfAwareness, "Hmm...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Overthinking") && e.Contains("Nat 1 on SelfAwareness"));
        }

        // ======================== Trigger 2: Catastrophic Wit failure → +1 Dread ========================

        [Fact]
        public async Task CatastrophicWitFailure_GrowsDread()
        {
            // Need to miss DC by 10+. Wit roll: d20=2, modifier=0, level=1 → total=2, DC ~13+ → miss by 10+
            var shadows = MakeShadowTracker();
            var session = MakeSession(
                diceValues: new[] { 2, 50 },
                playerStats: MakeStatBlock(wit: 0),
                dateeStats: MakeStatBlock(rizz: 0), // Defence for Wit is Rizz
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(2); // Wit option

            // Roll: d20=2 + 0 (wit) + 0 (level bonus at level 1) = 2, DC = 16 + datee's Rizz effective
            // Miss margin = DC - Total, need >= 10 for Catastrophe
            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Catastrophic Wit failure"));
        }

        // ======================== Trigger 3: Every TropeTrap → +1 Madness (#716) ========================

        [Fact]
        public async Task ThreeTropeTraps_GrowsMadness()
        {
            // Need 3 TropeTrap-tier failures. Miss by 6–9 = TropeTrap.
            // Use Honesty to avoid CHARM 3x trigger.
            var shadows = MakeShadowTracker();
            var dice = new QueueDice(new[] { 6, 50, 6, 50, 6, 50 });
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(honesty: 0),
                dateeStats: MakeStatBlock(charm: 0), // Honesty defence = Charm
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "truth") },
                startingInterest: 15);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Each TropeTrap gives +1 Madness, so 3 TropeTraps = 3
            Assert.Equal(3, shadows.GetDelta(ShadowStatType.Madness));
        }

        // ======================== Trigger 4: Same stat 3 turns → +1 Fixation ========================

        [Fact]
        public async Task SameStat3Turns_GrowsFixation()
        {
            var shadows = MakeShadowTracker();
            // Use high rolls to succeed (avoid Nat 1 side effects)
            // Each turn consumes: 1 d20 + 1 d100 (ComputeDelay) = 2 dice values per turn
            var dice = new QueueDice(new[] { 15, 50, 15, 50, 15, 50 });
            // Charm(0) is NOT the highest-prob; Honesty(5) vs Chaos defence(0) is.
            // This isolates same-stat trigger from highest-% trigger.
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 0, honesty: 5),
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Honesty, "honest"),
                    new DialogueOption(StatType.Charm, "charming"),
                    new DialogueOption(StatType.Wit, "witty"),
                    new DialogueOption(StatType.Chaos, "chaotic")
                });

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Charm each time at index 1 (NOT highest-prob)
            }

            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Fixation));
        }

        [Fact]
        public async Task SameStat6Turns_TriggersFixationTwice()
        {
            var shadows = MakeShadowTracker();
            // Use mild failures: d20=10, Charm=0, DC=13 → miss by 3 → Misfire(-1 per rules-v3.4 §5)
            // Each turn: 1 d20 + 1 d100 (ComputeDelay) = 2 dice per turn, 12 total
            // Start at 15 (Interested, no advantage). 6× -1 = -6 → 9 → Interested. No ghost risk.
            var diceValues = new List<int>();
            for (int i = 0; i < 6; i++) { diceValues.Add(10); diceValues.Add(50); }
            var dice = new QueueDice(diceValues.ToArray());
            // Charm at index 1 to avoid highest-% trigger
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 0),
                dateeStats: MakeStatBlock(sa: 0), // DC=13
                shadows: shadows,
                startingInterest: 15,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Honesty, "honest"),
                    new DialogueOption(StatType.Charm, "charming"),
                    new DialogueOption(StatType.Wit, "witty"),
                    new DialogueOption(StatType.Chaos, "chaotic")
                });

            for (int i = 0; i < 6; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Charm each time at index 1
            }

            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Fixation));
        }

        // ======================== Trigger 5: Highest-% option 3 in a row → +1 Fixation ========================

        [Fact]
        public async Task HighestPctOption3InARow_GrowsFixation()
        {
            var shadows = MakeShadowTracker();
            // Each turn: d20 + d100 = 2 dice. 3 turns = 6 dice.
            var dice = new QueueDice(new[] { 15, 50, 15, 50, 15, 50 });
            // Charm(5) vs SA defence(0) → DC 13, margin = 5−13 = −8 → highest prob
            // Honesty(5) vs Chaos(0) → DC 13, margin = 5−13 = −8 → tied
            // Wit(5) vs Rizz(0) → DC 13, margin = 5−13 = −8 → tied
            // All stats tied: picking any counts as "highest-%" per tie-breaking rule
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 5, honesty: 5, wit: 5),
                dateeStats: MakeStatBlock(sa: 0, chaos: 0, rizz: 0),
                shadows: shadows,
                llmOptions: new[]
                {
                    new DialogueOption(StatType.Charm, "a"),
                    new DialogueOption(StatType.Honesty, "b"),
                    new DialogueOption(StatType.Wit, "c"),
                    new DialogueOption(StatType.Chaos, "d")
                });

            // Pick index 0 three times (Charm each time → same-stat + highest-%)
            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            // Charm 3x triggers same-stat Fixation(+1) + highest-% Fixation(+1) = 2
            Assert.Equal(2, shadows.GetDelta(ShadowStatType.Fixation));
        }
    }
}

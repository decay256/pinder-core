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
    /// <summary>
    /// Tests for shadow growth events — §7 growth table in GameSession (#44).
    /// Maturity: Prototype (happy-path tests for key triggers).
    /// </summary>
    public class ShadowGrowthEventTests
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
                opponentStats: MakeStatBlock(rizz: 0), // Defence for Wit is Rizz
                shadows: shadows);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(2); // Wit option

            // Roll: d20=2 + 0 (wit) + 0 (level bonus at level 1) = 2, DC = 16 + opponent's Rizz effective
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
                opponentStats: MakeStatBlock(charm: 0), // Honesty defence = Charm
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
                opponentStats: MakeStatBlock(sa: 0), // DC=13
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
                opponentStats: MakeStatBlock(sa: 0, chaos: 0, rizz: 0),
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
                opponentStats: MakeStatBlock(sa: 0),
                shadows: shadows,
                startingInterest: 1);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Contains(result.ShadowGrowthEvents, e => e.Contains("Dread") && e.Contains("Interest hit 0 (unmatch)"));
            Assert.True(shadows.GetDelta(ShadowStatType.Dread) >= 2);
        }

        // ======================== Trigger 8: Ghost → +1 Dread ========================

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
            Assert.Equal(1, shadows.GetDelta(ShadowStatType.Dread));
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

        // ======================== No shadow tracker → empty events ========================

        [Fact]
        public async Task NoShadowTracker_EmptyShadowEvents()
        {
            var dice = new QueueDice(new[] { 1, 50 }); // Nat 1, d100=50
            var session = MakeSessionWithDice(dice,
                playerStats: MakeStatBlock(charm: 0),
                shadows: null);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Empty(result.ShadowGrowthEvents);
        }

        // ======================== Read/Recover Overthinking ========================

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
            var tracker = MakeShadowTracker();
            string desc = tracker.ApplyOffset(ShadowStatType.Fixation, -1, "test offset");

            Assert.Contains("-1", desc);
            var events = tracker.DrainGrowthEvents();
            Assert.Single(events);
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

        // ======================== Helpers ========================

        private static SessionShadowTracker MakeShadowTracker()
        {
            return new SessionShadowTracker(MakeStatBlock());
        }

        private static StatBlock MakeStatBlock(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz }, { StatType.Honesty, honesty },
                    { StatType.Chaos, chaos }, { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
        {
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession MakeSession(
            int[] diceValues,
            StatBlock? playerStats = null,
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? llmOptions = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats = playerStats ?? MakeStatBlock();
            opponentStats = opponentStats ?? MakeStatBlock();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);

            var llm = llmOptions != null ? (ILlmAdapter)new CustomLlmAdapter(llmOptions) : new NullLlmAdapter();

            // Prepend horniness roll (1d10)
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
                llm,
                new QueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithDice(
            QueueDice dice,
            StatBlock? playerStats = null,
            StatBlock? opponentStats = null,
            SessionShadowTracker? shadows = null,
            DialogueOption[]? llmOptions = null,
            string? previousOpener = null,
            int? startingInterest = null)
        {
            playerStats = playerStats ?? MakeStatBlock();
            opponentStats = opponentStats ?? MakeStatBlock();

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                previousOpener: previousOpener,
                startingInterest: startingInterest);

            var llm = llmOptions != null ? (ILlmAdapter)new CustomLlmAdapter(llmOptions) : new NullLlmAdapter();

            // Prepend horniness roll via wrapper
            var wrappedDice = new PrependedDice(5, dice);

            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
                llm,
                wrappedDice,
                new NullTrapRegistry(),
                config);
        }

        /// <summary>Wraps a dice roller, returning a prepended value first.</summary>
        private sealed class PrependedDice : IDiceRoller
        {
            private int? _first;
            private readonly IDiceRoller _inner;
            public PrependedDice(int firstValue, IDiceRoller inner) { _first = firstValue; _inner = inner; }
            public int Roll(int sides) { if (_first.HasValue) { var v = _first.Value; _first = null; return v; } return _inner.Roll(sides); }
        }

        private static void ActivateTrap(GameSession session)
        {
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;
            var trap = new TrapDefinition("test-trap", StatType.Charm, TrapEffect.Disadvantage, 1, 3, "test instruction", "clear", "");
            trapState.Activate(trap);
        }

        /// <summary>Dice that returns values from a queue.</summary>
        private sealed class QueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public QueueDice(int[] values)
            {
                _values = new Queue<int>(values);
            }

            public void Enqueue(params int[] values)
            {
                foreach (var v in values)
                    _values.Enqueue(v);
            }

            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    return 10; // safe default
                return _values.Dequeue();
            }
        }

        /// <summary>LLM adapter that returns custom options.</summary>
        private sealed class CustomLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;

            public CustomLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}

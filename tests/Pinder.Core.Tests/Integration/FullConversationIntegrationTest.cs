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

namespace Pinder.Core.Tests.Integration
{
    /// <summary>
    /// End-to-end integration test running a full 8-turn GameSession conversation.
    /// Verifies the complete rules stack: interest deltas with success/failure scaling,
    /// momentum streaks, combo detection, trap activation and recovery, shadow growth
    /// events, XP accumulation, and game outcome resolution.
    ///
    /// All randomness is controlled via FixedDice; no real LLM API calls are made.
    /// </summary>
    public class FullConversationIntegrationTest
    {
        /// <summary>
        /// Runs 8 turns: 7 Speak actions + 1 Recover action.
        /// Turn plan (with startingInterest=15 to reach DateSecured):
        ///   T1: Wit success          → +2 (Hard tier)
        ///   T2: Charm success        → +2 (The Setup combo: Wit→Charm)
        ///   T3: Honesty fail Misfire → −2
        ///   T4: SA success           → +5 (Bold tier + The Recovery combo)
        ///   T5: Chaos fail TropeTrap → −3 (trap "unhinged" activates)
        ///   T6: Recover success      → 0  (trap cleared)
        ///   T7: Charm success        → +2
        ///   T8: Charm Nat20 success  → +4 → interest 25 → DateSecured
        /// </summary>
        [Fact]
        public async Task FullEightTurnConversation_VerifiesAllMechanics()
        {
            // ---- Setup ----
            var geraldStats = CreateGeraldStats();
            var velvetStats = CreateVelvetStats();

            var timing = new TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral");

            var gerald = new CharacterProfile(geraldStats, "Test system prompt", "Gerald", timing, level: 5);
            var velvet = new CharacterProfile(velvetStats, "Test system prompt", "Velvet", timing, level: 7);

            var playerShadows = new SessionShadowTracker(geraldStats);
            var opponentShadows = new SessionShadowTracker(velvetStats);

            var config = new GameSessionConfig(
                clock: null,
                playerShadows: playerShadows,
                opponentShadows: opponentShadows,
                startingInterest: 15,
                previousOpener: null);

            var trapRegistry = new TestTrapRegistry();

            // Build scripted LLM adapter: returns correct stat options per turn.
            // Turn 4 needs SelfAwareness which NullLlmAdapter doesn't provide.
            var llm = new ScriptedLlmAdapter(new[]
            {
                // Turn 1: pick index 0 = Wit
                new[] { Opt(StatType.Wit), Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Chaos) },
                // Turn 2: pick index 1 = Charm (index 1 to avoid highest-% streak)
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 3: pick index 0 = Honesty
                new[] { Opt(StatType.Honesty), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 4: pick index 0 = SelfAwareness
                new[] { Opt(StatType.SelfAwareness), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 5: pick index 0 = Chaos
                new[] { Opt(StatType.Chaos), Opt(StatType.Charm), Opt(StatType.Wit), Opt(StatType.Honesty) },
                // Turn 6 is Recover — no options needed
                // Turn 7: pick index 0 = Charm
                new[] { Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Wit), Opt(StatType.Chaos) },
                // Turn 8: pick index 0 = Charm
                new[] { Opt(StatType.Charm), Opt(StatType.Honesty), Opt(StatType.Wit), Opt(StatType.Chaos) },
            });

            // Full dice queue (traced through every dice.Roll call):
            // T1 (no adv):  d20=14,         d100=50
            // T2 (adv):     d20=3, d20=5,   d100=50
            // T3 (adv):     d20=19, d20=18, d100=50
            // T4 (adv):     d20=15, d20=18, d100=50
            // T5 (adv):     d20=5, d20=7,   d100=50
            // T6 (Recover, adv): d20=10, d20=8
            // T7 (adv):     d20=8, d20=6,   d100=50
            // T8 (adv):     d20=20, d20=5,  d100=50
            var dice = new FixedDice(
                14, 50,             // T1
                3, 5, 50,           // T2
                19, 18, 50,         // T3
                15, 18, 50,         // T4
                5, 7, 50,           // T5
                10, 8,              // T6 Recover
                8, 6, 50,           // T7
                20, 5, 50           // T8
            );

            var session = new GameSession(gerald, velvet, llm, dice, trapRegistry, config);

            // ========== Turn 1: Speak WIT (Success) ==========
            var turn1Start = await session.StartTurnAsync();
            Assert.NotNull(turn1Start);
            var turn1 = await session.ResolveTurnAsync(0); // Wit

            Assert.True(turn1.Roll.IsSuccess);
            Assert.Equal(StatType.Wit, turn1.Roll.Stat);
            Assert.Equal(19, turn1.Roll.Total);           // 14 + 3 + 2
            Assert.Equal(17, turn1.Roll.DC);
            Assert.Equal(RiskTier.Hard, turn1.Roll.RiskTier); // need = 17-3-2 = 12
            Assert.Equal(2, turn1.InterestDelta);          // +1 success + 1 Hard
            Assert.Equal(17, turn1.StateAfter.Interest);   // 15 + 2
            Assert.Equal(InterestState.VeryIntoIt, turn1.StateAfter.State);
            Assert.Equal(1, turn1.StateAfter.MomentumStreak);
            Assert.Null(turn1.ComboTriggered);
            Assert.Equal(10, turn1.XpEarned);              // DC 17 → mid-tier → 10
            Assert.False(turn1.IsGameOver);

            // ========== Turn 2: Speak CHARM (Success, The Setup combo) ==========
            var turn2Start = await session.StartTurnAsync();
            var turn2 = await session.ResolveTurnAsync(1); // Charm at index 1

            Assert.True(turn2.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn2.Roll.Stat);
            Assert.Equal(20, turn2.Roll.Total);            // max(3,5)=5 + 13 + 2
            Assert.Equal(18, turn2.Roll.DC);
            Assert.Equal(RiskTier.Safe, turn2.Roll.RiskTier); // need = 18-13-2 = 3
            Assert.Equal("The Setup", turn2.ComboTriggered); // Wit→Charm
            Assert.Equal(2, turn2.InterestDelta);          // +1 success + 0 Safe + 1 combo
            Assert.Equal(19, turn2.StateAfter.Interest);   // 17 + 2
            Assert.Equal(InterestState.VeryIntoIt, turn2.StateAfter.State);
            Assert.Equal(2, turn2.StateAfter.MomentumStreak);
            Assert.Equal(15, turn2.XpEarned);              // DC 18 → high-tier → 15
            Assert.False(turn2.IsGameOver);

            // ========== Turn 3: Speak HONESTY (Fail, Misfire) ==========
            var turn3Start = await session.StartTurnAsync();
            var turn3 = await session.ResolveTurnAsync(0); // Honesty at index 0

            Assert.False(turn3.Roll.IsSuccess);
            Assert.Equal(StatType.Honesty, turn3.Roll.Stat);
            Assert.Equal(24, turn3.Roll.Total);            // max(19,18)=19 + 3 + 2
            Assert.Equal(27, turn3.Roll.DC);               // 13 + Velvet Chaos 14
            Assert.Equal(FailureTier.Misfire, turn3.Roll.Tier); // miss = 27-24 = 3
            Assert.Equal(-2, turn3.InterestDelta);         // Misfire → −2
            Assert.Equal(17, turn3.StateAfter.Interest);   // 19 − 2
            Assert.Equal(InterestState.VeryIntoIt, turn3.StateAfter.State);
            Assert.Equal(0, turn3.StateAfter.MomentumStreak); // reset on fail
            Assert.Null(turn3.ComboTriggered);
            Assert.Equal(2, turn3.XpEarned);               // failure → 2
            Assert.False(turn3.IsGameOver);

            // ========== Turn 4: Speak SA (Success, The Recovery combo, Bold tier) ==========
            var turn4Start = await session.StartTurnAsync();
            var turn4 = await session.ResolveTurnAsync(0); // SelfAwareness at index 0

            Assert.True(turn4.Roll.IsSuccess);
            Assert.Equal(StatType.SelfAwareness, turn4.Roll.Stat);
            Assert.Equal(24, turn4.Roll.Total);            // max(15,18)=18 + 4 + 2
            Assert.Equal(23, turn4.Roll.DC);               // 13 + Velvet Honesty 10
            Assert.Equal(RiskTier.Bold, turn4.Roll.RiskTier); // need = 23-4-2 = 17
            Assert.Equal("The Recovery", turn4.ComboTriggered); // fail→SA success
            Assert.Equal(5, turn4.InterestDelta);          // +1 success + 2 Bold + 2 Recovery
            Assert.Equal(22, turn4.StateAfter.Interest);   // 17 + 5
            Assert.Equal(InterestState.AlmostThere, turn4.StateAfter.State);
            Assert.Equal(1, turn4.StateAfter.MomentumStreak);
            Assert.Equal(15, turn4.XpEarned);              // DC 23 → high-tier → 15
            Assert.False(turn4.IsGameOver);

            // ========== Turn 5: Speak CHAOS (Fail, TropeTrap) ==========
            var turn5Start = await session.StartTurnAsync();
            var turn5 = await session.ResolveTurnAsync(0); // Chaos at index 0

            Assert.False(turn5.Roll.IsSuccess);
            Assert.Equal(StatType.Chaos, turn5.Roll.Stat);
            Assert.Equal(11, turn5.Roll.Total);            // max(5,7)=7 + 2 + 2
            Assert.Equal(18, turn5.Roll.DC);               // 13 + Velvet Charm 5
            Assert.Equal(FailureTier.TropeTrap, turn5.Roll.Tier); // miss = 18-11 = 7
            Assert.NotNull(turn5.Roll.ActivatedTrap);
            Assert.Equal("unhinged", turn5.Roll.ActivatedTrap.Id);
            Assert.Equal(-3, turn5.InterestDelta);         // TropeTrap → −3
            Assert.Equal(19, turn5.StateAfter.Interest);   // 22 − 3
            Assert.Equal(InterestState.VeryIntoIt, turn5.StateAfter.State);
            Assert.Equal(0, turn5.StateAfter.MomentumStreak); // reset on fail
            Assert.Contains("unhinged", turn5.StateAfter.ActiveTrapNames);
            Assert.Equal(2, turn5.XpEarned);               // failure → 2
            Assert.False(turn5.IsGameOver);

            // ========== Turn 6: Recover (SA vs DC 12, Success) ==========
            var recover = await session.RecoverAsync();

            Assert.True(recover.Success);
            Assert.Equal("unhinged", recover.ClearedTrapName);
            Assert.True(recover.Roll.IsSuccess);
            Assert.Equal(16, recover.Roll.Total);          // max(10,8)=10 + 4 + 2
            Assert.Equal(12, recover.Roll.DC);
            Assert.Equal(19, recover.StateAfter.Interest); // unchanged
            Assert.Empty(recover.StateAfter.ActiveTrapNames);
            Assert.Equal(15, recover.XpEarned);            // recovery success → 15

            // ========== Turn 7: Speak CHARM (Success) ==========
            var turn7Start = await session.StartTurnAsync();
            var turn7 = await session.ResolveTurnAsync(0); // Charm at index 0

            Assert.True(turn7.Roll.IsSuccess);
            Assert.Equal(StatType.Charm, turn7.Roll.Stat);
            Assert.Equal(23, turn7.Roll.Total);            // max(8,6)=8 + 13 + 2
            Assert.Equal(18, turn7.Roll.DC);
            Assert.Equal(RiskTier.Safe, turn7.Roll.RiskTier);
            Assert.Equal(2, turn7.InterestDelta);          // +2 success (beat by 5)
            Assert.Equal(21, turn7.StateAfter.Interest);   // 19 + 2
            Assert.Equal(InterestState.AlmostThere, turn7.StateAfter.State);
            Assert.Equal(1, turn7.StateAfter.MomentumStreak);
            // The Triple combo fires: SA(T4), Chaos(T5), Charm(T7) = 3 different stats in combo history
            Assert.Equal("The Triple", turn7.ComboTriggered);
            Assert.True(turn7.StateAfter.TripleBonusActive); // +1 roll bonus queued for next turn
            Assert.Equal(15, turn7.XpEarned);              // DC 18 → high-tier → 15
            Assert.False(turn7.IsGameOver);

            // ========== Turn 8: Speak CHARM (Nat 20, DateSecured) ==========
            var turn8Start = await session.StartTurnAsync();
            var turn8 = await session.ResolveTurnAsync(0); // Charm at index 0

            Assert.True(turn8.Roll.IsSuccess);
            Assert.True(turn8.Roll.IsNatTwenty);
            Assert.Equal(StatType.Charm, turn8.Roll.Stat);
            Assert.Equal(35, turn8.Roll.Total);            // max(20,5)=20 + 13 + 2
            Assert.Equal(1, turn8.Roll.ExternalBonus);     // +1 from consumed Triple bonus
            Assert.Equal(4, turn8.InterestDelta);          // Nat20 → +4
            Assert.Equal(25, turn8.StateAfter.Interest);   // 21 + 4
            Assert.Equal(InterestState.DateSecured, turn8.StateAfter.State);
            Assert.Equal(2, turn8.StateAfter.MomentumStreak);
            Assert.True(turn8.IsGameOver);
            Assert.Equal(GameOutcome.DateSecured, turn8.Outcome);

            // Nat20 → 25 XP + DateSecured → 50 XP = 75 total for this turn
            Assert.Equal(75, turn8.XpEarned);

            // ---- Cumulative XP verification ----
            int expectedTotalXp = 10 + 15 + 2 + 15 + 2 + 15 + 15 + 75; // = 149
            Assert.Equal(expectedTotalXp, session.TotalXpEarned);

            // ---- Shadow growth events verification ----
            // End-of-game triggers: Denial +1 (no Honesty success) and Fixation -1 (4+ distinct stats)
            // These appear on Turn 8's shadow growth events
            Assert.NotEmpty(turn8.ShadowGrowthEvents);
            Assert.Contains(turn8.ShadowGrowthEvents,
                e => e.Contains("Denial") && e.Contains("+1"));
            Assert.Contains(turn8.ShadowGrowthEvents,
                e => e.Contains("Fixation") && e.Contains("-1"));

            // Verify shadow tracker reflects the growth
            Assert.True(playerShadows.GetDelta(ShadowStatType.Denial) >= 1);
        }

        // ---- Helper methods ----

        /// <summary>Creates Gerald's stats: Charm+13, Wit+3, Honesty+3, Chaos+2, SA+4, Rizz+2. All shadows 0.</summary>
        private static StatBlock CreateGeraldStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 13 },
                    { StatType.Wit, 3 },
                    { StatType.Honesty, 3 },
                    { StatType.Chaos, 2 },
                    { StatType.SelfAwareness, 4 },
                    { StatType.Rizz, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        /// <summary>Creates Velvet's stats: Chaos+14, Honesty+10, Charm+5, SA+5, Wit+4, Rizz+4. All shadows 0.</summary>
        private static StatBlock CreateVelvetStats()
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Chaos, 14 },
                    { StatType.Honesty, 10 },
                    { StatType.Charm, 5 },
                    { StatType.SelfAwareness, 5 },
                    { StatType.Wit, 4 },
                    { StatType.Rizz, 4 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 },
                    { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 },
                    { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 },
                    { ShadowStatType.Overthinking, 0 }
                });
        }

        private static DialogueOption Opt(StatType stat, string text = "Test option")
        {
            return new DialogueOption(stat, text);
        }

        // ---- Test doubles ----

        /// <summary>
        /// Deterministic dice roller that returns values from a queue.
        /// Duplicated from GameSessionTests since it is internal to that file.
        /// </summary>
        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;

            public FixedDice(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public int Roll(int sides)
            {
                if (_values.Count == 0)
                    throw new InvalidOperationException(
                        $"FixedDice: no more values in queue (requested d{sides}).");
                return _values.Dequeue();
            }
        }

        /// <summary>
        /// Trap registry that returns "unhinged" for Chaos and null for all other stats.
        /// </summary>
        private sealed class TestTrapRegistry : ITrapRegistry
        {
            private readonly TrapDefinition _chaosTrap = new TrapDefinition(
                id: "unhinged",
                stat: StatType.Chaos,
                effect: TrapEffect.Disadvantage,
                effectValue: 0,
                durationTurns: 3,
                llmInstruction: "You're unhinged now",
                clearMethod: "Recover",
                nat1Bonus: "Extra chaos");

            public TrapDefinition? GetTrap(StatType stat)
            {
                return stat == StatType.Chaos ? _chaosTrap : null;
            }

            public string? GetLlmInstruction(StatType stat) => null;
        }

        /// <summary>
        /// LLM adapter that returns pre-scripted dialogue options per turn.
        /// Behaves like NullLlmAdapter for delivery, opponent response, and narrative beats.
        /// </summary>
        private sealed class ScriptedLlmAdapter : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets;

            public ScriptedLlmAdapter(IEnumerable<DialogueOption[]> optionSets)
            {
                _optionSets = new Queue<DialogueOption[]>(optionSets);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                if (_optionSets.Count == 0)
                    throw new InvalidOperationException(
                        "ScriptedLlmAdapter: no more option sets in queue.");
                return Task.FromResult(_optionSets.Dequeue());
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                string message = context.Outcome == FailureTier.None
                    ? context.ChosenOption.IntendedText
                    : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
                return Task.FromResult(message);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("..."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
        }
    }
}

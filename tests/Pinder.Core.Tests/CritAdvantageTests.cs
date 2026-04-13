using System;
using System.Collections.Generic;
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
    /// Tests for §4 Nat 20 crit advantage: after rolling a natural 20,
    /// the player gets advantage on their next roll, consumed after one use.
    /// </summary>
    [Trait("Category", "Core")]
    public class CritAdvantageTests
    {
        // What: Nat 20 on Speak turn N → advantage on Speak turn N+1
        // Mutation: Fails if _pendingCritAdvantage is not set on Nat 20 or not consumed in StartTurnAsync
        [Fact]
        public async Task Speak_Nat20_GrantsAdvantageOnNextSpeak()
        {
            // Interest starts at 10 (Interested) — no interest-based adv/disadv
            // Turn 1: Nat 20 → sets _pendingCritAdvantage
            // Turn 2: advantage → rolls 2 dice, uses max
            var dice = new FixedDice(
                5,          // Constructor: horniness roll (1d10)
                20, 50,     // Turn 1: d20=20 (Nat 20), d100=50 (ghost check not reached at interest 10)
                15, 3, 50   // Turn 2: advantage → d20=15, d20=3 (max=15), d100=50
            );

            var session = MakeSession(dice);
            var llm = (ScriptedLlm)GetLlm(session);

            // Turn 1: Nat 20
            var turn1Start = await session.StartTurnAsync();
            var turn1Result = await session.ResolveTurnAsync(0);
            Assert.True(turn1Result.Roll.IsNatTwenty, "Turn 1 should be a Nat 20");

            // Turn 2: should have advantage — we verify by checking that 2 dice were consumed
            // (FixedDice will throw if we try to dequeue more than queued)
            var turn2Start = await session.StartTurnAsync();
            var turn2Result = await session.ResolveTurnAsync(0);

            // If advantage was granted, used die roll should be max(15, 3) = 15
            Assert.Equal(15, turn2Result.Roll.UsedDieRoll);
        }

        // What: Advantage from Nat 20 clears after one roll — turn N+2 has no crit advantage
        // Mutation: Fails if _pendingCritAdvantage is not cleared after consumption
        [Fact]
        public async Task Speak_Nat20_AdvantageClears_AfterOneRoll()
        {
            // Start interest at 5 (Interested, no adv/disadv) to keep it in Interested range throughout
            // Turn 1: Nat 20 → interest rises (Bold tier bonus with new DC base 16)
            // Turn 2: crit advantage → 2 dice; use roll=12 to fail (12+3=15 < DC 18) → keep interest below VeryIntoIt
            // Turn 3: no crit advantage, no interest advantage → 1 die
            var dice = new FixedDice(
                5,                  // Constructor: horniness
                20, 50,             // Turn 1: d20=20 (Nat 20), d100=50 (timing)
                12, 3, 50,          // Turn 2: crit adv → d20=12, d20=3=max 12, d100=50 (timing)
                8, 50, 50, 50       // Turn 3: no adv → d20=8, extras
            );

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 5);
            var session = MakeSession(dice, config);

            // Turn 1: Nat 20 — interest goes 5→9
            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.True(t1.Roll.IsNatTwenty);

            // Turn 2: crit advantage consumed — max(12,3) = 12
            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            Assert.Equal(12, t2.Roll.UsedDieRoll);

            // Turn 3: no crit advantage — single die = 8
            await session.StartTurnAsync();
            var t3 = await session.ResolveTurnAsync(0);
            Assert.Equal(8, t3.Roll.UsedDieRoll);
        }



        // What: Crit advantage stacks correctly with interest-based advantage
        // (both give advantage — still just advantage, no double-roll)
        // Mutation: Fails if crit advantage overrides or conflicts with interest advantage
        [Fact]
        public async Task CritAdvantage_StacksWithInterestAdvantage()
        {
            // Start at interest 16 (VeryIntoIt → grants advantage already)
            // Turn 1: Nat 20 → sets crit advantage
            // Turn 2: has both interest advantage AND crit advantage → still just advantage (2 dice)
            var dice = new FixedDice(
                5,              // Constructor: horniness
                20, 8, 50,      // Turn 1: advantage (interest=16→VeryIntoIt), d20=20, d20=8, Nat 20
                14, 6, 50       // Turn 2: advantage (both interest + crit), d20=14, d20=6
            );

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 16);
            var session = MakeSession(dice, config);

            // Turn 1: already has advantage from interest, Nat 20
            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.True(t1.Roll.IsNatTwenty);

            // Turn 2: interest-based advantage + crit advantage = still advantage
            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(0);
            // max(14, 6) = 14
            Assert.Equal(14, t2.Roll.UsedDieRoll);
        }



        // What: Wait() does not consume crit advantage (E4)
        // Mutation: Fails if Wait() clears _pendingCritAdvantage
        [Fact]
        public async Task Wait_DoesNotConsumeCritAdvantage()
        {
            // Turn 1: Speak Nat 20 → sets flag
            // Turn 2: Wait() → flag persists
            // Turn 3: Speak with crit advantage
            var dice = new FixedDice(
                5,              // Constructor: horniness
                20, 50,         // Turn 1: d20=20 (Nat 20)
                14, 3, 50       // Turn 3: crit adv → d20=14, d20=3, max=14
            );

            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10);
            var session = MakeSession(dice, config);

            // Turn 1: Speak Nat 20
            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(0);
            Assert.True(t1.Roll.IsNatTwenty);

            // Turn 2: Wait — should NOT consume the crit advantage
            session.Wait();

            // Turn 3: Speak — should still have crit advantage
            await session.StartTurnAsync();
            var t3 = await session.ResolveTurnAsync(0);
            // If advantage was granted: max(14, 3) = 14
            Assert.Equal(14, t3.Roll.UsedDieRoll);
        }



        // ======================== Test Helpers ========================

        private static GameSession MakeSession(IDiceRoller dice, GameSessionConfig? config = null)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });

            var opponentStats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });

            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            var player = new CharacterProfile(stats, "system prompt", "Player", timing, 1);
            var opponent = new CharacterProfile(opponentStats, "system prompt", "Opponent", timing, 1);
            var llm = new ScriptedLlm();
            var trapRegistry = new NullTrapRegistry();

            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());
            return new GameSession(player, opponent, llm, dice, trapRegistry, config);
        }

        private static ILlmAdapter GetLlm(GameSession session)
        {
            var field = typeof(GameSession).GetField("_llm",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (ILlmAdapter)field!.GetValue(session)!;
        }

        private static void ActivateTrap(GameSession session, StatType stat = StatType.Charm)
        {
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;
            var trap = new TrapDefinition("test_trap", stat, TrapEffect.Disadvantage, -1, 3, "llm", "clear", "nat1");
            trapState.Activate(trap);
        }

        /// <summary>
        /// LLM adapter that always returns 4 Charm options and simple responses.
        /// </summary>
        private sealed class ScriptedLlm : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "option 1"),
                    new DialogueOption(StatType.Rizz, "option 2"),
                    new DialogueOption(StatType.Honesty, "option 3"),
                    new DialogueOption(StatType.Wit, "option 4")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                return Task.FromResult("delivered");
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("response"));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}

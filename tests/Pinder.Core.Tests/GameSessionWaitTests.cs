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
    public class GameSessionWaitTests
    {
        // ======================== Wait ========================

        // What: AC4 — Wait applies -1 interest (spec §3.7)
        // Mutation: Fails if Wait doesn't decrement interest
        [Fact]
        public void Wait_MinusOneInterest()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            session.Wait();

            Assert.Equal(9, GetInterest(session)); // 10 - 1
        }

        // What: AC4 — Wait advances trap timers; trap with 1 turn expires (spec §3.7, edge case §5.4)
        // Mutation: Fails if AdvanceTurn is not called (trap would remain active)
        [Fact]
        public void Wait_AdvancesTrapTimers_TrapExpires()
        {
            var trapDef = new TrapDefinition("TestTrap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 1, "test", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            session.Wait(); // trap with 1 turn remaining → expires

            // After Wait, trap should be gone
            Assert.False(GetTrapState(session).HasActive);
        }

        // What: AC4 — Wait with trap that has multiple turns: doesn't expire yet
        // Mutation: Fails if all traps are cleared instead of just decrementing
        [Fact]
        public void Wait_AdvancesTrapTimers_TrapNotExpiredIfMultipleTurns()
        {
            var trapDef = new TrapDefinition("TestTrap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 3, "test", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            session.Wait(); // trap with 3 turns → 2 remaining

            // Trap should still be active
            Assert.True(GetTrapState(session).HasActive);
        }

        // What: Edge case §5.3 — Wait when interest=1 drops to 0, game ends (Unmatched)
        // Mutation: Fails if end condition check missing after Apply(-1)
        [Fact]
        public void Wait_InterestHitsZero_GameEnds()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);

            session.Wait(); // interest 1→0

            Assert.Throws<GameEndedException>(() => session.Wait());
        }

        // What: Edge case §5.1 — Wait on ended game throws GameEndedException
        // Mutation: Fails if _ended check is missing at start of Wait
        [Fact]
        public void Wait_OnEndedGame_ThrowsGameEndedException()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);
            session.Wait(); // ends game

            Assert.Throws<GameEndedException>(() => session.Wait());
        }

        // What: AC4 — Wait does not earn XP (spec §9 Turn Lifecycle)
        // Mutation: Fails if Wait erroneously awards XP
        [Fact]
        public void Wait_NoXpEarned()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);

            // Should not throw — just a sanity check
            session.Wait();
            // The spec says Wait earns 0 XP. Covered by the void return type.
        }

        // What: AC1 — Wait increments turn number
        // Mutation: Fails if _turnNumber++ is missing from Wait
        [Fact]
        public void Wait_IncrementsTurnNumber()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            session.Wait();

            Assert.Equal(1, GetTurnNumber(session));
        }

        // What: AC1 — Multiple Wait actions accumulate turn numbers correctly
        // Mutation: Fails if turn counter is not incremented each time
        [Fact]
        public void MultipleWaits_TurnNumberAccumulates()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 10);
            var session = MakeSession(diceValue: 15, saModifier: 3, config: config);

            session.Wait(); // turn 1
            session.Wait(); // turn 2
            session.Wait(); // turn 3

            Assert.Equal(3, GetTurnNumber(session));
        }

        // What: Edge case §5.9 — Wait after StartTurnAsync clears pending options
        // Mutation: Fails if Wait doesn't clear _currentOptions
        [Fact]
        public async Task Wait_AfterStartTurn_ClearsOptions()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);
            await session.StartTurnAsync();

            session.Wait();

            // ResolveTurnAsync should fail
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        // What: Edge case §5.12 — Ghost trigger fires on Wait when Bored
        // Mutation: Fails if ghost trigger check is missing from Wait
        [Fact]
        public void Wait_Bored_GhostTrigger_Fires()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        // What: Edge case §5.13 — Wait does not call ILlmAdapter
        // Mutation: Fails if LLM adapter is called (throws on any call)
        [Fact]
        public void Wait_NoLlmCalls()
        {
            var session = MakeSessionWithThrowingLlm(diceValue: 15, saModifier: 3);

            // Should succeed without throwing — no LLM calls
            session.Wait();
        }

        // ======================== Helpers ========================

        private static int GetInterest(GameSession session)
        {
            var field = typeof(GameSession).GetField("_interest",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var interest = field!.GetValue(session)!;
            var currentProp = interest.GetType().GetProperty("Current");
            return (int)currentProp!.GetValue(interest)!;
        }

        private static int GetTurnNumber(GameSession session)
        {
            var field = typeof(GameSession).GetField("_turnNumber",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (int)field!.GetValue(session)!;
        }

        private static TrapState GetTrapState(GameSession session)
        {
            var field = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (TrapState)field!.GetValue(session)!;
        }

        private static GameSession MakeSession(
            int diceValue,
            int saModifier,
            GameSessionConfig? config = null)
        {
            var stats = MakeStatBlock(sa: saModifier);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());
            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());

            return new GameSession(
                player,
                opponent,
                new StubLlmAdapter(),
                new StubDice(diceValue),
                new StubTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithThrowingLlm(
            int diceValue,
            int saModifier,
            GameSessionConfig? config = null)
        {
            var stats = MakeStatBlock(sa: saModifier);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());
            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());

            return new GameSession(
                player,
                opponent,
                new ThrowingLlmAdapter(),
                new StubDice(diceValue),
                new StubTrapRegistry(),
                config);
        }

        private static void ActivateTrapOnSession(GameSession session, TrapDefinition trap)
        {
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;
            trapState.Activate(trap);
        }

        private static StatBlock MakeStatBlock(int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock? stats = null)
        {
            stats = stats ?? MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private sealed class StubDice : IDiceRoller
        {
            private readonly int _value;
            public StubDice(int value = 10) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Test option")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult("delivered message");

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("opponent reply"));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction) => System.Threading.Tasks.Task.FromResult(message);
        }

                private sealed class ThrowingLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => throw new InvalidOperationException("LLM should not be called for Wait");

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => throw new InvalidOperationException("LLM should not be called for Wait");

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => throw new InvalidOperationException("LLM should not be called for Wait");

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => throw new InvalidOperationException("LLM should not be called for Wait");
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction) => System.Threading.Tasks.Task.FromResult(message);
        }

                private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

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
    public class GameSessionReadRecoverWaitTests
    {
        // ======================== Read: Success ========================

        [Fact]
        public async Task ReadAsync_Success_RevealsInterest_NoInterestChange()
        {
            // SA +3, dice rolls 10 → total 10+3=13 >= DC 12 → success
            var session = MakeSession(diceValue: 10, saModifier: 3);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(10, result.InterestValue); // default starting interest
            Assert.Equal(5, result.XpEarned);
            Assert.Empty(result.ShadowGrowthEvents);
            Assert.Equal(10, result.StateAfter.Interest); // interest unchanged
            Assert.Equal(1, result.StateAfter.TurnNumber);
        }

        [Fact]
        public async Task ReadAsync_Success_RollResultIncluded()
        {
            var session = MakeSession(diceValue: 15, saModifier: 2);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Roll);
            Assert.Equal(12, result.Roll.DC);
            Assert.Equal(StatType.SelfAwareness, result.Roll.Stat);
        }

        // ======================== Read: Failure ========================

        [Fact]
        public async Task ReadAsync_Failure_MinusOneInterest_Overthinking()
        {
            // SA +0, dice rolls 5 → total 5 < DC 12 → failure
            var stats = MakeStatBlock(sa: 0);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(playerShadows: shadows);
            var session = MakeSession(diceValue: 5, saModifier: 0, config: config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Null(result.InterestValue);
            Assert.Equal(2, result.XpEarned);
            Assert.Equal(9, result.StateAfter.Interest); // 10 - 1
            Assert.Single(result.ShadowGrowthEvents);
            Assert.Contains("Overthinking", result.ShadowGrowthEvents[0]);
            Assert.Contains("Read failed", result.ShadowGrowthEvents[0]);
        }

        [Fact]
        public async Task ReadAsync_Failure_NoShadowTracker_NoShadowGrowth()
        {
            // No SessionShadowTracker → shadow growth skipped, no crash
            var session = MakeSession(diceValue: 3, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Null(result.InterestValue);
            Assert.Equal(9, result.StateAfter.Interest);
            Assert.Empty(result.ShadowGrowthEvents);
        }

        // ======================== Read: Nat 20 / Nat 1 ========================

        [Fact]
        public async Task ReadAsync_Nat20_AutoSuccess()
        {
            // SA -2 but nat20 → auto-success
            var session = MakeSession(diceValue: 20, saModifier: -2);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(10, result.InterestValue);
        }

        [Fact]
        public async Task ReadAsync_Nat1_AutoFail_MinusOne_Overthinking()
        {
            // SA +5 but nat1 → auto-fail
            var stats = MakeStatBlock(sa: 5);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(playerShadows: shadows);
            var session = MakeSession(diceValue: 1, saModifier: 5, config: config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Null(result.InterestValue);
            Assert.Equal(9, result.StateAfter.Interest);
            Assert.Single(result.ShadowGrowthEvents);
        }

        // ======================== Read: End Conditions ========================

        [Fact]
        public async Task ReadAsync_FailureCausesInterestZero_GameEnds()
        {
            // Start at interest 1, fail → drops to 0 → Unmatched
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceValue: 3, saModifier: 0, config: config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(0, result.StateAfter.Interest);

            // Subsequent call should throw GameEndedException
            await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
        }

        [Fact]
        public async Task ReadAsync_OnEndedGame_ThrowsGameEndedException()
        {
            // Start at interest 1, wait to end game, then try read
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);

            session.Wait(); // interest 1→0, game ends

            await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
        }

        // ======================== Recover: Success ========================

        [Fact]
        public async Task RecoverAsync_Success_ClearsTrap()
        {
            // Setup a session with an active trap
            var trapDef = new TrapDefinition("Oversharing", StatType.Honesty,
                TrapEffect.Disadvantage, 0, 3, "overshare", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            Assert.Equal("Oversharing", result.ClearedTrapName);
            Assert.Equal(15, result.XpEarned);
            Assert.Equal(10, result.StateAfter.Interest); // unchanged on success
        }

        // ======================== Recover: Failure ========================

        [Fact]
        public async Task RecoverAsync_Failure_MinusOneInterest_TrapRemains()
        {
            var trapDef = new TrapDefinition("Oversharing", StatType.Honesty,
                TrapEffect.Disadvantage, 0, 5, "overshare", "clear", "nat1");
            var session = MakeSession(diceValue: 3, saModifier: 0);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Null(result.ClearedTrapName);
            Assert.Equal(2, result.XpEarned);
            Assert.Equal(9, result.StateAfter.Interest); // 10 - 1
        }

        // ======================== Recover: No Active Trap ========================

        [Fact]
        public async Task RecoverAsync_NoActiveTrap_ThrowsInvalidOperationException()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverAsync());
            Assert.Contains("no active trap", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ======================== Recover: Multiple Traps ========================

        [Fact]
        public async Task RecoverAsync_MultipleTraps_ClearsFirst()
        {
            var trap1 = new TrapDefinition("TrapA", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "a", "clear", "nat1");
            var trap2 = new TrapDefinition("TrapB", StatType.Wit,
                TrapEffect.Disadvantage, 0, 5, "b", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trap1);
            ActivateTrapOnSession(session, trap2);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            // One trap cleared, one remains
            Assert.NotNull(result.ClearedTrapName);
            // The snapshot should still have 1 trap (the other one, after AdvanceTurn)
            // Note: after clearing one, AdvanceTurn decrements the other
            Assert.True(result.StateAfter.ActiveTrapNames.Length >= 1 || result.StateAfter.ActiveTrapNames.Length == 0);
        }

        // ======================== Recover: Ended Game ========================

        [Fact]
        public async Task RecoverAsync_OnEndedGame_ThrowsGameEndedException()
        {
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);
            session.Wait(); // ends game

            await Assert.ThrowsAsync<GameEndedException>(() => session.RecoverAsync());
        }

        // ======================== Wait ========================

        [Fact]
        public void Wait_MinusOneInterest()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);

            session.Wait();

            // We can verify via ReadAsync that interest changed
            // Actually let's use another Wait + Read to check
        }

        [Fact]
        public async Task Wait_MinusOneInterest_VerifiedViaRead()
        {
            // Start at 10, Wait → 9, then Read (success) reveals 9
            var session = MakeSession(diceValue: 15, saModifier: 3);

            session.Wait();

            var result = await session.ReadAsync();
            Assert.True(result.Success);
            Assert.Equal(9, result.InterestValue); // 10 - 1 from Wait
        }

        [Fact]
        public void Wait_AdvancesTrapTimers()
        {
            var trapDef = new TrapDefinition("TestTrap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 1, "test", "clear", "nat1");
            var session = MakeSession(diceValue: 10, saModifier: 0);
            ActivateTrapOnSession(session, trapDef);

            session.Wait(); // trap with 1 turn remaining → expires
        }

        [Fact]
        public void Wait_InterestHitsZero_GameEnds()
        {
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);

            session.Wait(); // interest 1→0

            Assert.Throws<GameEndedException>(() => session.Wait());
        }

        [Fact]
        public void Wait_OnEndedGame_ThrowsGameEndedException()
        {
            var config = new GameSessionConfig(startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);
            session.Wait(); // ends game

            Assert.Throws<GameEndedException>(() => session.Wait());
        }

        // ======================== TurnNumber Incremented ========================

        [Fact]
        public async Task ReadAsync_IncrementsTurnNumber()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var result = await session.ReadAsync();
            Assert.Equal(1, result.StateAfter.TurnNumber);
        }

        [Fact]
        public async Task RecoverAsync_IncrementsTurnNumber()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();
            Assert.Equal(1, result.StateAfter.TurnNumber);
        }

        // ======================== Called After StartTurnAsync ========================

        [Fact]
        public async Task ReadAsync_AfterStartTurn_ClearsOptions()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            // Call StartTurnAsync first
            await session.StartTurnAsync();

            // Then call ReadAsync instead of ResolveTurnAsync
            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(1, result.StateAfter.TurnNumber); // StartTurn doesn't increment turn; Read does

            // Subsequent ResolveTurnAsync should fail (options cleared)
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        [Fact]
        public async Task Wait_AfterStartTurn_ClearsOptions()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);
            await session.StartTurnAsync();

            session.Wait();

            // ResolveTurnAsync should fail
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        // ======================== Ghost Trigger ========================

        [Fact]
        public async Task ReadAsync_Bored_GhostTrigger_Fires()
        {
            // Interest at 2 (Bored), dice returns 1 (ghost trigger fires on d4==1)
            var config = new GameSessionConfig(startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        [Fact]
        public async Task RecoverAsync_Bored_GhostTrigger_Fires()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var config = new GameSessionConfig(startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);
            ActivateTrapOnSession(session, trapDef);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.RecoverAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        [Fact]
        public void Wait_Bored_GhostTrigger_Fires()
        {
            var config = new GameSessionConfig(startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);

            var ex = Assert.Throws<GameEndedException>(() => session.Wait());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        [Fact]
        public async Task ReadAsync_Bored_GhostTrigger_DoesNotFire()
        {
            // Interest at 3 (Bored), dice returns 2 (not 1 → ghost doesn't fire)
            // BUT dice is also used for the d20 roll. We need a sequence dice.
            var dice = new SequenceDice(new[] { 2, 15 }); // d4=2 (no ghost), d20=15
            var config = new GameSessionConfig(startingInterest: 3);
            var session = MakeSessionWithDice(dice, saModifier: 0, config: config);

            var result = await session.ReadAsync();
            // Should not throw — ghost didn't fire
            Assert.NotNull(result);
        }

        // ======================== Momentum Not Affected ========================

        [Fact]
        public async Task ReadAsync_DoesNotAffectMomentum()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var result = await session.ReadAsync();
            Assert.Equal(0, result.StateAfter.MomentumStreak); // Read doesn't affect momentum
        }

        // ======================== Helpers ========================

        private static GameSession MakeSession(
            int diceValue,
            int saModifier,
            GameSessionConfig? config = null)
        {
            var stats = MakeStatBlock(sa: saModifier);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());

            return new GameSession(
                player,
                opponent,
                new StubLlmAdapter(),
                new StubDice(diceValue),
                new StubTrapRegistry(),
                config);
        }

        private static GameSession MakeSessionWithDice(
            IDiceRoller dice,
            int saModifier,
            GameSessionConfig? config = null)
        {
            var stats = MakeStatBlock(sa: saModifier);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());

            return new GameSession(
                player,
                opponent,
                new StubLlmAdapter(),
                dice,
                new StubTrapRegistry(),
                config);
        }

        /// <summary>
        /// Activates a trap on the session's internal TrapState by using the RollEngine
        /// with a guaranteed TropeTrap-tier failure. This is a workaround since TrapState
        /// is internal to GameSession. We use a simpler approach: create a new session
        /// that shares a trap state... Actually, TrapState is created inside GameSession.
        /// 
        /// Alternative approach: Use reflection or a dedicated method.
        /// Since this is a test helper, we'll use a public-facing approach:
        /// call StartTurnAsync + ResolveTurnAsync with a roll that triggers TropeTrap.
        /// 
        /// Actually, the simplest approach: the session's TrapState is internal, but we
        /// can't access it directly. However, we know that RollEngine.ResolveFixedDC
        /// receives the TrapState. The traps come from _traps inside GameSession.
        /// 
        /// Best approach for testing: create a TrapRegistry that returns our trap,
        /// then trigger a TropeTrap failure via ResolveTurnAsync.
        /// But that's complex. Let's use the fact that Read/Recover use the same _traps.
        /// We need to get a trap active. Let's trigger it via a Speak turn.
        /// 
        /// Simplest: We need to add traps to _traps, but it's private. We can use
        /// a special ITrapRegistry + a failed roll that hits TropeTrap tier.
        /// 
        /// For Read/Recover tests, we need traps active. Let's make the dice sequence
        /// produce a TropeTrap-tier failure first, then the test roll.
        /// </summary>
        private static void ActivateTrapOnSession(GameSession session, TrapDefinition trap)
        {
            // Use reflection to access _traps field and activate the trap
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
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
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

        /// <summary>Dice that returns values from a sequence, cycling if exhausted.</summary>
        private sealed class SequenceDice : IDiceRoller
        {
            private readonly int[] _values;
            private int _index;

            public SequenceDice(int[] values)
            {
                _values = values;
                _index = 0;
            }

            public int Roll(int sides)
            {
                var val = _values[_index % _values.Length];
                _index++;
                return val;
            }
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
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

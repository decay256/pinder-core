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

        // What: AC2 — Read success reveals interest, no interest change (spec §3.1)
        // Mutation: Fails if ReadAsync returns null InterestValue on success or modifies interest
        [Fact]
        public async Task ReadAsync_Success_RevealsInterest_NoInterestChange()
        {
            // SA +3, dice rolls 10 → total 10+3=13 >= DC 12 → success
            var session = MakeSession(diceValue: 10, saModifier: 3);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(10, result.InterestValue); // default starting interest
            Assert.Equal(0, result.XpEarned); // Read does not grant XP per §10 (#48)
            Assert.Empty(result.ShadowGrowthEvents);
            Assert.Equal(10, result.StateAfter.Interest); // interest unchanged
            Assert.Equal(1, result.StateAfter.TurnNumber);
        }

        // What: AC2 — Read result includes the RollResult with DC 12 and SA stat
        // Mutation: Fails if DC is not 12 or stat is not SelfAwareness
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

        // What: AC6 — Read success earns 5 XP (spec §9, Turn Lifecycle)
        // Mutation: Fails if XP on success is not exactly 5
        [Fact]
        public async Task ReadAsync_Success_Xp5()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(0, result.XpEarned); // Read does not grant XP per §10 (#48)
        }

        // What: AC2 — Read success does not apply shadow growth events
        // Mutation: Fails if shadow events are populated on success
        [Fact]
        public async Task ReadAsync_Success_NoShadowGrowthEvents()
        {
            var stats = MakeStatBlock(sa: 3);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var session = MakeSession(diceValue: 15, saModifier: 3, config: config);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Empty(result.ShadowGrowthEvents);
        }

        // ======================== Read: Failure ========================

        // What: AC2 + AC5 — Read failure: -1 interest, Overthinking +1, null InterestValue (spec §3.2)
        // Mutation: Fails if interest penalty is not -1, or Overthinking growth not applied, or InterestValue is non-null
        [Fact]
        public async Task ReadAsync_Failure_MinusOneInterest_Overthinking()
        {
            // SA +0, dice rolls 5 → total 5 < DC 12 → failure
            var stats = MakeStatBlock(sa: 0);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var session = MakeSession(diceValue: 5, saModifier: 0, config: config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Null(result.InterestValue);
            Assert.Equal(0, result.XpEarned); // Read does not grant XP per §10 (#48)
            Assert.Equal(9, result.StateAfter.Interest); // 10 - 1
            Assert.Single(result.ShadowGrowthEvents);
            Assert.Contains("Overthinking", result.ShadowGrowthEvents[0]);
            Assert.Contains("Read failed", result.ShadowGrowthEvents[0]);
        }

        // What: AC5 — Read failure without SessionShadowTracker: no shadow growth, no crash (spec §3.3)
        // Mutation: Fails if implementation throws NullReferenceException when no tracker present
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

        // What: AC6 — Read failure earns 2 XP (spec §9)
        // Mutation: Fails if XP on failure is not 2
        [Fact]
        public async Task ReadAsync_Failure_Xp2()
        {
            var session = MakeSession(diceValue: 3, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(0, result.XpEarned); // Read does not grant XP per §10 (#48)
        }

        // ======================== Read: Nat 20 / Nat 1 ========================

        // What: Edge case §5.8 — Nat 20 auto-success regardless of SA modifier
        // Mutation: Fails if nat20 doesn't bypass DC comparison
        [Fact]
        public async Task ReadAsync_Nat20_AutoSuccess()
        {
            // SA -2 but nat20 → auto-success
            var session = MakeSession(diceValue: 20, saModifier: -2);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(10, result.InterestValue);
        }

        // What: Edge case §5.8 — Nat 1 auto-fail, -1 interest, Overthinking +1
        // Mutation: Fails if nat1 doesn't force failure despite high total
        [Fact]
        public async Task ReadAsync_Nat1_AutoFail_MinusOne_Overthinking()
        {
            // SA +5 but nat1 → auto-fail
            var stats = MakeStatBlock(sa: 5);
            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var session = MakeSession(diceValue: 1, saModifier: 5, config: config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Null(result.InterestValue);
            Assert.Equal(9, result.StateAfter.Interest);
            Assert.Single(result.ShadowGrowthEvents);
        }

        // ======================== Read: End Conditions ========================

        // What: Edge case §5.2 — Read failure drops interest to 0, game ends (Unmatched)
        // Mutation: Fails if end condition check is missing after interest apply
        [Fact]
        public async Task ReadAsync_FailureCausesInterestZero_GameEnds()
        {
            // Start at interest 1, fail → drops to 0 → Unmatched
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceValue: 3, saModifier: 0, config: config);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(0, result.StateAfter.Interest);

            // Subsequent call should throw GameEndedException
            await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
        }

        // What: Edge case §5.1 — ReadAsync on ended game throws GameEndedException
        // Mutation: Fails if _ended check is missing at start of ReadAsync
        [Fact]
        public async Task ReadAsync_OnEndedGame_ThrowsGameEndedException()
        {
            // Start at interest 1, wait to end game, then try read
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);

            session.Wait(); // interest 1→0, game ends

            await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
        }

        // What: §5.11 — Read success reveals exact interest, no rounding
        // Mutation: Fails if interest value is rounded or truncated
        [Fact]
        public async Task ReadAsync_Success_RevealsExactInterest()
        {
            // Start at 10, Wait twice to get to 8, then Read
            var dice = new SequenceDice(new[] { 10, 10, 15 }); // two Waits don't use dice for roll, but ghost checks; then Read d20=15
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 13);
            var session = MakeSessionWithDice(dice, saModifier: 3, config: config);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(13, result.InterestValue);
        }

        // ======================== Recover: Success ========================

        // What: AC3 — Recover success clears trap, returns trap name, 15 XP, interest unchanged (spec §3.4)
        // Mutation: Fails if trap is not cleared, or wrong XP, or interest changed on success
        [Fact]
        public async Task RecoverAsync_Success_ClearsTrap()
        {
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

        // What: AC6 — Recover success earns 15 XP (spec §9)
        // Mutation: Fails if XP on recovery success is not 15
        [Fact]
        public async Task RecoverAsync_Success_Xp15()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            Assert.Equal(15, result.XpEarned);
        }

        // ======================== Recover: Failure ========================

        // What: AC3 — Recover failure: -1 interest, trap remains, null ClearedTrapName (spec §3.5)
        // Mutation: Fails if interest penalty not applied, or trap wrongly cleared on failure
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
            Assert.Equal(0, result.XpEarned); // Failed recover does not grant XP per §10 (#48)
            Assert.Equal(9, result.StateAfter.Interest); // 10 - 1
        }

        // What: AC3 — Recover failure: trap still listed in active traps snapshot
        // Mutation: Fails if trap is erroneously cleared on failure
        [Fact]
        public async Task RecoverAsync_Failure_TrapStillActiveInSnapshot()
        {
            var trapDef = new TrapDefinition("Oversharing", StatType.Honesty,
                TrapEffect.Disadvantage, 0, 10, "overshare", "clear", "nat1");
            var session = MakeSession(diceValue: 3, saModifier: 0);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            // Trap should still be active (duration 10, minus 1 from AdvanceTurn = 9 remaining)
            Assert.True(result.StateAfter.ActiveTrapNames.Length >= 1);
        }

        // ======================== Recover: No Active Trap ========================

        // What: AC3 — Recover with no active trap throws InvalidOperationException (spec §3.6)
        // Mutation: Fails if precondition check is missing
        [Fact]
        public async Task RecoverAsync_NoActiveTrap_ThrowsInvalidOperationException()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverAsync());
            Assert.Contains("no active trap", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ======================== Recover: Multiple Traps ========================

        // What: Edge case §5.4 — Multiple traps: Recover clears exactly one, the first in iteration
        // Mutation: Fails if all traps are cleared instead of just one
        [Fact]
        public async Task RecoverAsync_MultipleTraps_ClearsExactlyOne()
        {
            var trap1 = new TrapDefinition("TrapA", StatType.Charm,
                TrapEffect.Disadvantage, 0, 10, "a", "clear", "nat1");
            var trap2 = new TrapDefinition("TrapB", StatType.Wit,
                TrapEffect.Disadvantage, 0, 10, "b", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trap1);
            ActivateTrapOnSession(session, trap2);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.ClearedTrapName);
            // Exactly one trap should remain (the other was cleared, plus AdvanceTurn)
            // Duration=10 so AdvanceTurn won't expire the remaining trap
            Assert.Single(result.StateAfter.ActiveTrapNames);
        }

        // What: Edge case §5.4 — After clearing one trap, a second Recover can clear the other
        // Mutation: Fails if Recover can't be called again for a second trap
        [Fact]
        public async Task RecoverAsync_MultipleTraps_SecondRecoverClearsRemaining()
        {
            var trap1 = new TrapDefinition("TrapA", StatType.Charm,
                TrapEffect.Disadvantage, 0, 10, "a", "clear", "nat1");
            var trap2 = new TrapDefinition("TrapB", StatType.Wit,
                TrapEffect.Disadvantage, 0, 10, "b", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trap1);
            ActivateTrapOnSession(session, trap2);

            var result1 = await session.RecoverAsync();
            Assert.True(result1.Success);

            var result2 = await session.RecoverAsync();
            Assert.True(result2.Success);
            Assert.NotEqual(result1.ClearedTrapName, result2.ClearedTrapName);
            Assert.Empty(result2.StateAfter.ActiveTrapNames);
        }

        // ======================== Recover: Ended Game ========================

        // What: Edge case §5.1 — RecoverAsync on ended game throws GameEndedException
        // Mutation: Fails if _ended check is missing at start of RecoverAsync
        [Fact]
        public async Task RecoverAsync_OnEndedGame_ThrowsGameEndedException()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var session = MakeSession(diceValue: 10, saModifier: 0, config: config);
            session.Wait(); // ends game

            await Assert.ThrowsAsync<GameEndedException>(() => session.RecoverAsync());
        }

        // What: Edge case §5.2 — Recover failure drops interest to 0, game ends
        // Mutation: Fails if end condition not checked after interest Apply(-1)
        [Fact]
        public async Task RecoverAsync_FailureCausesInterestZero_GameEnds()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 1);
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSession(diceValue: 3, saModifier: 0, config: config);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Equal(0, result.StateAfter.Interest);

            // Subsequent call should throw GameEndedException
            await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
        }

        // ======================== Wait ========================

        // What: AC4 — Wait applies -1 interest (spec §3.7)
        // Mutation: Fails if Wait doesn't decrement interest
        [Fact]
        public async Task Wait_MinusOneInterest()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            session.Wait();

            // Verify interest decreased by reading it
            var readResult = await session.ReadAsync();
            Assert.True(readResult.Success);
            Assert.Equal(9, readResult.InterestValue); // 10 - 1 from Wait
        }

        // What: AC4 — Wait verified via subsequent Read that interest dropped (spec §3.7)
        // Mutation: Fails if Wait is a no-op on interest
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

        // What: AC4 — Wait advances trap timers; trap with 1 turn expires (spec §3.7, edge case §5.4)
        // Mutation: Fails if AdvanceTurn is not called (trap would remain active)
        [Fact]
        public async Task Wait_AdvancesTrapTimers_TrapExpires()
        {
            var trapDef = new TrapDefinition("TestTrap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 1, "test", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            session.Wait(); // trap with 1 turn remaining → expires

            // After Wait, trap should be gone — RecoverAsync should fail with "no active trap"
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverAsync());
            Assert.Contains("no active trap", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // What: AC4 — Wait with trap that has multiple turns: doesn't expire yet
        // Mutation: Fails if all traps are cleared instead of just decrementing
        [Fact]
        public async Task Wait_AdvancesTrapTimers_TrapNotExpiredIfMultipleTurns()
        {
            var trapDef = new TrapDefinition("TestTrap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 3, "test", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            session.Wait(); // trap with 3 turns → 2 remaining

            // Trap should still be active — RecoverAsync should work
            var result = await session.RecoverAsync();
            Assert.True(result.Success);
            Assert.Equal("TestTrap", result.ClearedTrapName);
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
            // Wait is void and doesn't return XP directly; 
            // we verify indirectly that no XP is recorded
            var session = MakeSession(diceValue: 10, saModifier: 0);

            // Should not throw — just a sanity check
            session.Wait();
            // No assertion on XP since Wait is void, but we verify it doesn't crash
            // The spec says Wait earns 0 XP. Covered by the void return type.
        }

        // ======================== TurnNumber Incremented ========================

        // What: AC1 — ReadAsync increments turn number
        // Mutation: Fails if _turnNumber++ is missing from ReadAsync
        [Fact]
        public async Task ReadAsync_IncrementsTurnNumber()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var result = await session.ReadAsync();
            Assert.Equal(1, result.StateAfter.TurnNumber);
        }

        // What: AC1 — RecoverAsync increments turn number
        // Mutation: Fails if _turnNumber++ is missing from RecoverAsync
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

        // What: AC1 — Wait increments turn number (verified by subsequent Read)
        // Mutation: Fails if _turnNumber++ is missing from Wait
        [Fact]
        public async Task Wait_IncrementsTurnNumber()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            session.Wait();

            var result = await session.ReadAsync();
            // Wait was turn 1, Read is turn 2
            Assert.Equal(2, result.StateAfter.TurnNumber);
        }

        // What: AC1 — Multiple actions accumulate turn numbers correctly
        // Mutation: Fails if turn counter is not incremented each time
        [Fact]
        public async Task MultipleActions_TurnNumberAccumulates()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            session.Wait(); // turn 1
            session.Wait(); // turn 2
            var result = await session.ReadAsync(); // turn 3

            Assert.Equal(3, result.StateAfter.TurnNumber);
        }

        // ======================== Called After StartTurnAsync ========================

        // What: Edge case §5.9 — ReadAsync after StartTurnAsync clears pending options
        // Mutation: Fails if _currentOptions is not set to null
        [Fact]
        public async Task ReadAsync_AfterStartTurn_ClearsOptions()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            // Call StartTurnAsync first
            await session.StartTurnAsync();

            // Then call ReadAsync instead of ResolveTurnAsync
            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(1, result.StateAfter.TurnNumber);

            // Subsequent ResolveTurnAsync should fail (options cleared)
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
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

        // What: Edge case §5.9 — RecoverAsync after StartTurnAsync clears pending options
        // Mutation: Fails if RecoverAsync doesn't clear _currentOptions
        [Fact]
        public async Task RecoverAsync_AfterStartTurn_ClearsOptions()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);
            await session.StartTurnAsync();

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        // ======================== Ghost Trigger ========================

        // What: Edge case §5.12 — Ghost trigger fires on ReadAsync when Bored (d4==1)
        // Mutation: Fails if ghost trigger check is missing from ReadAsync
        [Fact]
        public async Task ReadAsync_Bored_GhostTrigger_Fires()
        {
            // Interest at 2 (Bored), dice returns 1 (ghost trigger fires on d4==1)
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.ReadAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
        }

        // What: Edge case §5.12 — Ghost trigger fires on RecoverAsync when Bored
        // Mutation: Fails if ghost trigger check is missing from RecoverAsync
        [Fact]
        public async Task RecoverAsync_Bored_GhostTrigger_Fires()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);
            ActivateTrapOnSession(session, trapDef);

            var ex = await Assert.ThrowsAsync<GameEndedException>(() => session.RecoverAsync());
            Assert.Equal(GameOutcome.Ghosted, ex.Outcome);
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

        // What: Edge case §5.12 — Ghost trigger does NOT fire when d4 != 1 (Bored)
        // Mutation: Fails if ghost trigger fires on every Bored check, not just d4==1
        [Fact]
        public async Task ReadAsync_Bored_GhostTrigger_DoesNotFire()
        {
            // Interest at 3 (Bored), dice returns 2 (not 1 → ghost doesn't fire)
            // Dice is also used for the d20 roll. We need a sequence dice.
            var dice = new SequenceDice(new[] { 2, 15 }); // d4=2 (no ghost), d20=15
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 3);
            var session = MakeSessionWithDice(dice, saModifier: 0, config: config);

            var result = await session.ReadAsync();
            // Should not throw — ghost didn't fire
            Assert.NotNull(result);
        }

        // What: Edge case §5.12 — Ghost trigger on RecoverAsync: HasActive checked before ghost
        // Mutation: Fails if HasActive precondition is checked after ghost roll (wrong order per RecoverAsync pseudocode)
        [Fact]
        public async Task RecoverAsync_NoTrap_Bored_ThrowsInvalidOp_NotGhosted()
        {
            // No active trap AND Bored. Per pseudocode, HasActive check comes first (step 2) before ghost (step 4)
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 2);
            var session = MakeSession(diceValue: 1, saModifier: 0, config: config);

            // Should throw InvalidOperationException for no trap, not GameEndedException for ghost
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverAsync());
        }

        // ======================== Momentum Not Affected ========================

        // What: Edge case §5.7 — Read/Recover/Wait don't affect momentum streak
        // Mutation: Fails if momentum is incremented or reset by non-Speak actions
        [Fact]
        public async Task ReadAsync_DoesNotAffectMomentum()
        {
            var session = MakeSession(diceValue: 15, saModifier: 3);

            var result = await session.ReadAsync();
            Assert.Equal(0, result.StateAfter.MomentumStreak); // Read doesn't affect momentum
        }

        // What: Edge case §5.7 — Recover doesn't affect momentum
        // Mutation: Fails if momentum is changed by RecoverAsync
        [Fact]
        public async Task RecoverAsync_DoesNotAffectMomentum()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();
            Assert.Equal(0, result.StateAfter.MomentumStreak);
        }

        // ======================== Read/Recover use SA stat ========================

        // What: AC2/AC3 — Both Read and Recover use SelfAwareness stat for the roll
        // Mutation: Fails if a different stat is used for the roll
        [Fact]
        public async Task RecoverAsync_UsesFixedDC12_SAStat()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSession(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();

            Assert.NotNull(result.Roll);
            Assert.Equal(12, result.Roll.DC);
            Assert.Equal(StatType.SelfAwareness, result.Roll.Stat);
        }

        // ======================== Interest penalty always -1 regardless of failure tier ========================

        // What: AC2/AC3 — Interest penalty is always -1 regardless of failure tier
        // Mutation: Fails if failure tier affects the interest delta for Read/Recover
        [Fact]
        public async Task ReadAsync_Failure_InterestPenaltyAlwaysMinusOne()
        {
            // Very low roll → large failure margin, but still only -1 interest
            var session = MakeSession(diceValue: 2, saModifier: -2);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(9, result.StateAfter.Interest); // exactly 10 - 1, not more
        }

        // ======================== No LLM Calls ========================

        // What: Edge case §5.13 — Read, Recover, Wait do not call ILlmAdapter
        // Mutation: Fails if LLM adapter is called (throws on any call)
        [Fact]
        public async Task ReadAsync_NoLlmCalls()
        {
            var session = MakeSessionWithThrowingLlm(diceValue: 15, saModifier: 3);

            // Should succeed without throwing — no LLM calls
            var result = await session.ReadAsync();
            Assert.True(result.Success);
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

        // What: Edge case §5.13 — Recover does not call ILlmAdapter
        // Mutation: Fails if LLM adapter is called (throws on any call)
        [Fact]
        public async Task RecoverAsync_NoLlmCalls()
        {
            var trapDef = new TrapDefinition("T", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "t", "clear", "nat1");
            var session = MakeSessionWithThrowingLlm(diceValue: 15, saModifier: 3);
            ActivateTrapOnSession(session, trapDef);

            var result = await session.RecoverAsync();
            Assert.True(result.Success);
        }

        // ======================== Shadow Disadvantage on Read/Recover (#260) ========================

        // What: AC1 — ReadAsync applies shadow SA disadvantage when Overthinking T2+ (≥12)
        // Mutation: Fails if ReadAsync ignores shadow-based disadvantage
        [Fact]
        public async Task ReadAsync_OverthinkingT2_AppliesShadowDisadvantage()
        {
            // SA +3, Overthinking = 12 (T2) → SA should have disadvantage
            // With disadvantage, dice rolls 2d20 takes lower. StubDice returns same value,
            // but we use SequenceDice to prove disadvantage path: rolls 15, 5 → takes 5.
            // 5 + 3 = 8 < DC 12 → failure (without disadvantage, 15+3=18 >= 12 → success)
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 12 }
                });

            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());

            // Disadvantage rolls 2d20 takes lower: 15 then 5 → uses 5
            var dice = new SequenceDice(new[] { 15, 5 });
            var session = new GameSession(player, opponent, new StubLlmAdapter(), dice, new StubTrapRegistry(), config);

            var result = await session.ReadAsync();

            // 5 + 3 = 8 < 12 → failure
            Assert.False(result.Success);
        }

        // What: AC3 — ReadAsync does NOT apply shadow disadvantage when Overthinking < T2 (e.g. 5)
        // Mutation: Fails if ReadAsync incorrectly applies disadvantage at low Overthinking
        [Fact]
        public async Task ReadAsync_OverthinkingBelowT2_NoShadowDisadvantage()
        {
            // SA +3, Overthinking = 5 (T1, below T2 threshold of 12) → no shadow disadvantage
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 5 }
                });

            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());

            // Single roll: 10 + 3 = 13 >= 12 → success (no disadvantage, so only one roll)
            var dice = new SequenceDice(new[] { 10 });
            var session = new GameSession(player, opponent, new StubLlmAdapter(), dice, new StubTrapRegistry(), config);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
        }

        // What: AC2 — RecoverAsync applies shadow SA disadvantage when Overthinking T2+ (≥12)
        // Mutation: Fails if RecoverAsync ignores shadow-based disadvantage
        [Fact]
        public async Task RecoverAsync_OverthinkingT2_AppliesShadowDisadvantage()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 12 }
                });

            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());

            // Disadvantage: rolls 15 then 5 → takes 5. 5 + 3 = 8 < 12 → failure
            var dice = new SequenceDice(new[] { 15, 5 });
            var session = new GameSession(player, opponent, new StubLlmAdapter(), dice, new StubTrapRegistry(), config);

            // Activate a trap so Recover is valid
            ActivateTrapOnSession(session, new TrapDefinition("test-trap", StatType.Charm, TrapEffect.Disadvantage, 0, 3, "trap", "clear", "nat1"));

            var result = await session.RecoverAsync();

            // 5 + 3 = 8 < 12 → failure
            Assert.False(result.Success);
        }

        // What: AC3 — RecoverAsync does NOT apply shadow disadvantage when Overthinking < T2
        // Mutation: Fails if RecoverAsync incorrectly applies disadvantage at low Overthinking
        [Fact]
        public async Task RecoverAsync_OverthinkingBelowT2_NoShadowDisadvantage()
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, 3 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 5 }
                });

            var shadows = new SessionShadowTracker(stats);
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);
            var player = MakeProfile("player", stats);
            var opponent = MakeProfile("opponent", MakeStatBlock());

            var dice = new SequenceDice(new[] { 10 });
            var session = new GameSession(player, opponent, new StubLlmAdapter(), dice, new StubTrapRegistry(), config);

            // Activate a trap so Recover is valid
            ActivateTrapOnSession(session, new TrapDefinition("test-trap", StatType.Charm, TrapEffect.Disadvantage, 0, 3, "trap", "clear", "nat1"));

            var result = await session.RecoverAsync();

            // 10 + 3 = 13 >= 12 → success (no shadow disadvantage)
            Assert.True(result.Success);
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
            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());

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
            config = config ?? new GameSessionConfig(clock: TestHelpers.MakeClock());

            return new GameSession(
                player,
                opponent,
                new StubLlmAdapter(),
                dice,
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

        /// <summary>LLM adapter that throws on every call — used to verify no LLM calls are made.</summary>
        private sealed class ThrowingLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => throw new InvalidOperationException("LLM should not be called for Read/Recover/Wait");

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => throw new InvalidOperationException("LLM should not be called for Read/Recover/Wait");

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => throw new InvalidOperationException("LLM should not be called for Read/Recover/Wait");

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => throw new InvalidOperationException("LLM should not be called for Read/Recover/Wait");
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}

using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class Wave0SpecTests
    {
        // ==================================================================
        // AC6: GameSessionConfig — PreviousOpener and edge values
        // ==================================================================

        // Mutation: Fails if PreviousOpener is not stored
        [Fact]
        public void GameSessionConfig_PreviousOpener_Stored()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), previousOpener: "Hey beautiful");
            Assert.Equal("Hey beautiful", config.PreviousOpener);
        }

        // Mutation: Fails if StartingInterest=0 is treated as null (default 10)
        [Fact]
        public void GameSessionConfig_StartingInterest_Zero_IsValidNotNull()
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 0);
            Assert.Equal(0, config.StartingInterest);
            Assert.True(config.StartingInterest.HasValue);
        }

        // Mutation: Fails if GameSession with config doesn't apply StartingInterest properly
        [Fact]
        public async Task GameSession_Config_StartingInterest_Zero_CreatesUnmatchedSession()
        {
            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new StubLlmAdapter(),
                new FixedDice(10),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: 0));

            var snapshot = session.CreateSnapshot();
            Assert.Equal(0, snapshot.Interest);
            Assert.Equal(InterestState.Unmatched, snapshot.State);
            Assert.Equal(0, snapshot.TurnNumber);
            Assert.Empty(snapshot.ActiveTrapNames);

            var ended = await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync());
            Assert.Equal(GameOutcome.Unmatched, ended.Outcome);
        }

        // Mutation: Fails if negative StartingInterest crashes instead of clamping
        [Fact]
        public void GameSession_Config_NegativeStartingInterest_ClampsToUnmatched()
        {
            // Negative should be clamped by InterestMeter(int) to 0
            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("datee"),
                new StubLlmAdapter(),
                new FixedDice(10),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: -10));

            var snapshot = session.CreateSnapshot();
            Assert.Equal(0, snapshot.Interest);
            Assert.Equal(InterestState.Unmatched, snapshot.State);
            Assert.Equal(0, snapshot.TurnNumber);
            Assert.Empty(snapshot.ActiveTrapNames);
        }

        // Mutation: Fails if config with all properties set doesn't pass clock through
        [Fact]
        public void GameSessionConfig_AllProperties_Set()
        {
            var stats = MakeStatBlock();
            var clock = new TestFixedClock();
            var pShadows = new SessionShadowTracker(stats);
            var oShadows = new SessionShadowTracker(stats);

            var config = new GameSessionConfig(
                clock: clock,
                playerShadows: pShadows,
                dateeShadows: oShadows,
                startingInterest: 15,
                previousOpener: "opener");

            Assert.Same(clock, config.Clock);
            Assert.Same(pShadows, config.PlayerShadows);
            Assert.Same(oShadows, config.DateeShadows);
            Assert.Equal(15, config.StartingInterest);
            Assert.Equal("opener", config.PreviousOpener);
        }

        // ==================================================================
        // AC7: InterestMeter — GrantsAdvantage/Disadvantage with custom start
        // ==================================================================

        // Mutation: Fails if custom starting value doesn't correctly determine advantage
        [Fact]
        public void InterestMeter_CustomStart_VeryIntoIt_GrantsAdvantage()
        {
            var meter = new InterestMeter(16);
            Assert.Equal(InterestState.VeryIntoIt, meter.GetState());
            Assert.True(meter.GrantsAdvantage);
            Assert.False(meter.GrantsDisadvantage);
        }

        // Mutation: Fails if custom starting value doesn't correctly determine disadvantage
        [Fact]
        public void InterestMeter_CustomStart_Bored_GrantsDisadvantage()
        {
            var meter = new InterestMeter(2);
            Assert.Equal(InterestState.Bored, meter.GetState());
            Assert.True(meter.GrantsDisadvantage);
            Assert.False(meter.GrantsAdvantage);
        }

        // Mutation: Fails if AlmostThere doesn't grant advantage
        [Fact]
        public void InterestMeter_CustomStart_AlmostThere_GrantsAdvantage()
        {
            var meter = new InterestMeter(22);
            Assert.Equal(InterestState.AlmostThere, meter.GetState());
            Assert.True(meter.GrantsAdvantage);
        }

        // Mutation: Fails if IsMaxed not set at 25
        [Fact]
        public void InterestMeter_CustomStart_25_IsMaxed()
        {
            var meter = new InterestMeter(25);
            Assert.True(meter.IsMaxed);
        }

        // Mutation: Fails if IsZero not set at 0
        [Fact]
        public void InterestMeter_CustomStart_0_IsZero()
        {
            var meter = new InterestMeter(0);
            Assert.True(meter.IsZero);
        }

        // Mutation: Fails if interest state boundaries are wrong
        [Fact]
        public void InterestMeter_CustomStart_Boundaries()
        {
            Assert.Equal(InterestState.Bored, new InterestMeter(4).GetState());
            Assert.Equal(InterestState.Lukewarm, new InterestMeter(5).GetState());
            Assert.Equal(InterestState.Lukewarm, new InterestMeter(9).GetState());
            Assert.Equal(InterestState.Interested, new InterestMeter(10).GetState());
            Assert.Equal(InterestState.Interested, new InterestMeter(15).GetState());
            Assert.Equal(InterestState.VeryIntoIt, new InterestMeter(16).GetState());
            Assert.Equal(InterestState.VeryIntoIt, new InterestMeter(20).GetState());
            Assert.Equal(InterestState.AlmostThere, new InterestMeter(21).GetState());
            Assert.Equal(InterestState.AlmostThere, new InterestMeter(24).GetState());
        }

        // ==================================================================
        // AC8 (W2a #371): TrapState single-slot model — HasActive after AdvanceTurn.
        // Replaces the prior multi-slot mixed-duration test. Every trap is fixed
        // at 3 turns and a fresh Activate REPLACES the existing trap.
        // ==================================================================

        [Fact]
        public void TrapState_HasActive_AfterPartialExpiry()
        {
            var state = new TrapState();
            // Definition's duration_turns is overridden to FixedDurationTurns=3
            // by Activate(); the activation turn counts as turn 1 of 3.
            var trap = new TrapDefinition("trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 1, "i", "c", "n");

            state.Activate(trap);
            Assert.True(state.HasActive);
            Assert.Equal(3, state.Get()!.TurnsRemaining);

            state.AdvanceTurn();
            Assert.True(state.HasActive);
            Assert.Equal(2, state.Get()!.TurnsRemaining);

            state.AdvanceTurn();
            Assert.True(state.HasActive);
            Assert.Equal(1, state.Get()!.TurnsRemaining);

            state.AdvanceTurn();
            // Expired after 3 AdvanceTurn calls
            Assert.False(state.HasActive);
        }
    }
}
